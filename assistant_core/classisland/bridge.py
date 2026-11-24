"""ClassIsland IPC 桥客户端（轮询 C# 桥的本地 HTTP 18761）。

GET /state     -> 当前课程状态
GET /schedule  -> 今日课表
桥未启动 / 无 ClassIsland 时返回 None / 空结果，上层退到 watcher。
"""
from __future__ import annotations

from datetime import datetime
from typing import Any

import requests

from .. import get_logger
from ..config import config
from ..models import LessonState

log = get_logger("classisland.bridge")


class BridgeClient:
    def __init__(self, port: int | None = None) -> None:
        self.port = int(port or config.get("classisland_bridge_port", 18761) or 18761)
        self.base = f"http://127.0.0.1:{self.port}"

    def ping(self) -> bool:
        try:
            return requests.get(f"{self.base}/ping", timeout=2).status_code == 200
        except Exception:
            return False

    def get_state(self) -> LessonState | None:
        """拉取当前课程状态；失败返回 None（不抛异常）。"""
        try:
            response = requests.get(f"{self.base}/state", timeout=3)
            if response.status_code != 200:
                return None
            data = response.json()
            if not isinstance(data, dict):
                return None
            lesson = data.get("lesson") or {}
            return LessonState(
                state=str(data.get("state", "Unknown") or "Unknown"),
                subject=str(data.get("subject") or lesson.get("subject") or ""),
                class_name=str(data.get("class_name") or ""),
                on_class_left_time=int(data.get("on_class_left_time", 0) or 0),
                current_time=str(data.get("current_time", "") or ""),
                source="ipc",
                ts=datetime.now(),
                lesson=lesson if isinstance(lesson, dict) else {},
            )
        except Exception as exc:
            log.debug(f"桥状态读取跳过: {exc}")
            return None

    def get_schedule(self) -> dict[str, Any] | None:
        """拉取桥侧今日课表；失败返回 None。"""
        try:
            response = requests.get(f"{self.base}/schedule", timeout=3)
            if response.status_code != 200:
                return None
            data = response.json()
            return data if isinstance(data, dict) else None
        except Exception as exc:
            log.debug(f"桥课表读取跳过: {exc}")
            return None
