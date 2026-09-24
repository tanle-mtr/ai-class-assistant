"""AI 课堂助手 - 主程序
课程会话状态机：监听 ClassIsland → 上课启动感知/监控 → 下课生成总结 → 分类导出
"""
from __future__ import annotations

import re
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .config import config
from .events import bus
from .logger import get_logger, setup_logger
from .seatmap import seatmap

log = get_logger("main")

# ---- 课型判定 ----
EXAM_KEYWORDS = ["考试", "自习", "测验", "月考", "期中", "期末", "模拟考", "周测"]
REVIEW_KEYWORDS = ["评讲", "讲评", "作业讲评", "试卷", "习题课", "订正", "试卷分析"]


def classify_lesson(subject: str) -> tuple[bool, bool]:
    """返回 (是否考试/自习课, 是否评讲课)"""
    s = subject or ""
    is_exam = any(k in s for k in EXAM_KEYWORDS)
    is_review = any(k in s for k in REVIEW_KEYWORDS)
    return is_exam, is_review


@dataclass
class LessonSession:
    """一节课程会话"""
    subject: str = ""
    class_name: str = ""
    start_time: datetime = field(default_factory=datetime.now)
    end_time: datetime | None = None
    is_exam_or_study: bool = False
    is_review: bool = False
    overtime_detected: bool = False
    overtime_at: datetime | None = None
    db_samples: list[dict] = field(default_factory=list)      # [{sec, db}]
    asr_segments: list[dict] = field(default_factory=list)    # [{start,end,text}]
    screen_shots: list[Path] = field(default_factory=list)
    camera_segments: list[Path] = field(default_factory=list)
    audio_file: Path | None = None
    notes: list[str] = field(default_factory=list)

    def elapsed(self) -> int:
        return int((datetime.now() - self.start_time).total_seconds())

    def to_dict(self) -> dict:
        return {
            "subject": self.subject,
            "class_name": self.class_name,
            "start": self.start_time.isoformat(),
            "end": self.end_time.isoformat() if self.end_time else "",
            "is_exam_or_study": self.is_exam_or_study,
            "is_review": self.is_review,
            "overtime": self.overtime_detected,
            "duration_min": round(self.elapsed() / 60, 1),
        }


