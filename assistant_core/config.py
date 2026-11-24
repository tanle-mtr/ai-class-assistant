"""配置 / 目录 / 硬件探测。

单例 ``config`` 在导入时创建（只建目录、读配置，不主动回写磁盘，避免改动用户既有数据）。
兼容 v1 的 ``data/config/config.json``：缺失的键用 DEFAULTS 补齐。
"""
from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from . import get_logger

log = get_logger("config")

# 项目根目录 = assistant_core 的上一级
PROJECT_ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = PROJECT_ROOT / "data"

# 数据子目录（faces 用于人脸建档，保留 v1 数据兼容）
DIRS: dict[str, Path] = {
    "config": DATA_DIR / "config",
    "exports": DATA_DIR / "exports",
    "recordings": DATA_DIR / "recordings",
    "seats": DATA_DIR / "seats",
    "courseware": DATA_DIR / "courseware",
    "logs": DATA_DIR / "logs",
    "textbooks": DATA_DIR / "textbooks",
    "faces": DATA_DIR / "faces",
}
CONFIG_FILE = DIRS["config"] / "config.json"

# 契约第 3 节：配置键（与 v1 兼容）
DEFAULTS: dict[str, Any] = {
    "model": "",
    "vision_model": "",
    "asr_model": "small",
    "grade": "",
    "region": "",
    "school": "",
    "class_name": "",
    "classisland_bridge_port": 18761,
    "classisland_conf_dir": "",
    "web_port": 18760,
    "monitor_enabled": True,
    "record_camera": True,
    "record_audio": True,
    "monitor_always_record": False,   # 隐私默认：无人上课不落盘
    "retention_days": 14,
    "screen_analysis_interval": 30,
    "overtime_check_seconds": 120,
    "overtime_screen_threshold": 0.6,
    "overtime_seated_ratio": 0.7,
    "db_sample_interval": 1,
    "db_windows": 60,
    "openlist_port": 5244,
    "tunnel_enabled": False,
    "tunnel_token": "",
    "public_ip_auto": True,
    "courseware_subjects": [],
    "teacher_name": "",
    "output_language": "zh-CN",
}


@dataclass
class HardwareInfo:
    """硬件摘要，字段与 /api/models.hardware 一一对应。"""
    cpu: str = ""
    cores: int = 0
    ram_gb: float = 0.0
    gpu: str = ""
    vram_gb: float = 0.0

    @property
    def gpu_name(self) -> str:
        return self.gpu

    @property
    def has_nvidia_gpu(self) -> bool:
        name = (self.gpu or "").lower()
        return any(k in name for k in ("nvidia", "geforce", "rtx", "quadro"))

    def model_advice(self) -> str:
        """按硬件给出模型规格建议。"""
        if self.has_nvidia_gpu and self.vram_gb >= 8:
            return "建议 7b~14b 级模型（如 qwen2.5:7b / 14b）"
        if self.ram_gb >= 16:
            return "无独显/NVIDIA 显存较小，建议 1.5b~3b 级模型（CPU 推理）"
        return "内存偏小，建议 0.6b~1.5b 级轻量模型"

    def to_dict(self) -> dict[str, Any]:
        return {
            "cpu": self.cpu,
            "cores": self.cores,
            "ram_gb": self.ram_gb,
            "gpu": self.gpu,
            "vram_gb": self.vram_gb,
            "advice": self.model_advice(),
        }


def detect_hardware() -> HardwareInfo:
    """探测 CPU / 内存 / GPU；任何失败都返回合理默认值，不抛异常。"""
    info = HardwareInfo()
    try:
        import psutil
        info.cpu = platform.processor() or platform.machine() or ""
        info.cores = psutil.cpu_count(logical=False) or os.cpu_count() or 0
        info.ram_gb = round(psutil.virtual_memory().total / 1024 ** 3, 1)
    except Exception:
        info.cores = os.cpu_count() or 0  # psutil 缺失时的必兜底路径（v1 曾漏 import os）
    if sys.platform == "win32":
        try:
            out = subprocess.run(
                ["powershell", "-NoProfile", "-Command",
                 "(Get-CimInstance Win32_VideoController | Select-Object -First 1 Name, AdapterRAM | ConvertTo-Json -Compress)"],
                capture_output=True, text=True, timeout=12,
            ).stdout.strip()
            if out:
                gpu = json.loads(out)
                info.gpu = str(gpu.get("Name", ""))
                info.vram_gb = round(float(gpu.get("AdapterRAM", 0) or 0) / 1024 ** 3, 1)
        except Exception:
            pass
    return info


