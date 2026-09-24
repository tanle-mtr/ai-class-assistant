"""AI 课堂助手 - 配置管理
- 默认数据目录结构
- 用户配置（JSON 持久化）
- Ollama 模型自动探测（ollama list）
- 硬件探测（CPU/GPU/内存）
"""
from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

from .logger import get_logger

log = get_logger("config")

# 项目根目录（assistant_core 的上一级）
PROJECT_ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = PROJECT_ROOT / "data"

# 各数据子目录
DIRS = {
    "exports": DATA_DIR / "exports",        # 导出根目录（OpenList 挂载点）
    "recordings": DATA_DIR / "recordings",  # 监控录像（14 天）
    "textbooks": DATA_DIR / "textbooks",    # 教科书知识库
    "faces": DATA_DIR / "faces",            # 学生人脸底库
    "config": DATA_DIR / "config",          # 软件配置
    "logs": DATA_DIR / "logs",              # 日志
    "seats": DATA_DIR / "seats",            # 座位表
    "courseware": DATA_DIR / "courseware",  # 课件上传根目录（按科目分类）
}

CONFIG_FILE = DIRS["config"] / "config.json"

DEFAULT_CONFIG: dict = {
    "model": "",                 # 主力模型（由 ollama list 选择）
    "vision_model": "",          # 识图模型（视觉模型）
    "asr_model": "small",        # faster-whisper 模型规格：tiny/base/small/medium
    "grade": "",                 # 年级（首启询问）
    "region": "",                # 地区（自动定位）
    "school": "",
    "class_name": "",
    # ClassIsland
    "classisland_bridge_port": 18761,   # C# 桥本地 HTTP 端口
    "classisland_conf_dir": "",         # ClassIsland 配置目录（自动探测）
    # Web 控制台
    "web_port": 18760,                  # 网页控制台端口（端口被占用时自动顺延）
    # 感知
    "monitor_enabled": True,            # 监控默认开启
    "record_camera": True,              # 监控录摄像头
    "record_audio": True,               # 监控录音频
    "retention_days": 14,               # 监控录像保留天数
    "screen_analysis_interval": 30,     # 屏幕分析间隔（秒）
    # 拖堂判定
    "overtime_check_seconds": 120,      # 下课事件后持续观察时长（秒）
    "overtime_screen_threshold": 0.6,   # 屏幕活跃判定阈值（画面变化比例）
    "overtime_seated_ratio": 0.7,       # 多数学生未离座比例阈值
    # 分贝监控
    "db_sample_interval": 1,            # 分贝采样间隔（秒）
    "db_windows": 60,                   # 每分钟聚合窗口
    # 网络
    "openlist_port": 5244,              # OpenList 默认端口
    "tunnel_enabled": False,            # cf 隧道是否已配置
    "tunnel_token": "",                 # Cloudflare tunnel token
    "public_ip_auto": True,             # 自动查询公网 IP
    # 课件上传
    "courseware_subjects": [],          # 已存在的科目列表（自动维护）
    # 输出
    "teacher_name": "",
    "output_language": "zh-CN",
}


@dataclass
class HardwareInfo:
    cpu: str = ""
    cores: int = 0
    ram_gb: float = 0
    gpu_name: str = ""
    gpu_vram_gb: float = 0
    has_nvidia_gpu: bool = False

    def model_advice(self) -> str:
        """根据硬件给出模型规格建议"""
        if self.has_nvidia_gpu and self.gpu_vram_gb >= 8:
            return "建议 7b~14b 级模型（如 qwen2.5:7b / 14b）"
        if self.ram_gb >= 16:
            return "无独显/NVIDIA 显存较小，建议 1.5b~3b 级模型（CPU 推理）"
        return "内存偏小，建议 0.6b~1.5b 级轻量模型"