class AssistantApp:
    """应用主状态机"""

    def __init__(self):
        self.session: LessonSession | None = None
        self._running = False
        self._lock = threading.Lock()
        self._audio: object | None = None
        self._camera: object | None = None
        self._screen: object | None = None
        self._asr: object | None = None
        self._summarizer: object | None = None
        self._exporter: object | None = None
        self._bridge: object | None = None
        self._file_watcher: object | None = None
        self._last_state = ""
        self._last_subject = ""
        self._overtime_check_until: float = 0
        self._overtime_screen_active = False
        self._overtime_students_seated = False

    # ---------- 生命周期 ----------
    def start(self):
        setup_logger(log_dir=config.dir("logs"))
        log.info("AI 课堂助手启动")
        config.ensure_model()
        log.info(f"主力模型: {config.get('model') or '(未配置)'} | 识图模型: {config.get('vision_model') or '(未配置)'}")

        self._init_modules()
        bus.emit("app.started")
        self._running = True
        self._loop()

    def stop(self):
        log.info("正在停止…")
        self._running = False
        self._end_session(final=True)
        # 关机/退出兜底：未归档监控文件导出 + 清理过期录像
        try:
            if self._exporter:
                n = self._exporter.export_unclassified()
                removed = self._exporter.cleanup()
                log.info(f"停机导出 {n} 个未归档文件，清理 {removed} 个过期录像")
        except Exception as e:
            log.warning(f"停机导出失败: {e}")
        if self._audio:
            self._audio.stop()
        if self._camera:
            self._camera.stop()
        if self._screen:
            self._screen.stop()

    def _init_modules(self):
        from .ai.asr import AsrEngine
        from .ai.summarizer import Summarizer
        from .classisland.bridge_client import BridgeClient
        from .classisland.file_watcher import FileWatcher
        from .exporter import Exporter
        from .perception.audio_capture import AudioMonitor
        from .perception.camera_capture import CameraMonitor
        from .perception.face_registry import FaceRegistry
        from .perception.screen_analyzer import ScreenAnalyzer

        self._bridge = BridgeClient()
        self._file_watcher = FileWatcher()
        self._audio = AudioMonitor()
        self._camera = CameraMonitor(face_registry=FaceRegistry())
        self._screen = ScreenAnalyzer(interval=config.get("screen_analysis_interval", 30))
        self._asr = AsrEngine(model_size=config.get("asr_model", "small"))
        self._summarizer = Summarizer()
        self._exporter = Exporter()

        # 事件订阅
        bus.on("perception.db_sample", self._on_db_sample)
        bus.on("perception.asr_segment", self._on_asr_segment)

        # 启动感知（共享模式，常驻）
        self._audio.start()
        self._camera.start()
        self._screen.start()

        # 启动 Web 控制台
        try:
            from .web.server import start as start_web
            start_web(self, port=None)
        except Exception as e:
            log.exception(f"Web 控制台启动失败: {e}")

        # 启动 OpenList 与 cf 隧道（若已配置）
        try:
            from .tunnel.openlist import openlist
            if openlist.exe():
                openlist.start()
        except Exception as e:
            log.warning(f"OpenList 启动失败: {e}")
        try:
            from .tunnel.cloudflared import cloudflared
            if cloudflared.get_token():
                cloudflared.start()
        except Exception as e:
            log.warning(f"cf 隧道启动失败: {e}")

    # ---------- 感知回调 ----------
    def _on_db_sample(self, ev):
        if self.session:
            self.session.db_samples.append(ev.data)

    def _on_asr_segment(self, ev):
        if self.session:
            self.session.asr_segments.append(ev.data)

    # ---------- 主循环 ----------
    def _loop(self):
        while self._running:
            try:
                self._tick()
            except Exception as e:
                log.exception(f"主循环异常: {e}")
            time.sleep(1.0)

    def _get_lesson_state(self):
        """优先 IPC 桥，其次文件监听"""
        st = self._bridge.get_state() if self._bridge else None
        if st is None:
            st = self._file_watcher.get_state() if self._file_watcher else None
        return st

    def _tick(self):
        st = self._get_lesson_state()
        if st is None:
            # ClassIsland 未运行/未配置：靠监控模式自主工作
            self._tick_no_source()
            return

        state = st.state
        subject = st.subject or ""

        # 状态变化事件
        if state != self._last_state:
            bus.emit("classisland.state_changed", state=state, subject=subject)
            self._last_state = state
        if subject != self._last_subject:
            bus.emit("classisland.subject_changed", subject=subject)
            self._last_subject = subject

        if state == "OnClass" and not subject:
            return

        if state == "OnClass":
            if self.session is None:
                self._start_session(subject, st.class_name)
            else:
                self._update_session()
            self._check_overtime_window()
        elif state in ("Breaking", "None", "AfterSchool"):
            if self.session is not None:
                # 下课：进入拖堂检测窗口（每会话仅一次）
                if state == "Breaking" and not self.session.end_time:
                    self._begin_overtime_check()
                if time.time() < self._overtime_check_until:
                    # 观察窗口内：每 tick 采样判定（屏幕活跃 + 绝大多数学生未离座）
                    # 屏幕采样间隔 30s，窗口内任意一次采样同时满足两条件即可判拖堂
                    self._check_overtime_window()
                else:
                    # 窗口结束才收尾
                    self._end_session()
            self._tick_no_source()

    def _tick_no_source(self):
        """无 ClassIsland 信号时的后台任务：分贝监测（考试/自习模式仍可用）"""
        if config.get("monitor_enabled", True):
            pass  # 录制由 AudioMonitor/CameraMonitor 独立常驻

    # ---------- 会话控制 ----------
    def _start_session(self, subject: str, class_name: str):
        with self._lock:
            is_exam, is_review = classify_lesson(subject)
            self.session = LessonSession(
                subject=subject,
                class_name=class_name,
                is_exam_or_study=is_exam,
                is_review=is_review,
            )
        log.info(f"[上课] {subject} ({class_name}) 考试/自习={is_exam} 评讲={is_review}")
        self._audio.start_recording(self.session)
        if config.get("record_camera", True) and not is_exam:
            self._camera.start_recording(self.session)
        bus.emit("session.started", session=self.session.to_dict())

    def _update_session(self):
        """上课中：把摄像头的人脸观察结果喂给座位表，自动检测换座"""
        # 注：ASR 增量转写由 AudioMonitor 内部完成，这里只处理座位维护
        self._drain_seat_observations()

    def _drain_seat_observations(self):
        """取出摄像头缓存的人脸观察 → 更新座位表（同一人新座位出现 2 次以上判定换座）"""
        try:
            if not self._camera:
                return
            obs = self._camera.collect_observations()
            if not obs:
                return
            changes = seatmap.update_from_observations(obs)
            if changes:
                log.info(f"[座位表] 检测到换座 {len(changes)} 人: {changes}")
                bus.emit("seatmap.changed", changes=changes)
        except Exception as e:
            log.debug(f"座位观察处理失败: {e}")

    def _begin_overtime_check(self):
        """下课事件：进入拖堂观察窗口"""
        self._overtime_check_until = time.time() + config.get("overtime_check_seconds", 120)
        self._overtime_screen_active = False
        self._overtime_students_seated = False
        if self.session:
            self.session.end_time = datetime.now()
        log.info("[下课] 进入拖堂观察窗口")

    def _check_overtime_window(self):
        if not self.session or not self.session.end_time:
            return
        # 拖堂窗口内：屏幕活跃 + 学生未离座
        try:
            self._overtime_screen_active = self._screen.is_active() if self._screen else False
            self._overtime_students_seated = self._camera.majority_seated() if self._camera else False
        except Exception as e:
            log.debug(f"拖堂判定采样失败: {e}")
        if self._overtime_screen_active and self._overtime_students_seated:
            if not self.session.overtime_detected:
                self.session.overtime_detected = True
                self.session.overtime_at = datetime.now()
                log.warning(f"[拖堂] {self.session.subject} 下课仍在继续（屏幕活跃 + 学生未离座）")
                bus.emit("overtime.detected", session=self.session.to_dict())

    def _overtime_check_passed(self) -> bool:
        return time.time() > self._overtime_check_until

    def _end_session(self, final: bool = False):
        with self._lock:
            s = self.session
            if s is None:
                return
            s.end_time = s.end_time or datetime.now()
            self.session = None
        log.info(f"[下课] {s.subject} 时长约 {s.elapsed() // 60} 分钟，拖堂={s.overtime_detected}")
        self._audio.stop_recording(s)
        self._camera.stop_recording(s)
        self._drain_seat_observations()   # 下课兜底：把残留观察并入座位表
        bus.emit("session.ended", session=s.to_dict())
        self._run_summary_pipeline(s)

    # ---------- 总结流水线 ----------
    def _run_summary_pipeline(self, s: LessonSession):
        """下课产物：按课型生成 md 并导出（无学习内容/电脑未开机时仅归档监控）"""
        try:
            outputs = self._summarizer.generate(s, screen=self._screen)
            paths = self._exporter.export(s, outputs)
            bus.emit("summary.generated", session=s.to_dict(), files=[str(p) for p in paths])
        except Exception as e:
            log.exception(f"总结生成失败: {e}")

    # ---------- 公网 IP ----------
    _ip_cache: str = ""
    _ip_cache_at: float = 0.0

    @classmethod
    def public_ip(cls) -> str:
        """自动查询公网 IP（可选），带缓存：立即返回缓存，查询放后台线程，绝不阻塞状态接口"""
        if not config.get("public_ip_auto", True):
            return ""
        if not cls._ip_cache:
            threading.Thread(target=cls._fetch_ip, daemon=True).start()
        return cls._ip_cache

    @classmethod
    def _fetch_ip(cls):
        """后台查询 ipify 并写缓存（失败保持原缓存/空串）"""
        now = time.time()
        if cls._ip_cache and now - cls._ip_cache_at < 600:
            return
        try:
            import requests
            cls._ip_cache = requests.get("https://api.ipify.org", timeout=5).text.strip()
            cls._ip_cache_at = time.time()
        except Exception:
            pass


def main():
    app = AssistantApp()
    try:
        app.start()
    except KeyboardInterrupt:
        app.stop()


if __name__ == "__main__":
    main()
