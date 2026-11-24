"""导出目录组织：exports/{日期}/{课程}/ 归档 md 与监控切片；14 天自动清理。"""
from __future__ import annotations

import shutil
import time
from datetime import datetime
from pathlib import Path
from typing import Any

from .. import get_logger
from ..config import config

log = get_logger("services.exporter")

# 产物类型 → 文件名（顺序即导出顺序）
EXPORT_ORDER = (
    ("db", "01-课堂报告.md"),
    ("teacher", "01-老师课堂总结.md"),
    ("student", "02-学生课堂总结.md"),
    ("guide", "03-学生导学案.md"),
    ("teacher_guide", "04-老师导学案.md"),
    ("review_questions", "04-讲评变式练习.md"),
    ("mindmap", "05-思维导图.md"),
)
MEDIA_SUFFIXES = (".wav", ".avi", ".mp4")


def safe_name(value: str) -> str:
    return "".join(c for c in (value or "") if c not in '\\/:*?"<>|') or "未知"


class Exporter:
    def export(self, session: Any, outputs: dict[str, str]) -> list[Path]:
        """归档课堂产物；无 md 产物时也归档监控录音/录像。"""
        root = config.dir("exports")
        day_dir = root / session.start_time.strftime("%Y-%m-%d")
        course_dir = day_dir / f"{safe_name(session.subject)}-{session.start_time.strftime('%H%M')}"
        written: list[Path] = []

        if any(outputs.values()):
            try:
                course_dir.mkdir(parents=True, exist_ok=True)
                for kind, filename in EXPORT_ORDER:
                    content = outputs.get(kind)
                    if content:
                        path = course_dir / filename
                        path.write_text(content, encoding="utf-8")
                        written.append(path)
            except Exception as exc:
                log.warning(f"md 产物写入失败: {exc}")

        if session.audio_file is not None and session.audio_file.exists():
            try:
                course_dir.mkdir(parents=True, exist_ok=True)
                target = course_dir / f"课堂录音-{safe_name(session.subject)}.wav"
                shutil.move(str(session.audio_file), str(target))
                session.audio_file = target
                written.append(target)
            except Exception as exc:
                log.warning(f"录音归档失败: {exc}")

        for source in list(session.camera_segments):
            try:
                if not source.exists():
                    continue
                course_dir.mkdir(parents=True, exist_ok=True)
                target = course_dir / source.name
                shutil.move(str(source), str(target))
                session.camera_segments[session.camera_segments.index(source)] = target
                written.append(target)
            except Exception as exc:
                log.warning(f"监控归档失败: {exc}")

        log.info(f"导出完成: {course_dir}（{len(written)} 个文件）")
        return written

    def export_unclassified(self) -> int:
        """停机兜底：把 recordings 中未归档媒体移到 exports/{日期}/未归类/。"""
        recordings = config.dir("recordings")
        exports = config.dir("exports")
        if not recordings.exists():
            return 0
        moved = 0
        for source in recordings.rglob("*"):
            if not source.is_file() or source.suffix.lower() not in MEDIA_SUFFIXES:
                continue
            day = source.parent.name if source.parent.name[:4].isdigit() else datetime.now().strftime("%Y-%m-%d")
            target_dir = exports / day / "未归类"
            try:
                target_dir.mkdir(parents=True, exist_ok=True)
                target = target_dir / source.name
                if not target.exists():
                    shutil.move(str(source), str(target))
                    moved += 1
            except Exception as exc:
                log.warning(f"未归类导出失败: {exc}")
        return moved

    def cleanup(self, retention_days: int | None = None) -> int:
        """清理超过保留期的媒体文件（md 文档永久保留）。"""
        days = int(retention_days or config.get("retention_days", 14) or 14)
        cutoff = time.time() - days * 86400
        removed = 0
        for source in config.dir("exports").rglob("*"):
            if source.is_file() and source.suffix.lower() in MEDIA_SUFFIXES:
                try:
                    if source.stat().st_mtime < cutoff:
                        source.unlink()
                        removed += 1
                except Exception as exc:
                    log.warning(f"清理失败: {exc}")
        if removed:
            log.info(f"清理过期监控媒体 {removed} 个（保留 {days} 天）")
        return removed


exporter = Exporter()
