"""视觉识图：本地视觉模型 OCR / 图片描述，把结果转成文本喂给纯文本模型。"""
from __future__ import annotations

import base64
from pathlib import Path

from .. import get_logger
from ..config import config, is_vision_model
from .ollama import ollama

log = get_logger("ai.vision")


def encode_image(path: str) -> str:
    return base64.b64encode(Path(path).read_bytes()).decode("ascii")


class VisionEngine:
    def __init__(self) -> None:
        self._model = config.get("vision_model") or ""

    def model(self) -> str:
        """优先用配置里的视觉模型；未配置则从 ollama list 自动挑一个。"""
        if not self._model:
            for item in ollama.list_models():
                name = str(item.get("name", ""))
                if is_vision_model(name):
                    self._model = name
                    config.set("vision_model", name)
                    break
        return self._model

    def available(self) -> bool:
        model = self.model()
        return bool(model) and any(
            normalize(m) == normalize(model) for m in ollama.model_names()
        )

    def describe(self, image_path: str, question: str = "请详细描述这张图片的内容。") -> str:
        model = self.model()
        if not model:
            return "[识图不可用：未安装视觉模型，请运行 ollama pull gemma3:1b 或 qwen2.5vl:3b]"
        try:
            return ollama.generate(
                model, question, images=[encode_image(image_path)], temperature=0.1,
            )
        except Exception as exc:
            log.warning(f"识图失败: {exc}")
            return f"[识图失败: {exc}]"

    def ocr(self, image_path: str) -> str:
        return self.describe(
            image_path,
            "请把这张图片中的全部文字逐字识别出来（OCR），保持原有顺序；若为表格请按行列输出。",
        )


def normalize(name: str) -> str:
    return (name or "").split(":", 1)[0].strip()


vision = VisionEngine()
