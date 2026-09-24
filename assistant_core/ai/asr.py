"""本地语音转写（faster-whisper，CPU int8）
全局单例 asr_engine：上课时把 16kHz 音频块转成文本
"""
from __future__ import annotations

import numpy as np

from ..config import config
from ..logger import get_logger

log = get_logger("ai.asr")


class AsrEngine:
    def __init__(self, model_size: str = "small"):
        self.model_size = model_size
        self._model = None
        self._lock = None

    def _ensure(self):
        if self._model is not None:
            return self._model
        try:
            from faster_whisper import WhisperModel
            self._model = WhisperModel(self.model_size, device="cpu", compute_type="int8")
            log.info(f"ASR 模型加载完成: {self.model_size} (CPU int8)")
            return self._model
        except ImportError:
            log.error("faster-whisper 未安装，请先 pip install faster-whisper")
            raise
        except Exception as e:
            log.error(f"ASR 模型加载失败: {e}")
            raise

    def transcribe(self, audio: np.ndarray, sample_rate: int = 16000) -> str:
        """audio: 16k 单声道 float32 [-1,1]，返回转写文本"""
        model = self._ensure()
        if audio is None or len(audio) == 0:
            return ""
        try:
            segments, _info = model.transcribe(
                audio, language="zh", beam_size=5, vad_filter=True,
            )
            return "".join(seg.text for seg in segments).strip()
        except Exception as e:
            log.warning(f"转写失败: {e}")
            return ""

    def transcribe_file(self, wav_path: str) -> str:
        model = self._ensure()
        try:
            segments, _info = model.transcribe(wav_path, language="zh", beam_size=5, vad_filter=True)
            return "".join(seg.text for seg in segments).strip()
        except Exception as e:
            log.warning(f"文件转写失败: {e}")
            return ""


# 全局单例（audio_capture 引用）
asr_engine = AsrEngine(model_size=config.get("asr_model", "small"))
