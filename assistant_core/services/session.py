"""课程会话状态机：上课 → 下课 → 拖堂判定 → 收尾。

拖堂判定的关键（v1 曾失效）：以 ClassIsland 的 Breaking 事件为起点进入观察窗口，
窗口内**每个 tick 持续采样**「屏幕仍活跃」+「绝大多数学生未离座」，
窗口结束才真正结束会话；麦克风不参与拖堂判定。
"""
from __future__ import annotations

import threading
import time
from datetime import datetime
from typing import Any, Callable

from .. import get_logger
from ..config import config
from ..events import bus
from ..models import LessonState, Session, classify_subject

log = get_logger("services.session")


class SessionManager:
    def __init__(self, *, audio: Any = None, camera: Any = None, screen: Any = None,
                 bridge: Any = None, watcher: Any = None, face_registry: Any = None,
                 on_session_end: Callable[[Session], None] | None = None) -> None:
        self.session: Session | None = None
        self.audio = audio
        self.camera = camera
        self.screen = screen
        self.bridge = bridge
        self.watcher = watcher
        self.face_registry = face_registry
        self.on_session_end = on_session_end

        self._running = False
        self._thread: threading.Thread | None = None
        self._lock = threading.RLock()
        self._last_state = ""
        self._overtime_until = 0.0

        # 公网 IP 缓存（不阻塞状态接口）
        self._ip_cache = ""
        self._ip_at = 0.0
        self._ip_lock = threading.Lock()

        # 订阅感知事件（契约第 5 节）：把分贝/ASR 样本挂到当前会话
        bus.on("perception.db_sample", self._on_db_sample)
        bus.on("perception.asr_segment", self._on_asr_segment)

    def _on_db_sample(self, event: Any) -> None:
        if self.session is not None:
            self.session.db_samples.append({
                "sec": event.get("sec", 0), "db": event.get("db", 0),
            })

    def _on_asr_segment(self, event: Any) -> None:
        if self.session is not None:
            self.session.asr_segments.append({
                "ts": event.get("ts", 0), "text": event.get("text", ""),
            })

    # ---------- 生命周期 ----------
    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="session-manager")
        self._thread.start()
        log.info("会话状态机已启动")

    def stop(self) -> None:
        self._running = False
        self._end_session(final=True)

    def _loop(self) -> None:
        while self._running:
            try:
                self._tick()
            except Exception as exc:
                log.exception(f"状态机 tick 异常: {exc}")
            time.sleep(1.0)

    # ---------- 课表源 ----------
    def _get_lesson_state(self) -> LessonState | None:
        if self.bridge is not None:
            state = self.bridge.get_state()
            if state is not None:
                return state
        if self.watcher is not None:
            return self.watcher.get_state()
        return None

    def _tick(self) -> None:
        state_info = self._get_lesson_state()
        if state_info is None:
            return  # 无 ClassIsland：感知常驻，分贝/就座率照常分析，不建会话

        state = state_info.state
        subject = state_info.subject or ""

        if state != self._last_state:
            self._last_state = state
            bus.emit("classisland.state", state=state, lesson=state_info.lesson or {
                "subject": subject, "class_name": state_info.class_name,
            })

        if state == "OnClass":
            if not subject:
                return
            with self._lock:
                if self.session is None:
                    self._start_session(subject, state_info.class_name)
                else:
                    self._drain_seat_observations()  # 上课中持续检测换座
            return

        if state in ("Breaking", "None", "AfterSchool"):
            with self._lock:
                if self.session is None:
                    return
                if state == "Breaking" and self.session.end_time is None:
                    self._begin_overtime_check()
                if time.time() < self._overtime_until:
                    self._check_overtime_window()  # 窗口内持续采样，v1 缺陷修复点
                else:
                    self._end_session()

    # ---------- 会话控制 ----------
    def _start_session(self, subject: str, class_name: str) -> None:
        is_exam, is_review = classify_subject(subject)
        self.session = Session(
            subject=subject, class_name=class_name or str(config.get("class_name", "") or ""),
            start_time=datetime.now(), is_exam_or_study=is_exam, is_review=is_review,
        )
        log.info(f"[上课] {subject}（{class_name}）考试/自习={is_exam} 评讲={is_review}")
        if self.audio is not None:
            self.audio.start_recording(self.session)
        if self.camera is not None:
            self.camera.start_recording(
                self.session,
                record=bool(config.get("record_camera", True)) and not is_exam,
            )
        bus.emit("session.start", self.session)

    def _begin_overtime_check(self) -> None:
        """ClassIsland 下课事件：进入拖堂观察窗口。"""
        self._overtime_until = time.time() + float(config.get("overtime_check_seconds", 120) or 120)
        if self.session is not None:
            self.session.end_time = datetime.now()
        log.info("[下课] 进入拖堂观察窗口（持续采样屏幕活跃 + 就座率）")

    def _check_overtime_window(self) -> None:
        """观察窗口内每 tick 采样；两条件同时成立即判拖堂。"""
        if self.session is None or self.session.end_time is None:
            return
        try:
            screen_active = self.screen.force_sample() if self.screen is not None else False
        except Exception:
            screen_active = False
        try:
            students_seated = self.camera.majority_seated() if self.camera is not None else False
        except Exception:
            students_seated = False
        if screen_active and students_seated and not self.session.overtime_detected:
            self.session.overtime_detected = True
            self.session.overtime_at = datetime.now()
            log.warning(f"[拖堂] {self.session.subject} 下课仍在继续")
            bus.emit("session.overtime", session=self.session, detected=True)

    def _drain_seat_observations(self) -> None:
        """把摄像头人脸观察喂给座位表（换座检测接线）。"""
        if self.camera is None:
            return
        try:
            from .seatmap import seatmap
            observations = self.camera.collect_observations()
            if not observations:
                return
            changes = seatmap.update_from_observations(observations)
            if changes:
                log.info(f"[座位表] 检测到换座: {changes}")
                bus.emit("seatmap.changed", changes=changes)
        except Exception as exc:
            log.debug(f"座位观察处理跳过: {exc}")

    def _end_session(self, final: bool = False) -> None:
        with self._lock:
            session = self.session
            if session is None:
                return
            session.end_time = session.end_time or datetime.now()
            self.session = None
            self._overtime_until = 0.0
        log.info(
            f"[下课] {session.subject} 时长约 {session.elapsed() // 60} 分钟，"
            f"拖堂={session.overtime_detected}"
        )
        if self.audio is not None:
            self.audio.stop_recording(session)
        if self.camera is not None:
            self.camera.stop_recording(session)
        self._drain_seat_observations()  # 下课兜底：残留观察并入座位表
        bus.emit("session.end", session)
        if self.on_session_end is not None:
            try:
                self.on_session_end(session)
            except Exception as exc:
                log.warning(f"下课产物管线异常: {exc}")

    # ---------- 监控开关（/api/monitor） ----------
    def set_monitor(self, enabled: bool, always_record: bool | None = None) -> dict[str, Any]:
        config.set("monitor_enabled", bool(enabled))
        if always_record is not None:
            config.set("monitor_always_record", bool(always_record))
            if not always_record:
                if self.audio is not None:
                    self.audio.close_auto_writers()
                if self.camera is not None:
                    self.camera.close_auto_writers()
        session = self.session
        if session is not None:
            try:
                if enabled and bool(config.get("record_audio", True)):
                    self.audio.start_recording(session) if self.audio else None
                elif not enabled and self.audio is not None:
                    self.audio.stop_recording(session)
                if not enabled and self.camera is not None:
                    self.camera.stop_recording(session)
            except Exception as exc:
                log.warning(f"监控开关即时生效失败（配置已保存）: {exc}")
        return {
            "ok": True,
            "monitor_enabled": bool(enabled),
            "monitor_always_record": bool(config.get("monitor_always_record", False)),
        }

    # ---------- 公网 IP ----------
    def public_ip(self) -> str:
        if not config.get("public_ip_auto", True):
            return ""
        with self._ip_lock:
            if not self._ip_cache:
                threading.Thread(target=self._fetch_ip, daemon=True).start()
            return self._ip_cache

    def _fetch_ip(self) -> None:
        now = time.time()
        with self._ip_lock:
            if self._ip_cache and now - self._ip_at < 600:
                return
        try:
            import requests
            ip = requests.get("https://api.ipify.org", timeout=5).text.strip()
            with self._ip_lock:
                self._ip_cache = ip
                self._ip_at = time.time()
        except Exception:
            pass  # 无网时保持空串
