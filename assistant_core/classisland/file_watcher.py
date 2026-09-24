"""ClassIsland 配置文件监听（B 方案，兜底）
轮询解析 ClassIsland 的 profiles.json，自行计算当前时间点所属课程。
桥（IPC）不可用或 ClassIsland 运行时才启用。
"""
from __future__ import annotations

import json
import os
from datetime import datetime
from pathlib import Path

from ..config import config
from ..logger import get_logger
from .state import LessonState

log = get_logger("classisland.file")


def _find_classisland_dir() -> Path | None:
    """自动探测 ClassIsland 配置目录"""
    cfg = config.get("classisland_conf_dir")
    if cfg and Path(cfg).exists():
        return Path(cfg)
    candidates = [
        Path(os.environ.get("APPDATA", "")) / "ClassIsland",
        Path(os.environ.get("LOCALAPPDATA", "")) / "ClassIsland",
    ]
    for c in candidates:
        if (c / "profiles.json").exists():
            return c
    return None


def _multi_get(d: dict, *keys):
    """多个候选 key 中取第一个非空值"""
    for k in keys:
        if k in d and d[k] not in (None, "", []):
            return d[k]
    return None


def _parse_profiles(data: dict, now: datetime) -> LessonState | None:
    """从 profiles.json 解析当前课程"""
    try:
        plans = _multi_get(data, "ClassPlans", "classPlans", "ClassPlan") or []
        if isinstance(plans, dict):
            plans = [plans]
        # 找启用/当前的课表
        plan = None
        for p in plans:
            if p.get("IsCurrent") or p.get("IsEnabled") or p.get("isCurrent"):
                plan = p
                break
        if plan is None and plans:
            plan = plans[0]
        if not plan:
            return None

        week_key = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"][now.weekday()]
        week = _multi_get(plan, "Week", "week") or {}
        day_entry = None
        if isinstance(week, list):
            for w in week:
                if week_key in w:
                    day_entry = w[week_key]
                    break
        elif isinstance(week, dict):
            day_entry = week.get(week_key)

        if not day_entry:
            return None
        if isinstance(day_entry, dict):
            day_entry = [day_entry]

        # 时间布局（用于判定当前时间点属于哪一节）
        time_layouts = _multi_get(data, "TimeLayouts", "timeLayouts") or []
        if isinstance(time_layouts, dict):
            time_layouts = list(time_layouts.values())
        # 合并 day_entry 内的时间表与课程
        now_sec = now.hour * 3600 + now.minute * 60 + now.second

        for entry in day_entry:
            layout_name = _multi_get(entry, "TimeLayout", "timeLayout", "TimeLayoutName") or ""
            classes = _multi_get(entry, "Classes", "classes") or []
            # 找到对应时间布局
            tp = None
            for tl in time_layouts:
                name = _multi_get(tl, "Name", "name")
                if name == layout_name or not layout_name:
                    tp = tl
                    break
            time_points = (_multi_get(tp, "TimePoints", "timePoints") if tp else None) or []
            for i, cls in enumerate(classes):
                subject = _multi_get(cls, "Subject", "subject") or ""
                # 对应时间点
                t = time_points[i] if i < len(time_points) else None
                if not t:
                    continue
                start = _multi_get(t, "StartSecond", "startSecond", "Start") or 0
                end = _multi_get(t, "EndSecond", "endSecond", "End") or start
                try:
                    start, end = int(start), int(end)
                except Exception:
                    continue
                if start <= now_sec < end:
                    return LessonState(
                        state="OnClass",
                        subject=subject,
                        class_name=plan.get("Name", ""),
                        on_class_left_time=max(0, end - now_sec),
                        current_time=now.strftime("%H:%M:%S"),
                        source="file",
                        ts=now,
                    )
                if start <= now_sec < end + 60:
                    return LessonState(state="Breaking", subject=subject, source="file", ts=now)
        return None
    except Exception as e:
        log.debug(f"profiles 解析失败: {e}")
        return None


def _sec_to_hm(sec: int) -> str:
    """秒 → HH:MM"""
    sec = int(sec)
    return f"{sec // 3600:02d}:{sec % 3600 // 60:02d}"


