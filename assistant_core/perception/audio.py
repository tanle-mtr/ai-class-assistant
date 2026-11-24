"""麦克风采集（共享模式，不独占设备）。

- 16kHz 单声道采集：实时分贝 + ASR 喂块
- 上课按课程切片写 WAV；无人上课默认仅取流分析、不落盘
- 事件：perception.db_sample {sec, db} / perception.asr_segment {ts, text}
"""
from __future__ import annotations

import queue
import threading
import time
import wave
from datetime import datetime
from pathlib import Path
from typing import Any

import numpy as np

from .. import get_logger
from ..config import config
from ..events import bus

log = get_logger("perception.audio")

SAMPLE_RATE = 16000
CHANNELS = 1
DTYPE = np.int16
ASR_BLOCK_SEC = 30          # ASR 增量窗口
MONITOR_SEGMENT_SEC = 1800  # 监控切片时长（30 分钟）


def rms_db(samples: np.ndarray) -> float:
    """RMS 分贝（0dBFS 满幅 + 90 偏移，使日常语音落在 30~80 区间）。"""
    if samples is None or len(samples) == 0:
        return 0.0
    try:
        rms = float(np.sqrt(np.mean(np.square(samples.astype(np.float32)))))
        if rms < 1e-7:
            return 0.0
        return round(90.0 + 20.0 * np.log10(rms), 1)
    except Exception:
        return 0.0


