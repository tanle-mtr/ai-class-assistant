"""python -m assistant_core.cli 入口。

流程：初始化模块 → 订阅事件 → 常驻感知（按隐私策略决定是否落盘）→
启动状态机 → 起 FastAPI（端口占用顺延）→ /api/shutdown 或 Ctrl+C 优雅停机。

命令行参数：
  --export-only   仅做一次性离线导出未归档数据后退出（不起感知线程、不起 Web），
                  供关机脚本兜底调用。
"""
from __future__ import annotations

import argparse
import threading

from . import get_logger, setup_logger
from .ai.summarizer import Summarizer
from .api import AppContext
from .api.app import start_server
from .classisland.bridge import BridgeClient
from .classisland.watcher import FileWatcher
from .config import config
from .events import bus
from .perception.audio import AudioMonitor
from .perception.camera import CameraMonitor
from .perception.faces import FaceRegistry
from .perception.screen import ScreenAnalyzer
from .services.courseware import courseware
from .services.exporter import Exporter
from .services.seatmap import seatmap
from .services.session import SessionManager
from .services.summary import SummaryService
from .services.textbook import textbook
from .services.tunnel import tunnel

log = get_logger("cli")


def _export_only() -> None:
    """一次性离线导出：归档未归类媒体 + 清理过期媒体，随后返回（供关机脚本兜底）。"""
    exporter = Exporter()
    try:
        moved = exporter.export_unclassified()
        removed = exporter.cleanup()
        log.info(f"[export-only] 归档 {moved} 个未归类文件，清理 {removed} 个过期媒体")
    except Exception as exc:
        log.warning(f"[export-only] 离线导出失败: {exc}")


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(
        prog="python -m assistant_core.cli", description="AI 课堂助手 Python 核心",
    )
    parser.add_argument(
        "--export-only", action="store_true",
        help="仅一次性离线导出未归档数据后退出（不启动感知与 Web 服务）",
    )
    options = parser.parse_args(argv)

    setup_logger(log_dir=config.dir("logs"))
    if options.export_only:
        _export_only()
        return

    config.ensure_model()
    log.info(
        f"AI 课堂助手启动 | 主力模型: {config.get('model') or '(未配置)'} | "
        f"识图模型: {config.get('vision_model') or '(未配置)'} | "
        f"始终录像: {config.get('monitor_always_record', False)}"
    )

    # ---------- 初始化模块 ----------
    face_registry = FaceRegistry()
    audio = AudioMonitor()
    camera = CameraMonitor(face_registry=face_registry)
    screen = ScreenAnalyzer()
    bridge = BridgeClient()
    watcher = FileWatcher()
    summarizer = Summarizer()
    exporter = Exporter()
    summary_service = SummaryService(summarizer=summarizer, exporter=exporter)

    def on_session_end(session):
        return summary_service.run(session, screen=screen)

    manager = SessionManager(
        audio=audio, camera=camera, screen=screen,
        bridge=bridge, watcher=watcher, face_registry=face_registry,
        on_session_end=on_session_end,
    )

    # ---------- 订阅事件（日志观测；业务订阅在 SessionManager 内） ----------
    def log_start(event) -> None:
        log.info(f"事件 session.start: {event.get('subject', '')}")

    def log_end(event) -> None:
        log.info(f"事件 session.end: {event.get('subject', '')}")

    def log_overtime(event) -> None:
        log.warning(f"事件 session.overtime: detected={event.get('detected')}")

    bus.on("session.start", log_start)
    bus.on("session.end", log_end)
    bus.on("session.overtime", log_overtime)

    ctx = AppContext(
        session_manager=manager, audio=audio, camera=camera, screen=screen,
        bridge=bridge, watcher=watcher, face_registry=face_registry,
        summarizer=summarizer, summary_service=summary_service, exporter=exporter,
        seatmap=seatmap, textbook=textbook, courseware=courseware, tunnel=tunnel,
    )

    # ---------- 优雅停机 ----------
    server_holder: list = []
    stopping = threading.Event()

    def cleanup() -> None:
        if stopping.is_set():
            return
        stopping.set()
        log.info("正在优雅停机…")
        manager.stop()          # 结束会话并触发总结/导出
        audio.stop()
        camera.stop()
        screen.stop()
        try:
            moved = exporter.export_unclassified()
            removed = exporter.cleanup()
            log.info(f"停机整理：归档 {moved} 个未归类文件，清理 {removed} 个过期媒体")
        except Exception as exc:
            log.warning(f"停机整理失败: {exc}")
        tunnel.stop()
        server = server_holder[0] if server_holder else None
        if server is not None:
            server.should_exit = True

    ctx.shutdown = cleanup

    # ---------- 常驻感知 ----------
    # 无人上课时仍取流（分贝 / 就座率需要），但 monitor_always_record=false
    # 时 AudioMonitor / CameraMonitor 不写任何 WAV / AVI。
    audio.start()
    camera.start()
    screen.start()
    manager.start()

    # ---------- Web 服务（主线程运行） ----------
    server = start_server(ctx)
    server_holder.append(server)
    try:
        server.run()
    except KeyboardInterrupt:
        log.info("收到 Ctrl+C")
    finally:
        cleanup()
        log.info("AI 课堂助手已停止")


if __name__ == "__main__":
    main()
