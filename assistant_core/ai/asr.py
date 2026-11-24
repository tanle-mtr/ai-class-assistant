"""本地语音转写（faster-whisper，CPU int8，惰性加载）。

模型下载/加载失败时返回空串并告警，不影响课堂其它功能。
"""
from __future__ import annotations

from typing import Any

import numpy as np

from .. import get_logger
from ..config import config

log = get_logger("ai.asr")


class AsrEngine:
    def __init__(self, model_size: str | None = None) -> None:
        self.model_size = model_size or config.get("asr_model", "small") or "small"
        self._model: Any = None
        self._failed = False

    def _ensure(self) -> Any | None:
        if self._model is not None:
            return self._model
        if self._failed:
            return None
        try:
            from faster_whisper import WhisperModel
            self._model = WhisperModel(self.model_size, device="cpu", compute_type="int8")
            log.info(f"ASR 模型加载完成: {self.model_size} (CPU int8)")
            return self._model
        except Exception as exc:
            self._failed = True  # 不在每次调用时重复尝试下载
            log.warning(f"ASR 模型不可用（转写功能降级）: {exc}")
            return None

    def transcribe(self, audio: np.ndarray, sample_rate: int = 16000) -> str:
        """16kHz 单声道 float32 [-1,1] → 转写文本。"""
        model = self._ensure()
        if model is None or audio is None or len(audio) == 0:
            return ""
        try:
            segments, _ = model.transcribe(audio, language="zh", beam_size=5, vad_filter=True)
            return "".join(seg.text for seg in segments).strip()
        except Exception as exc:
            log.debug(f"转写失败: {exc}")
            return ""

    def transcribe_file(self, wav_path: str) -> str:
        """转写整段 WAV（供归档/手动触发使用）。"""
        model = self._ensure()
        if model is None:
            return ""
        try:
            segments, _ = model.transcribe(wav_path, language="zh", beam_size=5, vad_filter=True)
            return "".join(seg.text for seg in segments).strip()
        except Exception as exc:
            log.warning(f"文件转写失败: {exc}")
            return ""


asr_engine = AsrEngine()
