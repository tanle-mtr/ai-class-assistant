"""Ollama 客户端：list / chat / generate / embed / create_model。

所有网络调用都在本地 HTTP 127.0.0.1:11434；Ollama 未启动时返回空值或抛
OllamaError（由调用方捕获降级），绝不导致 API 500 或进程崩溃。
"""
from __future__ import annotations

from typing import Any

import requests

from .. import get_logger

log = get_logger("ai.ollama")

OLLAMA_HOST = "http://127.0.0.1:11434"
DEFAULT_EMBED_MODEL = "bge-m3"


class OllamaError(Exception):
    """Ollama 不可用 / 调用失败。"""


def normalize_model_name(name: str) -> str:
    """去掉 tag 归一化匹配：qwen3:1.7b -> qwen3，bge-m3:latest -> bge-m3。"""
    return (name or "").split(":", 1)[0].strip()


class OllamaClient:
    def __init__(self, host: str = OLLAMA_HOST) -> None:
        self.host = host.rstrip("/")

    # ---------- 基础 ----------
    def ping(self) -> bool:
        try:
            return requests.get(f"{self.host}/api/tags", timeout=2).status_code == 200
        except Exception:
            return False

    def list_models(self) -> list[dict[str, Any]]:
        """返回 Ollama 已安装模型；未启动返回 []。"""
        try:
            response = requests.get(f"{self.host}/api/tags", timeout=5)
            response.raise_for_status()
            return response.json().get("models", []) or []
        except Exception as exc:
            log.debug(f"查询 Ollama 模型失败: {exc}")
            return []

    def model_names(self) -> list[str]:
        return [str(m.get("name", "")) for m in self.list_models() if m.get("name")]

    # ---------- 对话 / 生成 ----------
    def chat(self, model: str, messages: list[dict[str, str]], *,
             temperature: float = 0.3, timeout: int = 300,
             options: dict[str, Any] | None = None) -> str:
        if not model:
            raise OllamaError("未配置主力模型")
        payload = {
            "model": model,
            "messages": messages,
            "stream": False,
            "options": {"temperature": temperature, **(options or {})},
        }
        try:
            response = requests.post(f"{self.host}/api/chat", json=payload, timeout=timeout)
            response.raise_for_status()
            return str(response.json().get("message", {}).get("content", "") or "")
        except Exception as exc:
            raise OllamaError(f"Ollama 对话失败: {exc}") from exc

    def generate(self, model: str, prompt: str, *, images: list[str] | None = None,
                 temperature: float = 0.3, timeout: int = 300) -> str:
        if not model:
            raise OllamaError("未配置模型")
        payload: dict[str, Any] = {
            "model": model, "prompt": prompt, "stream": False,
            "options": {"temperature": temperature},
        }
        if images:
            payload["images"] = images
        try:
            response = requests.post(f"{self.host}/api/generate", json=payload, timeout=timeout)
            response.raise_for_status()
            return str(response.json().get("response", "") or "")
        except Exception as exc:
            raise OllamaError(f"Ollama 生成失败: {exc}") from exc

    # ---------- 向量 ----------
    def embed(self, model: str, text: str) -> list[float]:
        if not model:
            raise OllamaError("未配置 embedding 模型")
        try:
            response = requests.post(
                f"{self.host}/api/embeddings",
                json={"model": model, "prompt": text}, timeout=60,
            )
            response.raise_for_status()
            vector = response.json().get("embedding", []) or []
            return [float(v) for v in vector]
        except Exception as exc:
            raise OllamaError(f"Embedding 失败: {exc}") from exc

    def ensure_embedding_model(self, model: str | None = None) -> str:
        """确认 embedding 模型可用；按去 tag 名称匹配，避免把 bge-m3 误判为缺失。

        返回实际可用的模型名（保留用户传入的写法）；不可用时抛 OllamaError。
        """
        requested = model or DEFAULT_EMBED_MODEL
        pool: set[str] = set()
        for name in self.model_names():
            pool.add(name)
            pool.add(normalize_model_name(name))
        if requested in pool or normalize_model_name(requested) in pool:
            return requested
        for candidate in ("bge-m3", "nomic-embed-text", "mxbai-embed-large"):
            if candidate in pool:
                return candidate
        raise OllamaError(
            f"缺少 embedding 模型 {requested}，请在终端执行: ollama pull {requested}"
        )

    # ---------- Modelfile 打包专属模型 ----------
    def create_model(self, name: str, modelfile: str) -> bool:
        try:
            response = requests.post(
                f"{self.host}/api/create",
                json={"name": name, "modelfile": modelfile}, timeout=600,
            )
            if response.status_code == 200:
                log.info(f"专属模型创建成功: {name}")
                return True
            log.warning(f"创建模型失败: {response.text[:200]}")
            return False
        except Exception as exc:
            log.warning(f"创建模型失败: {exc}")
            return False


ollama = OllamaClient()
