"""导出：产物分类归档 + 监控录像 14 天清理"""
from __future__ import annotations

import shutil
import time
from datetime import datetime
from pathlib import Path

from .config import config
from .logger import get_logger

log = get_logger("exporter")

EXPORT_ORDER = [
    ("teacher", "01-老师课堂总结.md"),
    ("student", "02-学生课堂总结.md"),
    ("guide", "03-学生导学案.md"),
    ("teacher_guide", "04-老师导学案.md"),
    ("review_questions", "04-讲评变式练习.md"),
    ("mindmap", "05-思维导图.md"),
    ("db", "01-课堂报告.md"),
]


def _safe(s: str) -> str:
    return "".join(c for c in s if c not in '\\/:*?"<>|') or "未知"


class Exporter:
    def export(self, session, outputs: dict[str, str]) -> list[Path]:
        """把总结产物与课堂录音/监控切片归档到 exports/{日期}/{课程}/。
        若本节无 md 产物（无学习内容/电脑未开机），仍归档监控录像（符合"除监控录像"）。"""
        root = config.dir("exports")
        day_dir = root / session.start_time.strftime("%Y-%m-%d")
        course_dir = day_dir / f"{_safe(session.subject)}-{session.start_time.strftime('%H%M')}"

        written: list[Path] = []
        has_md = any(v for v in outputs.values() if v)

        # 1. md 产物（仅当存在时建目录）
        if has_md:
            course_dir.mkdir(parents=True, exist_ok=True)
            for kind, fname in EXPORT_ORDER:
                if kind in outputs and outputs[kind]:
                    p = course_dir / fname
                    p.write_text(outputs[kind], encoding="utf-8")
                    written.append(p)

        # 2. 录音切片（从 recordings 移动进课程目录）
        if session.audio_file and session.audio_file.exists():
            course_dir.mkdir(parents=True, exist_ok=True)
            dst = course_dir / f"课堂录音-{_safe(session.subject)}.wav"
            try:
                shutil.move(str(session.audio_file), str(dst))
                session.audio_file = dst
                written.append(dst)
            except Exception as e:
                log.warning(f"录音归档失败: {e}")

        # 3. 监控切片
        for src in session.camera_segments:
            if src.exists():
                course_dir.mkdir(parents=True, exist_ok=True)
                dst = course_dir / src.name
                try:
                    shutil.move(str(src), str(dst))
                    written.append(dst)
                except Exception as e:
                    log.warning(f"监控归档失败: {e}")

        log.info(f"导出完成: {course_dir}（{len(written)} 个文件）")
        return written

    def export_unclassified(self) -> int:
        """关机兜底：recordings 中未归档文件 → exports/{日期}/未归类/"""
        rec_root = config.dir("recordings")
        exp_root = config.dir("exports")
        n = 0
        if not rec_root.exists():
            return 0
        for f in rec_root.rglob("*"):
            if f.is_file() and f.suffix.lower() in (".wav", ".avi", ".mp4"):
                day = f.parent.name if f.parent.name[:4].isdigit() else datetime.now().strftime("%Y-%m-%d")
                dst_dir = exp_root / day / "未归类"
                dst_dir.mkdir(parents=True, exist_ok=True)
                dst = dst_dir / f.name
                if not dst.exists():
                    try:
                        shutil.move(str(f), str(dst))
                        n += 1
                    except Exception as e:
                        log.warning(f"未归类导出失败 {f.name}: {e}")
        return n

    def cleanup(self, retention_days: int | None = None) -> int:
        """删除超过保留期的监控录像（.wav/.avi/.mp4），保留 md 文档"""
        days = retention_days or config.get("retention_days", 14)
        cutoff = time.time() - days * 86400
        removed = 0
        root = config.dir("exports")
        for f in root.rglob("*"):
            if f.is_file() and f.suffix.lower() in (".wav", ".avi", ".mp4"):
                if f.stat().st_mtime < cutoff:
                    try:
                        f.unlink()
                        removed += 1
                    except Exception as e:
                        log.warning(f"清理失败 {f.name}: {e}")
        if removed:
            log.info(f"清理过期监控录像 {removed} 个（保留 {days} 天）")
        return removed
