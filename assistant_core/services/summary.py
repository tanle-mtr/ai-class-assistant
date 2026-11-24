"""总结管线：调用课型分流 summarizer → 导出归档 → 发事件。"""
from __future__ import annotations

from pathlib import Path
from typing import Any

from .. import get_logger
from ..ai.summarizer import Summarizer
from ..events import bus
from .exporter import Exporter

log = get_logger("services.summary")


class SummaryService:
    def __init__(self, summarizer: Summarizer | None = None, exporter: Exporter | None = None) -> None:
        self.summarizer = summarizer or Summarizer()
        self.exporter = exporter or Exporter()

    def run(self, session: Any, screen: Any = None) -> dict[str, Any]:
        """生成并归档；任何失败都返回结构完整的空结果，不抛异常。"""
        try:
            outputs = self.summarizer.generate(session, screen=screen)
            session.outputs = dict(outputs)
            files: list[Path] = self.exporter.export(session, outputs)
            bus.emit("summary.generated", session=session.to_dict(), files=[str(p) for p in files])
            return {"ok": True, "session": session.to_dict(), "files": [str(p) for p in files]}
        except Exception as exc:
            log.exception(f"总结管线失败: {exc}")
            bus.emit("summary.generated", session=session.to_dict(), files=[], error=str(exc))
            return {"ok": False, "error": str(exc), "files": []}


summary_service = SummaryService()