def _day_classes(data: dict, now: datetime) -> list[dict]:
    """解析某一天的全部课程（复用 _parse_profiles 的 ClassPlans/TimeLayouts 解析思路）"""
    plans = _multi_get(data, "ClassPlans", "classPlans", "ClassPlan") or []
    if isinstance(plans, dict):
        plans = [plans]
    plan = None
    for p in plans:
        if p.get("IsCurrent") or p.get("IsEnabled") or p.get("isCurrent"):
            plan = p
            break
    if plan is None and plans:
        plan = plans[0]
    if not plan:
        return []

    week_key = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"][now.weekday()]
    week = _multi_get(plan, "Week", "week") or {}
    day_entry = None
    if isinstance(week, list):
        for w in week:
            if week_key in w:
                day_entry = w[week_key]
                break
    elif isinstance(week, dict):
        day_entry = week.get(week_key)
    if not day_entry:
        return []
    if isinstance(day_entry, dict):
        day_entry = [day_entry]

    time_layouts = _multi_get(data, "TimeLayouts", "timeLayouts") or []
    if isinstance(time_layouts, dict):
        time_layouts = list(time_layouts.values())
    now_sec = now.hour * 3600 + now.minute * 60 + now.second

    items: list[dict] = []
    for entry in day_entry:
        layout_name = _multi_get(entry, "TimeLayout", "timeLayout", "TimeLayoutName") or ""
        classes = _multi_get(entry, "Classes", "classes") or []
        # 找到该节课表对应的时间布局
        tp = None
        for tl in time_layouts:
            name = _multi_get(tl, "Name", "name")
            if name == layout_name or not layout_name:
                tp = tl
                break
        time_points = (_multi_get(tp, "TimePoints", "timePoints") if tp else None) or []
        for i, cls in enumerate(classes):
            subject = _multi_get(cls, "Subject", "subject") or ""
            t = time_points[i] if i < len(time_points) else None
            if not t or not subject:
                continue
            start = _multi_get(t, "StartSecond", "startSecond", "Start") or 0
            end = _multi_get(t, "EndSecond", "endSecond", "End") or start
            try:
                start, end = int(start), int(end)
            except Exception:
                continue
            if now_sec >= end:
                state = "done"
            elif now_sec >= start:
                state = "now"
            else:
                state = "upcoming"
            items.append({
                "index": len(items) + 1,
                "subject": subject,
                "start": _sec_to_hm(start),
                "end": _sec_to_hm(end),
                "state": state,
            })
    items.sort(key=lambda x: x["start"])
    for i, it in enumerate(items, 1):
        it["index"] = i
    return items


def today_schedule(now: datetime | None = None) -> dict:
    """今日课程时间线：{"date": "2026-09-24", "items": [{index,subject,start,end,state}]}

    解析不到（ClassIsland 未安装/未配置）时返回 items 为空，不抛异常。
    """
    now = now or datetime.now()
    out: dict = {"date": now.strftime("%Y-%m-%d"), "items": []}
    try:
        d = _find_classisland_dir()
        if not d:
            return out
        f = d / "profiles.json"
        if not f.exists():
            return out
        data = json.loads(f.read_text(encoding="utf-8"))
        out["items"] = _day_classes(data, now)
    except Exception as e:
        log.debug(f"今日课表解析失败: {e}")
    return out


class FileWatcher:
    """轮询 profiles.json 计算当前课程"""

    def __init__(self, interval: float = 3.0):
        self.interval = interval
        self._dir = _find_classisland_dir()
        self._last_mtime = 0.0
        if self._dir:
            log.info(f"ClassIsland 配置目录: {self._dir}")

    def available(self) -> bool:
        return self._dir is not None and (self._dir / "profiles.json").exists()

    def get_state(self) -> LessonState | None:
        if not self.available():
            return None
        f = self._dir / "profiles.json"
        try:
            if f.stat().st_mtime != self._last_mtime:
                self._last_mtime = f.stat().st_mtime
                self._data = json.loads(f.read_text(encoding="utf-8"))
        except Exception:
            if not hasattr(self, "_data"):
                return None
        return _parse_profiles(getattr(self, "_data", {}), datetime.now())