def detect_hardware() -> HardwareInfo:
    """探测 CPU/内存/GPU（跨平台）"""
    info = HardwareInfo()
    try:
        import psutil
        info.cpu = platform.processor() or ""
        info.cores = psutil.cpu_count(logical=False) or os.cpu_count() or 0
        info.ram_gb = round(psutil.virtual_memory().total / 1024**3, 1)
    except Exception:
        info.cores = os.cpu_count() or 0
    # GPU（Windows：PowerShell CIM）
    try:
        if sys.platform == "win32":
            out = subprocess.run(
                ["powershell", "-NoProfile", "-Command",
                 "(Get-CimInstance Win32_VideoController | Select-Object -First 1 Name, AdapterRAM | ConvertTo-Json -Compress)"],
                capture_output=True, text=True, timeout=15,
            ).stdout.strip()
            if out:
                gpu = json.loads(out)
                info.gpu_name = str(gpu.get("Name", ""))
                vram = int(gpu.get("AdapterRAM", 0) or 0)
                info.gpu_vram_gb = round(vram / 1024**3, 1)
                name = info.gpu_name.lower()
                if "nvidia" in name or "geforce" in name or "rtx" in name or "quadro" in name:
                    info.has_nvidia_gpu = True
    except Exception:
        pass
    return info


def query_ollama_models() -> list[dict]:
    """执行 `ollama list`，返回 [{name, size, ...}]"""
    try:
        out = subprocess.run(
            ["ollama", "list"], capture_output=True, text=True, timeout=15,
        ).stdout
        lines = [l.strip() for l in out.splitlines() if l.strip()]
        if not lines or lines[0].startswith("NAME") is False:
            return []
        header = [h.strip() for h in lines[0].split()]
        models = []
        for line in lines[1:]:
            parts = line.split()
            if len(parts) >= 4:
                models.append({
                    "name": parts[0],
                    "id": parts[1],
                    "size": parts[2],
                    "modified": " ".join(parts[3:]),
                })
        return models
    except FileNotFoundError:
        log.warning("未检测到 ollama，请先安装 Ollama")
        return []
    except Exception as e:
        log.warning(f"ollama list 失败: {e}")
        return []


def is_vision_model(model_name: str) -> bool:
    """粗略判断模型是否支持视觉（多模态）"""
    name = model_name.lower()
    return any(k in name for k in ("vl", "vision", "llava", "gemma3", "minicpm", "moondream", "bakllava", "qwen2.5-vl", "qwen3-vl"))


class Config:
    """用户配置单例，JSON 持久化"""

    def __init__(self):
        for d in DIRS.values():
            d.mkdir(parents=True, exist_ok=True)
        self._data: dict = json.loads(json.dumps(DEFAULT_CONFIG))
        self.load()
        self.hardware = detect_hardware()

    def load(self):
        if CONFIG_FILE.exists():
            try:
                saved = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
                for k, v in saved.items():
                    if k in DEFAULT_CONFIG:
                        self._data[k] = v
            except Exception as e:
                log.error(f"读取配置失败: {e}")
        self.save()

    def save(self):
        try:
            CONFIG_FILE.write_text(
                json.dumps(self._data, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
        except Exception as e:
            log.error(f"保存配置失败: {e}")

    def get(self, key: str, default=None):
        return self._data.get(key, default)

    def set(self, key: str, value):
        self._data[key] = value
        self.save()

    def update(self, kv: dict):
        for k, v in kv.items():
            if k in DEFAULT_CONFIG:
                self._data[k] = v
        self.save()

    def ollama_models(self) -> list[dict]:
        return query_ollama_models()

    def pick_model(self, name: str):
        """用户选择主力模型"""
        self.set("model", name)

    def ensure_model(self):
        """若主力模型未配置，自动从 ollama list 选择最合适的"""
        if self._data.get("model"):
            return self._data["model"]
        models = query_ollama_models()
        if not models:
            return ""
        # 优先纯文本大一点的，其次视觉模型留给 vision_model
        candidates = [m for m in models if not is_vision_model(m["name"])]
        pool = candidates or models
        # 简单启发式：选 size 最大的
        def size_gb(m: dict) -> float:
            try:
                s = m["size"]
                if s.endswith("GB"):
                    return float(s[:-2])
                if s.endswith("MB"):
                    return float(s[:-2]) / 1024
            except Exception:
                pass
            return 0
        pool.sort(key=size_gb, reverse=True)
        chosen = pool[0]["name"]
        self.set("model", chosen)
        # 若有视觉模型则自动配为识图模型
        v = [m for m in models if is_vision_model(m["name"])]
        if v and not self._data.get("vision_model"):
            self.set("vision_model", v[0]["name"])
        return chosen

    def dir(self, key: str) -> Path:
        return DIRS[key]


config = Config()
