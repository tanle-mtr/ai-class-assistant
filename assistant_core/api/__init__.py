"""Web API 层：create_app(ctx) 注册 29 个端点 + 静态托管 webui/dist。"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Callable


@dataclass
class AppContext:
    """由 cli 装配后注入各路由模块的应用上下文。"""
    session_manager: Any = None
    audio: Any = None
    camera: Any = None
    screen: Any = None
    bridge: Any = None
    watcher: Any = None
    face_registry: Any = None
    summarizer: Any = None
    summary_service: Any = None
    exporter: Any = None
    seatmap: Any = None
    textbook: Any = None
    courseware: Any = None
    tunnel: Any = None
    shutdown: Callable[[], None] | None = None