def is_vision_model(name: str) -> bool:
    """粗略判断模型是否多模态（视觉）。"""
    low = (name or "").lower()
    return any(k in low for k in ("vl", "vision", "llava", "gemma3", "minicpm", "moondream", "bakllava"))


def query_ollama_models() -> list[dict[str, Any]]:
    """执行 ollama list，返回 [{name, id, size, modified}]；未安装返回空列表。"""
    try:
        out = subprocess.run(
            ["ollama", "list"], capture_output=True, text=True, timeout=15,
        ).stdout
        lines = [line.strip() for line in out.splitlines() if line.strip()]
        if not lines or not lines[0].upper().startswith("NAME"):
            return []
        models: list[dict[str, Any]] = []
        for line in lines[1:]:
            parts = line.split()
            if len(parts) >= 2:
                models.append({
                    "name": parts[0],
                    "id": parts[1] if len(parts) > 1 else "",
                    "size": parts[2] if len(parts) > 2 else "",
                    "modified": " ".join(parts[3:]) if len(parts) > 3 else "",
                })
        return models
    except FileNotFoundError:
        log.info("未检测到 ollama（model 列表为空）")
        return []
    except Exception as exc:
        log.warning(f"ollama list 失败: {exc}")
        return []


class Config:
    """配置单例：get / set / dir + JSON 持久化。"""

    def __init__(self) -> None:
        for d in DIRS.values():
            try:
                d.mkdir(parents=True, exist_ok=True)
            except Exception:
                pass
        self._data: dict[str, Any] = json.loads(json.dumps(DEFAULTS))
        self.hardware = detect_hardware()
        self.load()  # 只读补齐，不回写

    # ---------- 持久化 ----------
    def load(self) -> None:
        """读取 config.json（v1 兼容）：只接受已知键，缺键留默认值。"""
        if not CONFIG_FILE.exists():
            return
        try:
            saved = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
            for key, value in saved.items():
                if key in DEFAULTS:
                    self._data[key] = value
        except Exception as exc:
            log.warning(f"读取配置失败（使用默认值）: {exc}")

    def save(self) -> None:
        try:
            CONFIG_FILE.parent.mkdir(parents=True, exist_ok=True)
            CONFIG_FILE.write_text(
                json.dumps(self._data, ensure_ascii=False, indent=2), encoding="utf-8",
            )
        except Exception as exc:
            log.warning(f"保存配置失败: {exc}")

    # ---------- 访问 ----------
    def get(self, key: str, default: Any = None) -> Any:
        return self._data.get(key, default)

    def set(self, key: str, value: Any, *, persist: bool = True) -> None:
        self._data[key] = value
        if persist:
            self.save()

    def update(self, kv: dict[str, Any], *, persist: bool = True) -> None:
        for key, value in kv.items():
            if key in DEFAULTS:
                self._data[key] = value
        if persist:
            self.save()

    def dir(self, key: str) -> Path:
        """取数据子目录（不存在则创建）。"""
        d = DIRS.get(key)
        if d is None:
            raise KeyError(f"未知目录键: {key}")
        try:
            d.mkdir(parents=True, exist_ok=True)
        except Exception:
            pass
        return d

    def as_dict(self) -> dict[str, Any]:
        return dict(self._data)

    # ---------- 模型辅助 ----------
    def pick_model(self, name: str) -> None:
        self.set("model", name)

    def ensure_model(self) -> str:
        """主力模型未配置时，从 ollama list 自动挑一个纯文本模型。"""
        if self._data.get("model"):
            return str(self._data["model"])
        models = query_ollama_models()
        if not models:
            return ""
        pool = [m for m in models if not is_vision_model(m["name"])] or models

        def size_gb(m: dict[str, Any]) -> float:
            try:
                size = str(m.get("size", "")).upper()
                if size.endswith("GB"):
                    return float(size[:-2])
                if size.endswith("MB"):
                    return float(size[:-2]) / 1024
            except Exception:
                pass
            return 0.0

        pool.sort(key=size_gb, reverse=True)
        chosen = pool[0]["name"]
        self.set("model", chosen)
        if not self._data.get("vision_model"):
            for m in models:
                if is_vision_model(m["name"]):
                    self.set("vision_model", m["name"])
                    break
        return chosen


config = Config()
