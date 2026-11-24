"""摄像头采集（共享模式，不独占设备）。

- OpenCV DSHOW 后端取流；被其他程序独占时等待重试，不抢占
- 6x8 网格估算就座率；周期性人脸观察（建档 / 换座）
- 上课按课程切片写 AVI(MJPG)；无人上课默认不落盘
"""
from __future__ import annotations

import threading
import time
from datetime import datetime
from typing import Any

from .. import get_logger
from ..config import config

log = get_logger("perception.camera")

try:
    import cv2
except Exception as exc:  # pragma: no cover
    cv2 = None  # type: ignore[assignment]
    log.warning(f"opencv 不可用，摄像头功能降级: {exc}")

GRID_ROWS, GRID_COLS = 6, 8


class CameraMonitor:
    """常驻摄像头监控：取流分析（分贝/就座率需要），默认不写磁盘。"""

    def __init__(self, face_registry: Any = None, camera_index: int = 0) -> None:
        self._cap: Any = None
        self._writer: Any = None
        self._writer_path: Path | None = None
        self._session: Any = None
        self._face_registry = face_registry
        self._camera_index = camera_index
        self._running = False
        self._thread: threading.Thread | None = None
        self._analyze_interval = 5.0     # 就座率分析间隔
        self._face_interval = 30.0       # 人脸建档间隔
        self._last_analyze = 0.0
        self._last_face_observe = 0.0
        self._segment_start = 0.0
        self._seated_ratio = 0.0
        self._face_count = 0
        self._observations: list[dict[str, Any]] = []
        self._lock = threading.RLock()

    # ---------- 生命周期 ----------
    def start(self) -> None:
        if self._running or cv2 is None:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="camera-monitor")
        self._thread.start()
        log.info("摄像头监控已启动（共享模式）")

    def stop(self) -> None:
        self._running = False
        if self._cap is not None:
            try:
                self._cap.release()
            except Exception:
                pass
            self._cap = None
        self._close_writer()

    def _ensure_capture(self) -> bool:
        if self._cap is not None and self._cap.isOpened():
            return True
        try:
            self._cap = cv2.VideoCapture(self._camera_index, cv2.CAP_DSHOW)
            if not self._cap.isOpened():
                self._cap = None
                return False
            self._cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
            self._cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
            return True
        except Exception as exc:
            log.debug(f"摄像头打开失败（可能被占用）: {exc}")
            self._cap = None
            return False

    def _loop(self) -> None:
        failures = 0
        while self._running:
            if not self._ensure_capture():
                failures += 1
                time.sleep(5 if failures < 6 else 60)  # 无摄像头时拉长探测间隔
                continue
            failures = 0
            try:
                ok, frame = self._cap.read()
            except Exception:
                ok, frame = False, None
            if not ok or frame is None:
                time.sleep(1)
                continue
            self._on_frame(frame)
            time.sleep(0.2)  # 约 5FPS，控制占用

    def _on_frame(self, frame: Any) -> None:
        self._ensure_auto_segment()
        if self._writer is not None:
            try:
                self._writer.write(frame)
            except Exception as exc:
                log.warning(f"录像写入失败: {exc}")
        now = time.time()
        if now - self._last_analyze >= self._analyze_interval:
            self._last_analyze = now
            self._analyze(frame)

    # ---------- 无人上课：默认不落盘 ----------
    def _ensure_auto_segment(self) -> None:
        if not config.get("monitor_enabled", True) or not config.get("monitor_always_record", False):
            return
        if self._session is not None:
            return
        if self._writer is not None:
            if self._segment_start and time.time() - self._segment_start >= 1800:
                self._close_writer()
            return
        self._start_segment("监控")

    def _start_segment(self, tag: str, session: Any = None) -> None:
        if cv2 is None:
            return
        try:
            rec_dir = config.dir("recordings") / datetime.now().strftime("%Y-%m-%d")
            rec_dir.mkdir(parents=True, exist_ok=True)
            if session is not None:
                safe = "".join(c for c in session.subject if c not in '\\/:*?"<>|') or "未知"
                name = f"{session.start_time.strftime('%H%M%S')}-{safe}-{tag}.avi"
            else:
                name = f"{datetime.now().strftime('%H%M%S')}-{tag}-监控.avi"
            path = rec_dir / name
            height, width = 720, 1280
            # 用当前帧真实尺寸建 writer，避免尺寸不匹配导致写入失败
            if self._cap is not None:
                width = int(self._cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 1280
                height = int(self._cap.get(cv2.CAP_PROP_FRAME_HEIGHT)) or 720
            writer = cv2.VideoWriter(str(path), cv2.VideoWriter_fourcc(*"MJPG"), 10.0, (width, height))
            if not writer.isOpened():
                writer.release()
                return
            self._writer = writer
            self._writer_path = path
            self._segment_start = time.time()
            if session is not None:
                session.camera_segments.append(path)
            log.info(f"监控录像开始: {path.name}")
        except Exception as exc:
            log.warning(f"录像初始化失败: {exc}")

    def _close_writer(self) -> None:
        if self._writer is not None:
            try:
                self._writer.release()
            except Exception:
                pass
            self._writer = None
            if self._writer_path:
                log.info(f"监控录像结束: {self._writer_path.name}")
            self._writer_path = None
            self._segment_start = 0.0

    # ---------- 画面分析 ----------
    def _analyze(self, frame: Any) -> None:
        """人脸检测 + 6x8 网格就座率 + 上课时周期性建档。

        v1 的 NameError 根因：now 只在 _on_frame 定义，这里自行取时间。
        """
        if self._face_registry is None or frame is None:
            return
        try:
            faces = self._face_registry.detect_faces(frame)
            self._face_count = len(faces)
            height, width = frame.shape[:2]
            occupied: set[tuple[int, int]] = set()
            seat_label = ""
            biggest = 0
            for x, y, fw, fh in faces:
                row = min(GRID_ROWS - 1, max(0, int((y + fh / 2) / height * GRID_ROWS)))
                col = min(GRID_COLS - 1, max(0, int((x + fw / 2) / width * GRID_COLS)))
                occupied.add((row, col))
                if fw * fh >= biggest:
                    biggest = fw * fh
                    seat_label = f"r{row + 1}c{col + 1}"  # 1-based 标签 r行c列
            self._seated_ratio = round(len(occupied) / (GRID_ROWS * GRID_COLS), 2)

            now = time.time()  # 关键：_analyze 自带 now，不依赖调用域
            if self._session is not None and now - self._last_face_observe >= self._face_interval:
                self._last_face_observe = now
                for obs in self._face_registry.observe(frame, seat_label=seat_label, auto_name=""):
                    with self._lock:
                        self._observations.append({
                            "face_id": obs.get("face_id", ""),
                            "name": obs.get("name", ""),
                            "seat": obs.get("seat", seat_label),
                            "ts": now,
                        })
        except Exception as exc:
            log.debug(f"画面分析失败（忽略）: {exc}")

    def collect_observations(self) -> list[dict[str, Any]]:
        """取出并清空人脸观察记录（供座位表换座检测）。"""
        with self._lock:
            observations, self._observations = self._observations, []
            return observations

    def majority_seated(self) -> bool:
        """多数学生是否仍在座位（拖堂判定用）。"""
        return self._seated_ratio >= float(config.get("overtime_seated_ratio", 0.7) or 0.7)

    def seated_ratio(self) -> float:
        return self._seated_ratio

    def face_count(self) -> int:
        return self._face_count

    # ---------- 上课录像控制 ----------
    def start_recording(self, session: Any, *, record: bool | None = None) -> None:
        """上课：按课程切片录像（考试/自习课默认不录）。"""
        self._session = session
        self._close_writer()
        if record is None:
            record = bool(config.get("record_camera", True)) and not bool(getattr(session, "is_exam_or_study", False))
        if record:
            self._start_segment("课程", session=session)

    def stop_recording(self, session: Any = None) -> None:
        self._session = None
        self._close_writer()

    def close_auto_writers(self) -> None:
        """关闭无人上课期间的自动切片（不影响上课录像）。"""
        if self._session is None:
            self._close_writer()
