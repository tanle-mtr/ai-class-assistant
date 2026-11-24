"""ClassIsland 配置文件直读兜底（桥不可用时使用）。

轮询解析 profiles.json，自行计算当前时间点属于哪一节课。
ClassIsland 未安装 / 未配置 / 文件损坏时返回空结果，绝不抛异常。
"""
from __future__ import annotations

import json
import os
from datetime import datetime
from pathlib import Path
from typing import Any

from .. import get_logger
from ..config import config
from ..models import LessonState, lesson_type_of

log = get_logger("classisland.watcher")

WEEKDAYS = ("Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday")


def find_config_dir() -> Path | None:
    configured = config.get("classisland_conf_dir") or ""
    if configured and Path(configured).exists():
        return Path(configured)
    for candidate in (
        Path(os.environ.get("APPDATA", "")) / "ClassIsland",
        Path(os.environ.get("LOCALAPPDATA", "")) / "ClassIsland",
    ):
        if (candidate / "profiles.json").exists():
            return candidate
    return None


def _multi_get(data: dict[str, Any], *keys: str) -> Any:
    for key in keys:
        value = data.get(key)
        if value not in (None, "", []):
            return value
    return None


def _active_plan(data: dict[str, Any]) -> dict[str, Any] | None:
    plans = _multi_get(data, "ClassPlans", "classPlans", "ClassPlan") or []
    if isinstance(plans, dict):
        plans = [plans]
    for plan in plans:
        if plan.get("IsCurrent") or plan.get("IsEnabled") or plan.get("isCurrent"):
            return plan
    return plans[0] if plans else None


def _day_entries(plan: dict[str, Any], now: datetime) -> list[dict[str, Any]]:
    week = _multi_get(plan, "Week", "week") or {}
    day = None
    if isinstance(week, list):
        for item in week:
            if WEEKDAYS[now.weekday()] in item:
                day = item[WEEKDAYS[now.weekday()]]
                break
    elif isinstance(week, dict):
        day = week.get(WEEKDAYS[now.weekday()])
    if not day:
        return []
    return day if isinstance(day, list) else [day]


def _time_points(data: dict[str, Any], entry: dict[str, Any]) -> list[dict[str, Any]]:
    layouts = _multi_get(data, "TimeLayouts", "timeLayouts") or []
    if isinstance(layouts, dict):
        layouts = list(layouts.values())
    layout_name = _multi_get(entry, "TimeLayout", "timeLayout", "TimeLayoutName") or ""
    layout = None
    for item in layouts:
        name = _multi_get(item, "Name", "name")
        if name == layout_name or not layout_name:
            layout = item
            break
    return (_multi_get(layout, "TimePoints", "timePoints") if layout else None) or []


def _sec_to_hm(seconds: int) -> str:
    seconds = int(seconds)
    return f"{seconds // 3600:02d}:{seconds % 3600 // 60:02d}"


def _items(data: dict[str, Any], now: datetime) -> list[dict[str, Any]]:
    plan = _active_plan(data)
    if not plan:
        return []
    now_seconds = now.hour * 3600 + now.minute * 60 + now.second
    items: list[dict[str, Any]] = []
    for entry in _day_entries(plan, now):
        classes = _multi_get(entry, "Classes", "classes") or []
        points = _time_points(data, entry)
        for index, lesson in enumerate(classes):
            subject = str(_multi_get(lesson, "Subject", "subject") or "")
            point = points[index] if index < len(points) else None
            if not point or not subject:
                continue
            start = int(_multi_get(point, "StartSecond", "startSecond", "Start") or 0)
            end = int(_multi_get(point, "EndSecond", "endSecond", "End") or start)
            state = "done" if now_seconds >= end else ("now" if now_seconds >= start else "upcoming")
            items.append({
                "index": len(items) + 1,
                "subject": subject,
                "start": _sec_to_hm(start),
                "end": _sec_to_hm(end),
                "type": lesson_type_of(subject),
                "state": state,
                "left": max(0, end - now_seconds) if state == "now" else 0,
            })
    items.sort(key=lambda item: item["start"])
    for index, item in enumerate(items, 1):
        item["index"] = index
    return items


def today_schedule(now: datetime | None = None) -> dict[str, Any]:
    """今日课表：{date, items:[{subject,start,end,type,...}], source}。"""
    now = now or datetime.now()
    result: dict[str, Any] = {"date": now.strftime("%Y-%m-%d"), "items": [], "source": "classisland"}
    try:
        directory = find_config_dir()
        if not directory:
            result["source"] = "none"
            return result
        profile = directory / "profiles.json"
        if not profile.exists():
            result["source"] = "none"
            return result
        data = json.loads(profile.read_text(encoding="utf-8"))
        result["items"] = _items(data, now)
        if not result["items"]:
            result["source"] = "none"
    except Exception as exc:
        log.debug(f"课表解析跳过: {exc}")
        result["source"] = "none"
    return result


class FileWatcher:
    """轮询 profiles.json 得到当前课程状态。"""

    def __init__(self, interval: float = 3.0) -> None:
        self.interval = interval
        self._dir = find_config_dir()
        self._data: dict[str, Any] = {}
        self._last_mtime = 0.0
        if self._dir:
            log.info(f"ClassIsland 配置目录: {self._dir}")

    def available(self) -> bool:
        return self._dir is not None and (self._dir / "profiles.json").exists()

    def get_state(self) -> LessonState | None:
        if not self.available():
            return None
        try:
            profile = self._dir / "profiles.json"  # type: ignore[union-attr]
            mtime = profile.stat().st_mtime
            if mtime != self._last_mtime:
                self._last_mtime = mtime
                self._data = json.loads(profile.read_text(encoding="utf-8"))
        except Exception:
            if not self._data:
                return None
        now = datetime.now()
        now_seconds = now.hour * 3600 + now.minute * 60 + now.second
        plan = _active_plan(self._data)
        if not plan:
            return None
        for entry in _day_entries(plan, now):
            classes = _multi_get(entry, "Classes", "classes") or []
            points = _time_points(self._data, entry)
            for index, lesson in enumerate(classes):
                subject = str(_multi_get(lesson, "Subject", "subject") or "")
                point = points[index] if index < len(points) else None
                if not point:
                    continue
                start = int(_multi_get(point, "StartSecond", "startSecond", "Start") or 0)
                end = int(_multi_get(point, "EndSecond", "endSecond", "End") or start)
                if start <= now_seconds < end:
                    return LessonState(
                        state="OnClass", subject=subject,
                        class_name=str(plan.get("Name", "") or ""),
                        on_class_left_time=max(0, end - now_seconds),
                        current_time=now.strftime("%H:%M:%S"), source="file", ts=now,
                        lesson={"subject": subject, "start": _sec_to_hm(start), "end": _sec_to_hm(end)},
                    )
                if start <= now_seconds < end + 60:
                    return LessonState(
                        state="Breaking", subject=subject, source="file", ts=now,
                        lesson={"subject": subject, "start": _sec_to_hm(start), "end": _sec_to_hm(end)},
                    )
        return None
