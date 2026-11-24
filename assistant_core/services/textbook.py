"""教材服务：版本匹配 / PDF 导入建库 / RAG 问答（严格契约字段）。"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from .. import get_logger
from ..ai.rag import (
    OFFICIAL_FREE_SITE, add_book, guess_region_by_ip, match_textbooks, textbook_index,
)
from ..ai.skills import skill_engine
from ..config import config

log = get_logger("services.textbook")


class TextbookService:
    def status(self) -> dict[str, Any]:
        stats = textbook_index.stats()
        return {
            "ready": bool(stats.get("ready")),
            "chunks": int(stats.get("chunks", 0)),
            "grade": str(config.get("grade", "") or ""),
            "region": str(config.get("region", "") or ""),
            "books": list(stats.get("books", [])),
        }

    def match(self, grade: str = "", region: str = "") -> dict[str, Any]:
        grade = (grade or "").strip() or str(config.get("grade", "") or "")
        region = (region or "").strip() or str(config.get("region", "") or "")
        located = False
        if not region:
            region = guess_region_by_ip()
            located = bool(region)
        versions = match_textbooks(grade, region)
        if grade:
            config.set("grade", grade, persist=False)
        if region:
            config.set("region", region, persist=False)
        return {
            "books": [f"{subject}：{version}" for subject, version in versions.items()],
            "grade": grade,
            "region": region,
            "located": located,
            "official_free": OFFICIAL_FREE_SITE,
            "msg": (
                f"已按{region or '默认'}匹配教材版本；官方免费电子教材可从 {OFFICIAL_FREE_SITE} 获取，"
                f"若无免费版请自行导入文字版 PDF"
            ),
        }

    def import_file(self, filename: str, content: bytes, title: str = "") -> dict[str, Any]:
        """保存上传文件 → 解析 → 建库；失败返回 ok=False + msg，不抛异常。"""
        try:
            root = config.dir("textbooks")
            safe = "".join(c for c in (filename or "") if c not in '\\/:*?"<>|') or "教材.pdf"
            target = root / safe
            target.write_bytes(content)
            name = (title or "").strip() or Path(safe).stem
            chunks = add_book(name, str(target))
            if chunks <= 0:
                return {
                    "ok": False, "chunks": 0,
                    "msg": "未生成知识库片段：请确认已执行 ollama pull bge-m3，或使用文字版 PDF",
                }
            return {"ok": True, "chunks": chunks, "msg": f"《{name}》已入库，共 {chunks} 个片段"}
        except Exception as exc:
            log.warning(f"教材导入失败: {exc}")
            return {"ok": False, "chunks": 0, "msg": str(exc)}

    def ask(self, question: str, top_k: int = 5) -> dict[str, Any]:
        question = (question or "").strip()
        if not question:
            return {"answer": "请输入要提问的内容", "sources": []}
        try:
            hits = textbook_index.retrieve(question, max(1, min(int(top_k), 20)))
        except Exception as exc:
            log.warning(f"教材检索失败: {exc}")
            return {"answer": f"检索失败：{exc}", "sources": []}
        if not hits:
            empty = textbook_index.stats().get("chunks", 0) == 0
            return {
                "answer": "知识库为空，请先导入教材" if empty else "未检索到相关内容，请换个说法或导入更多教材",
                "sources": [],
            }
        context = "\n\n".join(f"【{hit['book']}·{hit['chapter']}】{hit['text']}" for hit in hits)
        try:
            answer = skill_engine.run("textbook-qa", question, context=context)
        except Exception as exc:
            log.warning(f"教材问答生成失败: {exc}")
            answer = f"（模型生成失败：{exc}；请确认 Ollama 已启动并配置主力模型）\n\n检索原文：\n{context}"
        return {
            "answer": answer,
            "sources": [
                {"book": hit.get("book", ""), "page": hit.get("page", ""), "score": hit.get("score", 0)}
                for hit in hits
            ],
        }


textbook = TextbookService()
