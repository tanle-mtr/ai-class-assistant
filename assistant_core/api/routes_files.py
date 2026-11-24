"""文件与课件路由（5）：summaries / files / file/content / upload / courseware。

文件读取/下载均做路径穿越防护：解析后的绝对路径必须位于 exports 根目录内。
"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import FileResponse, JSONResponse

from .. import get_logger
from ..config import config
from . import AppContext

log = get_logger("api.files")


def _safe_export_path(root: Path, *parts: str) -> Path | None:
    """拼接并校验路径；越界返回 None。"""
    try:
        target = (root.joinpath(*parts)).resolve()
        if not target.is_relative_to(root.resolve()):
            return None
        return target
    except Exception:
        return None


def register(app: FastAPI, ctx: AppContext) -> None:
    @app.get("/api/summaries")
    def summaries() -> list[dict[str, Any]]:
        root = config.dir("exports")
        result: list[dict[str, Any]] = []
        if not root.exists():
            return result
        try:
            for day in sorted(root.iterdir()):
                if not day.is_dir():
                    continue
                courses: list[dict[str, Any]] = []
                for course in sorted(day.iterdir()):
                    if not course.is_dir():
                        continue
                    files = [
                        {
                            "name": item.name,
                            "size": item.stat().st_size if item.is_file() else 0,
                            "url": f"/api/files/{day.name}/{course.name}/{item.name}",
                        }
                        for item in sorted(course.iterdir()) if item.is_file()
                    ]
                    courses.append({"name": course.name, "files": files})
                result.append({"date": day.name, "courses": courses})
        except Exception as exc:
            log.warning(f"导出目录扫描失败: {exc}")
        return result

    @app.get("/api/files/{date}/{course}/{filename}")
    def file_download(date: str, course: str, filename: str) -> Any:
        target = _safe_export_path(config.dir("exports"), date, course, filename)
        if target is None:
            return JSONResponse({"ok": False, "error": "非法路径"}, status_code=400)
        if not target.is_file():
            return JSONResponse({"ok": False, "error": "文件不存在"}, status_code=404)
        return FileResponse(target, filename=target.name)

    @app.get("/api/file/content")
    def file_content(date: str = "", course: str = "", name: str = "") -> Any:
        target = _safe_export_path(config.dir("exports"), date, course, name)
        if target is None:
            return JSONResponse({"ok": False, "error": "非法路径"}, status_code=400)
        if not target.is_file():
            return JSONResponse({"ok": False, "error": "文件不存在"}, status_code=404)
        try:
            return {"name": target.name, "content": target.read_text(encoding="utf-8", errors="ignore")}
        except Exception as exc:
            return JSONResponse({"ok": False, "error": f"读取失败：{exc}"}, status_code=400)

    @app.post("/api/upload")
    async def upload(subject: str = Form(""), file: UploadFile = File(...)) -> dict[str, Any]:
        try:
            content = await file.read()
        except Exception as exc:
            return {"ok": False, "name": "", "subject": subject, "msg": str(exc)}
        service = ctx.courseware
        if service is not None:
            return service.upload(subject, file.filename or "课件", content)
        # 无服务兜底：仍按科目建目录落盘
        name = (subject or "").strip() or "未分类"
        target_dir = config.dir("courseware") / name
        try:
            target_dir.mkdir(parents=True, exist_ok=True)
            safe = "".join(c for c in (file.filename or "课件") if c not in '\\/:*?"<>|')
            (target_dir / safe).write_bytes(content)
            return {"ok": True, "name": safe, "subject": name}
        except Exception as exc:
            return {"ok": False, "name": "", "subject": name, "msg": str(exc)}

    @app.get("/api/courseware")
    def courseware() -> dict[str, Any]:
        if ctx.courseware is not None:
            return ctx.courseware.listing()
        return {"subjects": [], "files": []}
