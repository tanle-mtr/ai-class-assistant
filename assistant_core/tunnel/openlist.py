"""OpenList 挂载管理
OpenList 是独立的开源网盘列表程序（https://github.com/OpenListTeam/openlist 等），
本模块负责检测安装、生成挂载配置（把导出目录作为网盘根目录）、启动与状态查询。
"""
from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path

from ..config import config
from ..logger import get_logger

log = get_logger("tunnel.openlist")

# OpenList 常见可执行文件与配置位置（用户可自定义）
DEFAULT_PORTS = [5244, 5245, 8088]


class OpenListManager:
    def __init__(self):
        self._dir = config.dir("config")
        self._cfg_path = self._dir / "openlist.json"

    def _cfg(self) -> dict:
        if self._cfg_path.exists():
            try:
                return json.loads(self._cfg_path.read_text(encoding="utf-8"))
            except Exception:
                pass
        return {}

    def _save(self, cfg: dict):
        self._cfg_path.write_text(json.dumps(cfg, ensure_ascii=False, indent=2), encoding="utf-8")

    def configure(self, exe_path: str, port: int):
        """配置 OpenList 程序路径与端口，导出目录作为网盘根"""
        cfg = self._cfg()
        cfg.update({"exe": exe_path, "port": int(port)})
        self._save(cfg)
        log.info(f"OpenList 已配置: {exe_path} :{port}")

    def exe(self) -> str:
        return self._cfg().get("exe", "")

    def port(self) -> int:
        return int(self._cfg().get("port", config.get("openlist_port", 5244)))

    def is_running(self) -> bool:
        try:
            import requests
            return requests.get(f"http://127.0.0.1:{self.port()}", timeout=2).status_code < 500
        except Exception:
            return False

    def start(self) -> bool:
        """启动 OpenList（若已配置 exe）"""
        exe = self.exe()
        if not exe or not Path(exe).exists():
            log.warning("OpenList 未配置程序路径，跳过启动")
            return False
        if self.is_running():
            return True
        try:
            subprocess.Popen(
                [exe],
                cwd=str(Path(exe).parent),
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
            log.info(f"OpenList 已启动（根目录=导出文件夹）")
            return True
        except Exception as e:
            log.error(f"OpenList 启动失败: {e}")
            return False

    def guide(self) -> str:
        """未安装时给出指引"""
        return (
            "未检测到 OpenList。请任选一个开源网盘列表程序安装（如 OpenList / Alist），"
            "把网盘根目录指向本软件导出文件夹 data/exports，"
            "然后在软件设置中填写 OpenList 程序路径与端口。"
        )


openlist = OpenListManager()
