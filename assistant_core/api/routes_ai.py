"""AI 路由（5）：models / models/select / models/create / summarize / export。"""
from __future__ import annotations

import threading
from typing import Any

from fastapi import FastAPI

from .. import get_logger
from ..ai.ollama import ollama
from ..ai.skills import skill_engine
from ..config import config, is_vision_model, query_ollama_models
from . import AppContext

log = get_logger("api.ai")


def register(app: FastAPI, ctx: AppContext) -> None:
    @app.get("/api/models")
    def models() -> dict[str, Any]:
        installed = query_ollama_models()
        normalized = [
            {"name": str(m.get("name", "")), "size": str(m.get("size", ""))}
            for m in installed
        ]
        return {
            "models": normalized,
            "current": str(config.get("model", "") or ""),
            "vision_models": [
                str(m.get("name", "")) for m in installed
                if is_vision_model(str(m.get("name", "")))
            ],
            "hardware": config.hardware.to_dict(),
        }

    @app.post("/api/models/select")
    def select_model(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        payload = payload or {}
        name = str(payload.get("model", "") or "").strip()
        if name:
            config.set("model", name)
        return {"ok": True, "model": str(config.get("model", "") or "")}

    @app.post("/api/models/create")
    def create_model(payload: dict[str, Any] | None = None) -> dict[str, Any]:
        """需求 §2-c：内置技能 + 提示词经 Modelfile 打包专属模型。"""
        payload = payload or {}
        name = str(payload.get("name", "") or "").strip()
        base = str(payload.get("base_model", "") or "").strip() or str(config.get("model", "") or "")
        if not name:
            return {"ok": False, "name": "", "base_model": base, "msg": "请填写要创建的模型名称"}
        if not base:
            return {
                "ok": False, "name": name, "base_model": base,
                "msg": "未指定基础模型，请先配置主力模型或传入 base_model",
            }
        try:
            system = skill_engine.build_system_prompt()
            modelfile = (
                f"FROM {base}\nPARAMETER temperature 0.3\nSYSTEM \"\"\"{system}\"\"\"\n"
            )
            if ollama.create_model(name, modelfile):
                return {
                    "ok": True, "name": name, "base_model": base,
                    "msg": f"专属模型 {name} 创建成功（基于 {base}）",
                }
            return {
                "ok": False, "name": name, "base_model": base,
                "msg": f"创建失败：Ollama 未响应或基础模型 {base} 不存在，请先执行 ollama pull {base}",
            }
        except Exception as exc:
            log.warning(f"创建专属模型失败: {exc}")
            return {"ok": False, "name": name, "base_model": base, "msg": f"创建失败：{exc}"}

    def _trigger(kind: str) -> dict[str, Any]:
        manager = ctx.session_manager
        session = manager.session if manager is not None else None
        if session is None:
            return {"ok": False, "error": "当前无课堂会话"}
        service = ctx.summary_service

        def worker() -> None:
            try:
                if service is not None:
                    service.run(session, screen=ctx.screen)
                else:
                    outputs = ctx.summarizer.generate(session, screen=ctx.screen)
                    ctx.exporter.export(session, outputs)
            except Exception as exc:
                log.warning(f"{kind} 任务失败: {exc}")

        threading.Thread(target=worker, daemon=True, name=f"api-{kind}").start()
        return {"ok": True}

    @app.post("/api/summarize")
    def summarize_now() -> dict[str, Any]:
        return _trigger("summarize")

    @app.post("/api/export")
    def export_now() -> dict[str, Any]:
        return _trigger("export")
