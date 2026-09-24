"""麦克风采集（WASAPI 共享模式，不独占设备）
- 16kHz 单声道采集，供分贝分析与 ASR
- 按课程切片录音（WAV）
- 分贝样本事件：perception.db_sample {sec, db}
- ASR 增量转写事件：perception.asr_segment {start, end, text}
"""
from __future__ import annotations

import queue
import threading
import time
import wave
from datetime import datetime
from pathlib import Path

import numpy as np

from ..config import config
from ..events import bus
from ..logger import get_logger

log = get_logger("perception.audio")

SAMPLE_RATE = 16000
CHANNELS = 1
DTYPE = np.int16
BLOCK_SEC = 1          # 分贝采样间隔（秒）
ASR_BLOCK_SEC = 30     # ASR 增量窗口（秒）


def _rms_db(samples: np.ndarray) -> float:
    """计算分贝（相对满幅 0dBFS，加偏移使日常语音落在 30~80 区间）"""
    if len(samples) == 0:
        return 0.0
    rms = float(np.sqrt(np.mean(np.square(samples.astype(np.float32)))))
    if rms < 1e-7:
        return 0.0
    return round(90.0 + 20.0 * np.log10(rms), 1)


class AudioMonitor:
    """麦克风监控：常驻共享采集，无课程时仅计数分贝；上课时录音 + ASR"""

    def __init__(self):
        self._stream = None
        self._running = False
        self._thread = None
        self._recording = False
        self._wav: wave.Wave_write | None = None
        self._session = None
        self._buf = np.zeros(0, dtype=np.float32)
        self._chunks: list[np.ndarray] = []
        self._chunk_sec = 0
        self._asr_queue: queue.Queue = queue.Queue()
        self._asr_worker = None
        self._asr_enabled = False
        self._segment_start = 0.0
        try:
            import sounddevice as sd
            self._sd = sd
        except ImportError:
            self._sd = None
            log.warning("sounddevice 未安装，麦克风功能不可用")

    # ---------- 生命周期 ----------
    def start(self):
        if self._running or self._sd is None:
            return
        self._running = True
        self._thread = threading.Thread(target=self._capture_loop, daemon=True, name="audio-capture")
        self._thread.start()
        self._asr_worker = threading.Thread(target=self._asr_loop, daemon=True, name="asr-worker")
        self._asr_worker.start()
        log.info("麦克风监控已启动（共享模式）")

    def stop(self):
        self._running = False
        if self._stream:
            try:
                self._stream.stop()
                self._stream.close()
            except Exception:
                pass
            self._stream = None
        self._stop_recording()

    # ---------- 录音控制 ----------
    def start_recording(self, session):
        """上课：开始录音与 ASR（先关闭监控切片句柄，避免 WAV 头丢失与句柄泄漏）"""
        self._stop_recording()
        self._session = session
        self._chunks = []
        self._chunk_sec = 0
        self._asr_enabled = not session.is_exam_or_study
        try:
            rec_dir = config.dir("recordings") / session.start_time.strftime("%Y-%m-%d")
            rec_dir.mkdir(parents=True, exist_ok=True)
            safe_subject = "".join(c for c in session.subject if c not in '\\/:*?"<>|') or "未知"
            path = rec_dir / f"{session.start_time.strftime('%H%M%S')}-{safe_subject}-音频.wav"
            self._wav = wave.open(str(path), "wb")
            self._wav.setnchannels(CHANNELS)
            self._wav.setsampwidth(2)
            self._wav.setframerate(SAMPLE_RATE)
            session.audio_file = path
            self._recording = True
            log.info(f"录音开始: {path.name}")
        except Exception as e:
            log.warning(f"录音初始化失败: {e}")
            self._recording = False

    def stop_recording(self, session):
        self._session = None
        self._stop_recording()

    def _stop_recording(self):
        self._recording = False
        self._asr_enabled = False
        if self._wav:
            try:
                self._wav.close()
            except Exception:
                pass
            self._wav = None
        self._segment_start = 0.0
        self._flush_asr()

    # ---------- 监控常驻录音 ----------
    def _ensure_auto_recording(self):
        """监控默认开启：无课程会话时按 30 分钟切片录音"""
        if not config.get("monitor_enabled", True) or not config.get("record_audio", True):
            return
        if self._recording and self._wav:
            if self._segment_start and time.time() - self._segment_start >= 1800:
                self._stop_recording()
                self._start_monitor_segment()
            return
        if self._session is None:
            self._start_monitor_segment()

    def _start_monitor_segment(self):
        try:
            rec_dir = config.dir("recordings") / datetime.now().strftime("%Y-%m-%d")
            rec_dir.mkdir(parents=True, exist_ok=True)
            path = rec_dir / f"{datetime.now().strftime('%H%M%S')}-监控-音频.wav"
            self._wav = wave.open(str(path), "wb")
            self._wav.setnchannels(CHANNELS)
            self._wav.setsampwidth(2)
            self._wav.setframerate(SAMPLE_RATE)
            self._recording = True
            self._segment_start = time.time()
            log.debug(f"监控录音切片开始: {path.name}")
        except Exception as e:
            log.warning(f"监控录音初始化失败: {e}")
            self._recording = False

    # ---------- 采集循环 ----------
    def _capture_loop(self):
        try:
            with self._sd.InputStream(
                samplerate=SAMPLE_RATE,
                channels=CHANNELS,
                dtype=DTYPE,
                blocksize=SAMPLE_RATE * BLOCK_SEC,  # 1s 一块
            ) as stream:
                self._stream = stream
                while self._running:
                    data, _ = stream.read(SAMPLE_RATE * BLOCK_SEC)
                    samples = data.reshape(-1).astype(np.float32) / 32768.0
                    self._on_block(samples)
        except Exception as e:
            log.warning(f"麦克风采集异常（可能被其他程序独占，稍后重试）: {e}")
            # 共享模式失败：等待后重启（不抢占）
            time.sleep(3)
            if self._running:
                self._thread = threading.Thread(target=self._capture_loop, daemon=True, name="audio-capture")
                self._thread.start()

    def _on_block(self, samples: np.ndarray):
        now = int(time.time())
        self._ensure_auto_recording()
        db = _rms_db(samples * 32768.0)
        self._last_db = db
        bus.emit("perception.db_sample", sec=now, db=db)

        if self._recording and self._wav:
            self._wav.writeframes((samples * 32768.0).astype(np.int16).tobytes())

        # ASR 攒块
        if self._asr_enabled:
            self._buf = np.concatenate([self._buf, samples])
            if len(self._buf) >= SAMPLE_RATE * ASR_BLOCK_SEC:
                chunk = self._buf[-SAMPLE_RATE * ASR_BLOCK_SEC:].copy()
                self._buf = np.zeros(0, dtype=np.float32)
                self._asr_queue.put((chunk, time.time()))

    def _asr_loop(self):
        while self._running:
            try:
                item = self._asr_queue.get(timeout=1)
                if item is None:
                    break
                chunk, t0 = item
                self._transcribe(chunk, t0)
            except queue.Empty:
                continue
            except Exception as e:
                log.warning(f"ASR 工作线程异常: {e}")

    def _transcribe(self, audio: np.ndarray, t0: float):
        """调用 ASR 引擎（惰性导入避免循环依赖）"""
        from ..ai.asr import asr_engine
        try:
            text = asr_engine.transcribe(audio)
            if text and text.strip():
                seg = {"start": int(t0), "end": int(time.time()), "text": text.strip()}
                bus.emit("perception.asr_segment", **seg)
                log.info(f"[转写] {text.strip()[:60]}")
        except Exception as e:
            log.warning(f"转写失败: {e}")

    def _flush_asr(self):
        """下课：把残留音频转写掉"""
        if self._asr_enabled and len(self._buf) > SAMPLE_RATE * 5:
            chunk = self._buf.copy()
            self._buf = np.zeros(0, dtype=np.float32)
            self._asr_queue.put((chunk, time.time()))

    def current_db(self) -> float:
        return self._last_db if hasattr(self, "_last_db") else 0.0
