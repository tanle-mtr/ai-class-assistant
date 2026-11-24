"""教科书 / 座位表 / 隧道路由（12）：textbook×4 + seatmap×3 + tunnel×5。"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import FileResponse, Response

from .. import get_logger
from ..config import config
from . import AppContext

log = get_logger("api.extra")


def register(app: FastAPI, ctx: AppContext) -> None:
    # ---------- 教科书 ----------
    @app.get("/api/textbook/status")
    def textbook_status() -> dict[str, Any]:
        if ctx.textbook is not None:
            return ctx.textbook.status()
        return {"ready": False, "chunks": 0, "grade": "", "region": "", "books": []}

    @app.post("/api/textbook/match")
    def textbook_match(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        if ctx.textbook is not None:
            return ctx.textbook.match(str(payload.get("grade", "") or ""), str(payload.get("region", "") or ""))
        return {"books": [], "grade": "", "region": "", "msg": "教材服务不可用"}

    @app.post("/api/textbook/import")
    async def textbook_import(file: UploadFile = File(...), title: str = Form("")) -> dict[str, Any]:
        try:
            content = await file.read()
            if ctx.textbook is not None:
                return ctx.textbook.import_file(file.filename or "教材.pdf", content, title)
            return {"ok": False, "chunks": 0, "msg": "教材服务不可用"}
        except Exception as exc:
            log.warning(f"教材导入失败: {exc}")
            return {"ok": False, "chunks": 0, "msg": str(exc)}

    @app.post("/api/textbook/ask")
    def textbook_ask(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        question = str(payload.get("question", "") or "")
        if ctx.textbook is not None:
            return ctx.textbook.ask(question, int(payload.get("top_k", 5) or 5))
        return {"answer": "教材服务不可用", "sources": []}

    # ---------- 座位表 ----------
    @app.get("/api/seatmap")
    def get_seatmap() -> dict[str, Any]:
        if ctx.seatmap is not None:
            return ctx.seatmap.to_api_dict(ctx.face_registry)
        return {"class_name": "", "seats": []}

    @app.post("/api/seatmap/init")
    async def init_seatmap(file: UploadFile = File(...), class_name: str = Form("")) -> dict[str, Any]:
        try:
            image_dir = config.dir("seats")
            image_path = image_dir / "upload_seatmap.png"
            image_path.write_bytes(await file.read())
            if ctx.seatmap is not None:
                return ctx.seatmap.init_from_image(str(image_path), class_name)
            return {"ok": False, "seats": 0, "msg": "座位表服务不可用"}
        except Exception as exc:
            log.warning(f"座位表初始化失败: {exc}")
            return {"ok": False, "seats": 0, "msg": str(exc)}

    @app.get("/api/seatmap/export")
    def export_seatmap() -> Any:
        try:
            if ctx.seatmap is None:
                return Response("# 座位表\n\n（尚未初始化）", media_type="text/markdown")
            path: Path = ctx.seatmap.export()
            return FileResponse(path, media_type="text/markdown", filename="座位表.md")
        except Exception as exc:
            log.warning(f"座位表导出失败: {exc}")
            return Response("# 座位表\n\n（导出失败）", media_type="text/markdown")

    # ---------- 隧道 ----------
    @app.post("/api/tunnel/token")
    def set_tunnel_token(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        if ctx.tunnel is not None:
            return ctx.tunnel.set_token(str(payload.get("token", "") or ""))
        config.set("tunnel_token", str(payload.get("token", "") or ""))
        return {"ok": True}

    @app.post("/api/tunnel/start")
    def start_tunnel() -> dict[str, Any]:
        if ctx.tunnel is not None:
            return ctx.tunnel.start()
        return {"ok": False, "msg": "隧道服务不可用"}

    @app.get("/api/tunnel/rules")
    def tunnel_rules() -> list[dict[str, Any]]:
        if ctx.tunnel is not None:
            return ctx.tunnel.rules()
        return []

    @app.post("/api/tunnel/proxy")
    def add_proxy(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        try:
            port = int(payload.get("port", 0) or 0)
        except (TypeError, ValueError):
            port = 0
        if port <= 0:
            rules = ctx.tunnel.rules() if ctx.tunnel is not None else []
            return {"ok": False, "rules": rules, "msg": "端口号无效"}
        rules = ctx.tunnel.add_proxy(port, str(payload.get("name", "") or ""),
                                     bool(payload.get("enabled", True))) if ctx.tunnel is not None else []
        return {"ok": True, "rules": rules}

    @app.post("/api/tunnel/login")
    def tunnel_login() -> dict[str, Any]:
        if ctx.tunnel is not None:
            return ctx.tunnel.login()
        return {"ok": False, "msg": "隧道服务不可用"}
