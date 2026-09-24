"""AI 课堂助手 - 日志模块"""
from __future__ import annotations

import logging
import sys
from logging.handlers import RotatingFileHandler
from pathlib import Path


def setup_logger(name: str = "ai_assistant", log_dir: Path | None = None) -> logging.Logger:
    """初始化全局日志器：控制台 + 滚动文件"""
    logger = logging.getLogger(name)
    if logger.handlers:  # 已初始化
        return logger

    logger.setLevel(logging.INFO)

    fmt = logging.Formatter(
        "%(asctime)s [%(levelname)s] %(name)s: %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    # 控制台
    console = logging.StreamHandler(sys.stdout)
    console.setFormatter(fmt)
    logger.addHandler(console)

    # 文件（滚动，5MB x 5）
    if log_dir is not None:
        log_dir = Path(log_dir)
        log_dir.mkdir(parents=True, exist_ok=True)
        fh = RotatingFileHandler(
            log_dir / "assistant.log",
            maxBytes=5 * 1024 * 1024,
            backupCount=5,
            encoding="utf-8",
        )
        fh.setFormatter(fmt)
        logger.addHandler(fh)

    return logger


def get_logger(name: str | None = None) -> logging.Logger:
    """获取子模块日志器"""
    return logging.getLogger(f"ai_assistant.{name}" if name else "ai_assistant")
