"""ClassIsland IPC 桥客户端
优先通过 C# 桥（tray/ClassIslandBridge）读取实时状态：
    GET  http://127.0.0.1:<port>/state  -> LessonState JSON
桥不可用时返回 None，由上层退到文件监听。
"""
from __future__ import annotations

import json
from datetime import datetime

import requests

from ..config import config
from ..logger import get_logger
from .state import LessonState

log = get_logger("classisland.bridge")


class BridgeClient:
    """连接 C# 桥的本地 HTTP 服务"""

    def __init__(self, port: int | None = None):
        self.port = port or config.get("classisland_bridge_port", 18761)
        self.base = f"http://127.0.0.1:{self.port}"

    def ping(self) -> bool:
        try:
            r = requests.get(f"{self.base}/ping", timeout=2)
            return r.status_code == 200
        except Exception:
            return False

    def get_state(self) -> LessonState | None:
        """拉取当前课程状态；失败返回 None"""
        try:
            r = requests.get(f"{self.base}/state", timeout=3)
            if r.status_code != 200:
                return None
            data = r.json()
            return LessonState(
                state=data.get("state", "Unknown"),
                subject=data.get("subject", ""),
                class_name=data.get("class_name", ""),
                on_class_left_time=int(data.get("on_class_left_time", 0) or 0),
                current_time=data.get("current_time", ""),
                source="ipc",
                ts=datetime.now(),
            )
        except Exception as e:
            log.debug(f"桥状态读取失败: {e}")
            return None
