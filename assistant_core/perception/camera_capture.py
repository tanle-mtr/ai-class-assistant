"""摄像头采集（共享模式，不独占）
- OpenCV DSHOW 后端打开，默认共享；被其他程序独占时等待重试，不抢占
- 监控默认开启：按课程切片录制（AVI/MJPG，无 ffmpeg 也能写）
- 上课时周期性做人脸观察（建档/座位）与就座率分析
"""
from __future__ import annotations

import threading
import time
from datetime import datetime

import cv2

from ..config import config
from ..logger import get_logger

log = get_logger("perception.camera")


class CameraMonitor:
    def __init__(self, face_registry=None, camera_index: int = 0):
        self._cap: cv2.VideoCapture | None = None
        self._running = False
        self._thread: threading.Thread | None = None
        self._writer = None
        self._writer_path = None
        self._session = None
        self._face_registry = face_registry
        self._camera_index = camera_index
        self._analyze_interval = 5      # 分析帧间隔（秒）
        self._last_analyze = 0.0
        self._last_face_observe = 0.0   # 人脸建档间隔（秒）
        self._seated_ratio = 0.0
        self._face_count = 0
        self._segment_start: float = 0.0
        self._observations: list[dict] = []   # [{face_id, name, seat, ts}]

    # ---------- 生命周期 ----------
    def start(self):
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="camera-monitor")
        self._thread.start()

    def stop(self):
        self._running = False
        self._close_writer()

    def _ensure_capture(self) -> bool:
        if self._cap and self._cap.isOpened():
            return True
        try:
            # DSHOW：Windows 共享模式；失败说明被其他程序占用
            self._cap = cv2.VideoCapture(self._camera_index, cv2.CAP_DSHOW)
            if not self._cap.isOpened():
                self._cap = None
                return False
            self._cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
            self._cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
            return True
        except Exception as e:
            log.warning(f"摄像头打开失败（可能被占用）: {e}")
            self._cap = None
            return False

    def _loop(self):
        fail = 0
        while self._running:
            if not self._ensure_capture():
                # 无摄像头/被占用：前几次 5 秒一探，之后拉长到 60 秒，避免空转耗 CPU
                fail += 1
                time.sleep(5 if fail < 6 else 60)
                continue
            fail = 0
            ok, frame = self._cap.read()
            if not ok or frame is None:
                time.sleep(1)
                continue
            self._on_frame(frame)
            time.sleep(0.2)  # 5 FPS 控制占用

    # ---------- 帧处理 ----------
    def _on_frame(self, frame):
        # 监控录制（按课程切片；无课程时按 30 分钟自动切片）
        self._ensure_auto_segment()
        if self._writer is not None:
            try:
                self._writer.write(frame)
            except Exception as e:
                log.warning(f"录像写入失败: {e}")

        # 周期性分析：人脸/座位/就座率
        now = time.time()
        if now - self._last_analyze >= self._analyze_interval:
            self._last_analyze = now
            self._analyze(frame)

    def _ensure_auto_segment(self):
        """监控默认开启：无课程会话时也按 30 分钟切片录制"""
        if not config.get("monitor_enabled", True) or not config.get("record_camera", True):
            return
        if self._writer is not None:
            if self._segment_start and time.time() - self._segment_start >= 1800:
                self._close_writer()
                self._start_segment("监控")
            return
        if self._session is None:
            self._start_segment("监控")

    def _start_segment(self, tag: str):
        rec_dir = config.dir("recordings") / datetime.now().strftime("%Y-%m-%d")
        rec_dir.mkdir(parents=True, exist_ok=True)
        path = rec_dir / f"{datetime.now().strftime('%H%M%S')}-{tag}-监控.avi"
        fourcc = cv2.VideoWriter_fourcc(*"MJPG")
        writer = cv2.VideoWriter(str(path), fourcc, 10.0, (1280, 720))
        if writer.isOpened():
            self._writer = writer
            self._writer_path = path
            self._segment_start = time.time()
            log.debug(f"监控切片开始: {path.name}")
        else:
            writer.release()

    def _analyze(self, frame):
        try:
            if self._face_registry:
                faces = self._face_registry.detect_faces(frame)
                self._face_count = len(faces)
                # 就座率近似：画面按 6x8 网格分区，含人脸的格子占比
                h, w = frame.shape[:2]
                grid_rows, grid_cols = 6, 8
                occupied = set()
                seat_label = ""
                biggest = 0
                for (x, y, fw, fh) in faces:
                    r = min(grid_rows - 1, int((y + fh / 2) / h * grid_rows))
                    c = min(grid_cols - 1, int((x + fw / 2) / w * grid_cols))
                    occupied.add((r, c))
                    # 座位网格标签：以面积最大的人脸为准（1-based r{行}c{列}）
                    if fw * fh >= biggest:
                        biggest = fw * fh
                        seat_label = f"r{r + 1}c{c + 1}"
                self._seated_ratio = round(len(occupied) / (grid_rows * grid_cols), 2)
                # 上课时建档观察（每 30 秒一次，避免重复建档）
                now = time.time()
                if self._session and now - self._last_face_observe >= 30:
                    self._last_face_observe = now
                    for obs in self._face_registry.observe(frame, seat_label=seat_label, auto_name=""):
                        self._observations.append({
                            "face_id": obs.get("face_id", ""),
                            "name": obs.get("name", ""),
                            "seat": obs.get("seat", seat_label),
                            "ts": now,
                        })
        except Exception as e:
            log.debug(f"画面分析失败: {e}")

    def collect_observations(self) -> list[dict]:
        """取出并清空已缓存的人脸观察记录（供上层更新座位表）"""
        obs, self._observations = self._observations, []
        return obs

    def majority_seated(self) -> bool:
        """多数学生是否仍在座位（拖堂判定用）"""
        return self._seated_ratio >= config.get("overtime_seated_ratio", 0.7)

    def face_count(self) -> int:
        return self._face_count

    # ---------- 录像控制 ----------
    def start_recording(self, session):
        """上课：开始按课程切片录像（仅 record_camera 且非考试/自习课）"""
        self._session = session
        self._close_writer()
        if not config.get("record_camera", True):
            return
        rec_dir = config.dir("recordings") / session.start_time.strftime("%Y-%m-%d")
        rec_dir.mkdir(parents=True, exist_ok=True)
        safe_subject = "".join(c for c in session.subject if c not in '\\/:*?"<>|') or "未知"
        path = rec_dir / f"{session.start_time.strftime('%H%M%S')}-{safe_subject}-监控.avi"
        fourcc = cv2.VideoWriter_fourcc(*"MJPG")
        writer = cv2.VideoWriter(str(path), fourcc, 10.0, (1280, 720))
        if writer.isOpened():
            self._writer = writer
            self._writer_path = path
            self._segment_start = time.time()
            session.camera_segments.append(path)
            log.info(f"监控录像开始: {path.name}")
        else:
            writer.release()

    def stop_recording(self, session):
        self._session = None
        self._close_writer()

    def _close_writer(self):
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
