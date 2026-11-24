"""同步、线程安全的事件总线。

事件名严格按契约第 5 节：classisland.state / perception.db_sample /
perception.asr_segment / session.start / session.end / session.overtime。
"""
from __future__ import annotations

import threading
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any, Callable

from . import get_logger

log = get_logger("events")


@dataclass
class Event:
    """一条事件：type 事件名，data 负载，ts 产生时间。"""
    type: str
    data: dict[str, Any] = field(default_factory=dict)
    ts: datetime = field(default_factory=datetime.now)

    def get(self, key: str, default: Any = None) -> Any:
        return self.data.get(key, default)

    def __repr__(self) -> str:
        return f"<Event {self.type} {self.data}>"


class EventBus:
    """订阅/发布（同步派发，单处理器异常不影响其它处理器）。"""

    def __init__(self) -> None:
        self._subs: dict[str, list[Callable[[Event], None]]] = {}
        self._lock = threading.RLock()

    def on(self, event_type: str, handler: Callable[[Event], None]) -> None:
        with self._lock:
            self._subs.setdefault(event_type, []).append(handler)

    def off(self, event_type: str, handler: Callable[[Event], None]) -> None:
        with self._lock:
            handlers = self._subs.get(event_type, [])
            if handler in handlers:
                handlers.remove(handler)

    def emit(self, event_type: str, **data: Any) -> Event:
        """同步派发；处理器异常只记日志，绝不影响发布者。"""
        event = Event(event_type, data)
        with self._lock:
            handlers = list(self._subs.get(event_type, []))
        for handler in handlers:
            try:
                handler(event)
            except Exception as exc:
                log.exception(f"事件处理器异常 [{event_type}]: {exc}")
        return event

    def clear(self, event_type: str | None = None) -> None:
        with self._lock:
            if event_type is None:
                self._subs.clear()
            else:
                self._subs.pop(event_type, None)


# 契约事件名常量（避免各处硬编码拼写不一致）
CLASSISLAND_STATE = "classisland.state"
PERCEPTION_DB_SAMPLE = "perception.db_sample"
PERCEPTION_ASR_SEGMENT = "perception.asr_segment"
SESSION_START = "session.start"
SESSION_END = "session.end"
SESSION_OVERTIME = "session.overtime"

bus = EventBus()
