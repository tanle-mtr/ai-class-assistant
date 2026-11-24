"""核心路由（7）：health / state / db / schedule / monitor / shutdown / public_ip。"""
from __future__ import annotations

from datetime import datetime
from typing import Any

from fastapi import FastAPI

from .. import get_logger
from ..config import config
from ..models import lesson_type_of
from . import AppContext

log = get_logger("api.core")


def _db_values(raw: list[dict[str, Any]]) -> list[float]:
    values: list[float] = []
    for sample in raw:
        value = sample.get("db", 0) if isinstance(sample, dict) else sample
        try:
            values.append(float(value))
        except (TypeError, ValueError):
            continue
    return values


def _db_per_minute(raw: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """按分钟聚合：返回契约要求的 [{min, avg}] 数字数组。"""
    buckets: dict[int, list[float]] = {}
    base: int | None = None
    for index, sample in enumerate(raw):
        value = sample.get("db", 0) if isinstance(sample, dict) else sample
        try:
            value = float(value)
        except (TypeError, ValueError):
            continue
        second = sample.get("sec", index) if isinstance(sample, dict) else index
        try:
            minute = int(second) // 60
        except (TypeError, ValueError):
            minute = index // 60
        if base is None:
            base = minute
        buckets.setdefault(minute - base, []).append(value)
    return [
        {"min": minute + 1, "avg": round(sum(values) / len(values), 1)}
        for minute, values in sorted(buckets.items())
    ]


def register(app: FastAPI, ctx: AppContext) -> None:
    @app.get("/api/health")
    def health() -> dict[str, Any]:
        return {"ok": True}

    @app.get("/api/state")
    def state() -> dict[str, Any]:
        manager = ctx.session_manager
        session = manager.session if manager is not None else None
        tunnel_status: Any = {}
        if ctx.tunnel is not None:
            try:
                tunnel_status = ctx.tunnel.status()
            except Exception:
                tunnel_status = {}
        return {
            "session": session.to_dict() if session is not None else None,
            "model": str(config.get("model", "") or ""),
            "vision_model": str(config.get("vision_model", "") or ""),
            "monitor_enabled": bool(config.get("monitor_enabled", True)),
            "record_camera": bool(config.get("record_camera", True)),
            "record_audio": bool(config.get("record_audio", True)),
            "monitor_always_record": bool(config.get("monitor_always_record", False)),
            "tunnel": tunnel_status,
            "public_ip": manager.public_ip() if manager is not None else "",
        }

    @app.get("/api/db")
    def db_stats() -> dict[str, Any]:
        """分贝统计：samples 必须是数字数组；无会话返回空数据不报错。"""
        try:
            manager = ctx.session_manager
            raw = list(manager.session.db_samples[-120:]) if manager and manager.session else []
            samples = _db_values(raw)
            return {
                "samples": [round(v, 1) for v in samples],
                "avg_db": round(sum(samples) / len(samples), 1) if samples else 0,
                "peak_db": round(max(samples), 1) if samples else 0,
                "per_minute": _db_per_minute(raw),
                "count": len(samples),
            }
        except Exception as exc:
            log.warning(f"分贝统计失败: {exc}")
            return {"samples": [], "avg_db": 0, "peak_db": 0, "per_minute": [], "count": 0}

    @app.get("/api/schedule")
    def schedule() -> dict[str, Any]:
        date = datetime.now().strftime("%Y-%m-%d")
        items: list[dict[str, Any]] = []
        source = "none"
        # 优先桥，其次直读配置文件
        if ctx.bridge is not None:
            try:
                remote = ctx.bridge.get_schedule()
                if remote and remote.get("items"):
                    items = remote["items"]
                    source = "bridge"
            except Exception:
                pass
        if not items and ctx.watcher is not None:
            try:
                result = ctx.watcher.today_schedule()
                items = result.get("items", [])
                source = "classisland" if items else "none"
                date = result.get("date", date)
            except Exception as exc:
                log.debug(f"课表读取跳过: {exc}")
        if not items:
            # 兜底：当前会话单条
            manager = ctx.session_manager
            session = manager.session if manager is not None else None
            if session is not None and session.subject:
                items = [{
                    "subject": session.subject,
                    "start": session.start_time.strftime("%H:%M"),
                    "end": session.end_time.strftime("%H:%M") if session.end_time else "",
                    "type": session.lesson_type,
                    "state": "now",
                }]
                source = "session"
        # 补齐契约字段（type 必存在）
        for item in items:
            if "type" not in item:
                item["type"] = lesson_type_of(str(item.get("subject", "")))
        return {"date": date, "items": items, "source": source}

    @app.post("/api/monitor")
    def set_monitor(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        enabled = bool(payload.get("enabled", True)) if "enabled" in payload else True
        always = payload.get("always_record")
        manager = ctx.session_manager
        if manager is not None:
            return manager.set_monitor(enabled, None if always is None else bool(always))
        config.set("monitor_enabled", enabled)
        if always is not None:
            config.set("monitor_always_record", bool(always))
        return {
            "ok": True,
            "monitor_enabled": enabled,
            "monitor_always_record": bool(config.get("monitor_always_record", False)),
        }

    @app.post("/api/shutdown")
    def shutdown() -> dict[str, Any]:
        """优雅停机：由 cli 注入回调（停感知 → 关 writer → 导出 → 退出）。"""
        if ctx.shutdown is not None:
            try:
                ctx.shutdown()
            except Exception as exc:
                log.warning(f"停机回调异常: {exc}")
        return {"ok": True}

    @app.get("/api/public_ip")
    def public_ip() -> dict[str, Any]:
        manager = ctx.session_manager
        return {"ip": manager.public_ip() if manager is not None else ""}