class AudioMonitor:
    """常驻麦克风监控；无课程时仅做分贝采样（供课堂活跃度 / 拖堂判定分析）。"""

    def __init__(self) -> None:
        self._sd: Any = None
        try:
            import sounddevice as sd
            self._sd = sd
        except Exception as exc:
            log.warning(f"sounddevice 不可用，麦克风功能降级: {exc}")

        self._running = False
        self._thread: threading.Thread | None = None
        self._asr_worker: threading.Thread | None = None
        self._stream: Any = None
        self._wav: wave.Wave_write | None = None
        self._recording = False
        self._session: Any = None
        self._segment_start = 0.0
        self._last_db = 0.0

        self._buf = np.zeros(0, dtype=np.float32)
        self._asr_enabled = False
        self._asr_queue: queue.Queue = queue.Queue()
        self._lock = threading.Lock()

    # ---------- 生命周期 ----------
    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._capture_loop, daemon=True, name="audio-capture")
        self._asr_worker = threading.Thread(target=self._asr_loop, daemon=True, name="asr-worker")
        self._thread.start()
        self._asr_worker.start()
        log.info("麦克风监控已启动（共享模式）")

    def stop(self) -> None:
        self._running = False
        try:
            if self._stream is not None:
                self._stream.stop()
                self._stream.close()
        except Exception:
            pass
        self._stream = None
        self._stop_recording()
        self._asr_queue.put(None)

    # ---------- 录音控制 ----------
    def start_recording(self, session: Any) -> None:
        """上课：先关掉旧 WAV 句柄（避免 v1 的句柄泄漏 / 文件头损坏），再开课程录音。"""
        self._stop_recording()
        self._session = session
        self._buf = np.zeros(0, dtype=np.float32)
        self._asr_enabled = not bool(getattr(session, "is_exam_or_study", False))
        try:
            rec_dir = config.dir("recordings") / session.start_time.strftime("%Y-%m-%d")
            rec_dir.mkdir(parents=True, exist_ok=True)
            safe = "".join(c for c in session.subject if c not in '\\/:*?"<>|') or "未知"
            path = rec_dir / f"{session.start_time.strftime('%H%M%S')}-{safe}-音频.wav"
            self._wav = wave.open(str(path), "wb")
            self._wav.setnchannels(CHANNELS)
            self._wav.setsampwidth(2)
            self._wav.setframerate(SAMPLE_RATE)
            session.audio_file = path
            self._recording = True
            log.info(f"录音开始: {path.name}")
        except Exception as exc:
            log.warning(f"录音初始化失败: {exc}")
            self._recording = False

    def stop_recording(self, session: Any = None) -> None:
        self._session = None
        self._stop_recording()

    def close_auto_writers(self) -> None:
        """关闭「无人上课」期间的自动切片（不影响上课录音）。"""
        if self._session is None:
            self._stop_recording()

    def _stop_recording(self) -> None:
        self._recording = False
        self._asr_enabled = False
        with self._lock:
            if self._wav is not None:
                try:
                    self._wav.close()
                except Exception:
                    pass
                self._wav = None
        self._segment_start = 0.0
        self._flush_asr()

    # ---------- 无人上课：默认不落盘 ----------
    def _ensure_auto_recording(self) -> None:
        """仅当 monitor_always_record=true 才写监控切片；默认只分贝采样。"""
        if not config.get("monitor_enabled", True) or not config.get("monitor_always_record", False):
            return
        if self._session is not None:
            return
        if self._recording and self._wav is not None:
            if self._segment_start and time.time() - self._segment_start >= MONITOR_SEGMENT_SEC:
                self._stop_recording()
            return
        self._start_monitor_segment()

    def _start_monitor_segment(self) -> None:
        try:
            rec_dir = config.dir("recordings") / datetime.now().strftime("%Y-%m-%d")
            rec_dir.mkdir(parents=True, exist_ok=True)
            path = rec_dir / f"{datetime.now().strftime('%H%M%S')}-监控-音频.wav"
            with self._lock:
                self._wav = wave.open(str(path), "wb")
                self._wav.setnchannels(CHANNELS)
                self._wav.setsampwidth(2)
                self._wav.setframerate(SAMPLE_RATE)
            self._recording = True
            self._segment_start = time.time()
        except Exception as exc:
            log.warning(f"监控录音切片失败: {exc}")
            self._recording = False

    # ---------- 采集循环 ----------
    def _capture_loop(self) -> None:
        if self._sd is None:
            return
        block = SAMPLE_RATE * max(1, int(config.get("db_sample_interval", 1) or 1))
        while self._running:
            try:
                with self._sd.InputStream(
                    samplerate=SAMPLE_RATE, channels=CHANNELS, dtype=DTYPE,
                    blocksize=block, device=None,  # 不指定独占设备，WASAPI 共享
                ) as stream:
                    self._stream = stream
                    while self._running:
                        data, _ = stream.read(block)
                        samples = data.reshape(-1).astype(np.float32) / 32768.0
                        self._on_block(samples)
            except Exception as exc:
                log.warning(f"麦克风采集异常（可能被其他程序占用，稍后重试）: {exc}")
                self._stream = None
                time.sleep(3)
            else:
                self._stream = None
                if self._running:
                    time.sleep(1)

    def _on_block(self, samples: np.ndarray) -> None:
        self._ensure_auto_recording()
        db = rms_db(samples * 32768.0)
        self._last_db = db
        bus.emit("perception.db_sample", sec=int(time.time()), db=db)

        if self._recording and self._wav is not None:
            try:
                with self._lock:
                    if self._wav is not None:
                        self._wav.writeframes((samples * 32768.0).astype(np.int16).tobytes())
            except Exception as exc:
                log.warning(f"录音写入失败: {exc}")

        if self._asr_enabled:
            self._buf = np.concatenate([self._buf, samples])
            if len(self._buf) >= SAMPLE_RATE * ASR_BLOCK_SEC:
                chunk = self._buf[-SAMPLE_RATE * ASR_BLOCK_SEC:].copy()
                self._buf = np.zeros(0, dtype=np.float32)
                self._asr_queue.put((chunk, time.time()))

    # ---------- ASR 工作线程 ----------
    def _asr_loop(self) -> None:
        while self._running:
            try:
                item = self._asr_queue.get(timeout=1)
            except queue.Empty:
                continue
            except Exception:
                break
            if item is None:
                break
            chunk, started = item
            self._transcribe(chunk, started)

    @staticmethod
    def _transcribe(audio: np.ndarray, started: float) -> None:
        try:
            from ..ai.asr import asr_engine
            text = asr_engine.transcribe(audio)
            if text and text.strip():
                # 契约要求 {ts, text}；start/end 为兼容旧消费方的附加字段
                bus.emit("perception.asr_segment", ts=int(started), text=text.strip(),
                         start=int(started), end=int(time.time()))
        except Exception as exc:
            log.debug(f"ASR 转写跳过: {exc}")

    def _flush_asr(self) -> None:
        if self._asr_enabled and len(self._buf) > SAMPLE_RATE * 5:
            chunk = self._buf.copy()
            self._buf = np.zeros(0, dtype=np.float32)
            self._asr_queue.put((chunk, time.time()))

    def current_db(self) -> float:
        return self._last_db
