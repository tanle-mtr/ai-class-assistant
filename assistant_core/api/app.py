"""FastAPI 应用装配与启动。

create_app(ctx) 注册契约第 4 节全部 29 个端点；webui/dist 存在时静态托管。
start_server() 探测端口（18760 起，占用顺延，跳过桥端口 18761）并把实际端口写回 config。
"""
from __future__ import annotations

import socket
import sys
from pathlib import Path
from typing import Any

import uvicorn
from fastapi import FastAPI
from fastapi.staticfiles import StaticFiles

from .. import get_logger
from ..config import PROJECT_ROOT, config
from . import AppContext
from . import routes_ai, routes_core, routes_extra, routes_files

log = get_logger("api.app")

WEBUI_DIST = PROJECT_ROOT / "webui" / "dist"


def create_app(ctx: AppContext | None = None) -> FastAPI:
    """装配全部路由（先 API 后静态托管，避免 '/' 吞掉接口）。"""
    # 无窗口启动时 stdout 可能为 None，uvicorn formatter 会调用 isatty() 而崩溃
    if sys.stdout is None:
        import os
        sys.stdout = open(os.devnull, "w", encoding="utf-8", errors="replace")
    if sys.stderr is None:
        import os
        sys.stderr = open(os.devnull, "w", encoding="utf-8", errors="replace")

    context = ctx or AppContext()
    app = FastAPI(title="AI 课堂助手", version="2.0.0")
    routes_core.register(app, context)
    routes_ai.register(app, context)
    routes_files.register(app, context)
    routes_extra.register(app, context)

    if WEBUI_DIST.exists():
        app.mount("/", StaticFiles(directory=str(WEBUI_DIST), html=True), name="webui")
        log.info(f"静态托管已启用: {WEBUI_DIST}")
    return app


def port_free(port: int) -> bool:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
        try:
            sock.bind(("127.0.0.1", port))
            return True
        except OSError:
            return False


def start_server(ctx: AppContext, host: str = "127.0.0.1",
                 port: int | None = None) -> uvicorn.Server:
    """选择空闲端口（占用顺延），写回 config，返回可直接 run() 的 server。"""
    chosen = int(port or config.get("web_port", 18760) or 18760)
    bridge_port = int(config.get("classisland_bridge_port", 18761) or 18761)
    for _ in range(64):
        if port_free(chosen) and chosen != bridge_port:
            break
        log.warning(f"端口 {chosen} 被占用或为桥端口，顺延到 {chosen + 1}")
        chosen += 1
    if chosen != int(config.get("web_port", 18760) or 18760):
        config.set("web_port", chosen)  # 契约：写回实际端口
    server = uvicorn.Server(
        uvicorn.Config(create_app(ctx), host=host, port=chosen, log_level="warning")
    )
    log.info(f"Web 控制台: http://{host}:{chosen}")
    return server
