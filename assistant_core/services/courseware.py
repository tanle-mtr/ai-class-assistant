"""课件上传：按科目自动建文件夹分类，无口令。"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from .. import get_logger
from ..config import config

log = get_logger("services.courseware")


def safe_filename(name: str) -> str:
    return "".join(c for c in (name or "") if c not in '\\/:*?"<>|') or "课件"


class CoursewareService:
    def subjects(self) -> list[str]:
        return [str(s) for s in (config.get("courseware_subjects", []) or [])]

    def add_subject(self, subject: str) -> str:
        name = (subject or "").strip() or "未分类"
        current = self.subjects()
        if name not in current:
            current.append(name)
            config.set("courseware_subjects", current)
        return name

    def upload(self, subject: str, filename: str, content: bytes) -> dict[str, Any]:
        name = self.add_subject(subject)
        safe = safe_filename(filename)
        root = config.dir("courseware")
        target_dir = root / name
        try:
            target_dir.mkdir(parents=True, exist_ok=True)
            (target_dir / safe).write_bytes(content)
        except Exception as exc:
            log.warning(f"课件保存失败: {exc}")
            return {"ok": False, "name": safe, "subject": name, "msg": str(exc)}

        # 同步一份到桌面「课件」文件夹，方便老师本机取用；失败不影响主流程
        try:
            desktop = Path.home() / "Desktop"
            if not desktop.exists():
                desktop = Path.home() / "桌面"
            if desktop.exists():
                copy_dir = desktop / "课件" / name
                copy_dir.mkdir(parents=True, exist_ok=True)
                (copy_dir / safe).write_bytes(content)
        except Exception as exc:
            log.debug(f"桌面课件副本跳过: {exc}")

        log.info(f"课件上传: {name}/{safe}（{len(content)} 字节）")
        return {"ok": True, "name": safe, "subject": name}

    def listing(self) -> dict[str, Any]:
        root = config.dir("courseware")
        subjects = self.subjects()
        files: list[dict[str, Any]] = []
        for subject in subjects:
            directory = root / subject
            if not directory.exists():
                continue
            for item in sorted(directory.iterdir()):
                if item.is_file():
                    try:
                        size = item.stat().st_size
                    except Exception:
                        size = 0
                    files.append({"name": item.name, "subject": subject, "size": size})
        # 也把磁盘上存在但配置未记录的科目纳入展示
        for directory in sorted(root.iterdir()):
            if directory.is_dir() and directory.name not in subjects:
                subjects.append(directory.name)
                for item in sorted(directory.iterdir()):
                    if item.is_file():
                        files.append({
                            "name": item.name, "subject": directory.name,
                            "size": item.stat().st_size if item.is_file() else 0,
                        })
        return {"subjects": subjects, "files": files}


courseware = CoursewareService()
