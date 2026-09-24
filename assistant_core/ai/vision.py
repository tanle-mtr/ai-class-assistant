"""视觉识图（纯文本 AI 识图的核心）
调用 Ollama 本地视觉模型对图片做描述 / OCR，把结果转成文本，
供纯文本模型理解图片内容。
"""
from __future__ import annotations

import base64
from pathlib import Path

from ..config import config
from ..logger import get_logger
from .ollama_client import ollama

log = get_logger("ai.vision")


def _encode_image(image_path: str) -> str:
    return base64.b64encode(Path(image_path).read_bytes()).decode("ascii")


class VisionEngine:
    def __init__(self):
        self._model = config.get("vision_model") or ""

    def model(self) -> str:
        if not self._model:
            models = ollama.list_models()
            for m in models:
                name = m.get("name", "")
                if any(k in name.lower() for k in ("vl", "vision", "llava", "gemma3", "minicpm")):
                    self._model = name
                    config.set("vision_model", name)
                    break
        return self._model

    def available(self) -> bool:
        m = self.model()
        if not m:
            return False
        return any(x.get("name") == m for x in ollama.list_models())

    def describe(self, image_path: str, question: str = "请详细描述这张图片的内容。") -> str:
        """图片 → 文本描述/OCR 结果"""
        m = self.model()
        if not m:
            return "[识图不可用：未安装视觉模型，请运行 ollama pull gemma3:1b 或 qwen2.5vl:3b]"
        try:
            return ollama.generate(
                model=m,
                prompt=question,
                images=[_encode_image(image_path)],
                temperature=0.1,
            )
        except Exception as e:
            log.warning(f"识图失败: {e}")
            return f"[识图失败: {e}]"

    def ocr(self, image_path: str) -> str:
        """图片 OCR（扫描件、座位表等）"""
        return self.describe(
            image_path,
            "请把这张图片中的全部文字内容逐字识别出来（OCR），保持原有顺序；若为表格请按行列输出。",
        )


vision = VisionEngine()
