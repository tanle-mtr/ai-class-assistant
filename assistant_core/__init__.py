"""AI 课堂助手 — Python 核心（v2 全量重写）。

分层：config/events/models（基础）→ perception（感知）→ ai（智能）→
classisland（课表源）→ services（业务）→ api（HTTP）→ cli（入口）。

本模块仅提供全局日志工具，供各子模块复用，避免引入契约之外的顶部文件。
"""
from __future__ import annotations

import logging
import sys
from pathlib import Path

__version__ = "2.0.0"
__app_name__ = "AI课堂助手"

_FMT = "%(asctime)s [%(levelname)s] %(name)s: %(message)s"


def setup_logger(name: str = "ai_assistant", log_dir: Path | None = None) -> logging.Logger:
    """初始化全局日志器（控制台 + 滚动文件），重复调用安全。"""
    logger = logging.getLogger(name)
    if logger.handlers:
        return logger
    logger.setLevel(logging.INFO)
    fmt = logging.Formatter(_FMT, datefmt="%Y-%m-%d %H:%M:%S")

    # 无窗口启动（托盘/打包 exe）时 stdout 可能为 None，需兜底，否则 uvicorn 日志会崩
    stream = sys.stdout
    if stream is None:
        import os
        stream = open(os.devnull, "w", encoding="utf-8", errors="replace")
    console = logging.StreamHandler(stream)
    console.setFormatter(fmt)
    logger.addHandler(console)

    if log_dir is not None:
        try:
            from logging.handlers import RotatingFileHandler
            log_dir = Path(log_dir)
            log_dir.mkdir(parents=True, exist_ok=True)
            fh = RotatingFileHandler(
                log_dir / "assistant.log", maxBytes=5 * 1024 * 1024,
                backupCount=5, encoding="utf-8",
            )
            fh.setFormatter(fmt)
            logger.addHandler(fh)
        except Exception:
            pass  # 日志落盘失败不影响主流程
    return logger


def get_logger(name: str | None = None) -> logging.Logger:
    """获取子模块日志器（形如 ai_assistant.<name>）。"""
    return logging.getLogger(f"ai_assistant.{name}" if name else "ai_assistant")
