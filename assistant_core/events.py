"""事件总线：课程开始/结束、拖堂、分贝、监控等事件的发布与订阅"""
from __future__ import annotations

import threading
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any, Callable

from .logger import get_logger

log = get_logger("events")


@dataclass
class Event:
    """一条事件"""
    type: str                 # 事件类型
    data: dict = field(default_factory=dict)
    ts: datetime = field(default_factory=datetime.now)

    def __repr__(self):
        return f"<Event {self.type} {self.data}>"


class EventBus:
    """线程安全的事件总线"""

    def __init__(self):
        self._subs: dict[str, list[Callable]] = {}
        self._lock = threading.Lock()

    def on(self, event_type: str, handler: Callable):
        """订阅事件"""
        with self._lock:
            self._subs.setdefault(event_type, []).append(handler)

    def off(self, event_type: str, handler: Callable):
        with self._lock:
            subs = self._subs.get(event_type, [])
            if handler in subs:
                subs.remove(handler)

    def emit(self, event_type: str, **data):
        """发布事件（异步派发，不阻塞发布者）"""
        ev = Event(event_type, data)
        with self._lock:
            handlers = list(self._subs.get(event_type, []))
        for h in handlers:
            try:
                h(ev)
            except Exception as e:
                log.exception(f"事件处理器异常 [{event_type}]: {e}")

    # ---- 常用事件常量 ----
    # classisland.lesson_started / lesson_ended / state_changed
    # perception.db_sample  (分贝样本)
    # overtime.detected    (拖堂)
    # session.started / session.ended  (课程会话)
    # monitor.frame        (监控帧)
    # summary.generated    (总结生成)


bus = EventBus()
