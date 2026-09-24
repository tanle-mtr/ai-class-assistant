"""Ollama 客户端：chat / generate / embeddings / Modelfile 打包"""
from __future__ import annotations

import base64
import json
import shutil
from pathlib import Path

import requests

from ..config import config
from ..logger import get_logger

log = get_logger("ai.ollama")

OLLAMA_HOST = "http://127.0.0.1:11434"


class OllamaError(Exception):
    pass


class OllamaClient:
    def __init__(self, host: str = OLLAMA_HOST):
        self.host = host.rstrip("/")

    # ---------- 基础 ----------
    def ping(self) -> bool:
        try:
            return requests.get(f"{self.host}/api/tags", timeout=3).status_code == 200
        except Exception:
            return False

    def list_models(self) -> list[dict]:
        try:
            r = requests.get(f"{self.host}/api/tags", timeout=10)
            r.raise_for_status()
            return r.json().get("models", [])
        except Exception as e:
            log.warning(f"查询 ollama 模型失败: {e}")
            return []

    # ---------- 对话 ----------
    def chat(self, model: str, messages: list[dict], options: dict | None = None,
             temperature: float = 0.3, stream: bool = False, timeout: int = 300) -> str:
        """messages: [{"role": "system"/"user"/"assistant", "content": ...}]"""
        payload = {
            "model": model,
            "messages": messages,
            "stream": stream,
            "options": {"temperature": temperature, **(options or {})},
        }
        try:
            if stream:
                r = requests.post(f"{self.host}/api/chat", json=payload, stream=True, timeout=timeout)
                r.raise_for_status()
                parts = []
                for line in r.iter_lines(decode_unicode=True):
                    if not line:
                        continue
                    obj = json.loads(line)
                    parts.append(obj.get("message", {}).get("content", ""))
                    if obj.get("done"):
                        break
                return "".join(parts)
            r = requests.post(f"{self.host}/api/chat", json=payload, timeout=timeout)
            r.raise_for_status()
            return r.json().get("message", {}).get("content", "")
        except requests.RequestException as e:
            raise OllamaError(f"Ollama 对话失败: {e}") from e

    # ---------- 生成（单 prompt） ----------
    def generate(self, model: str, prompt: str, images: list[str] | None = None,
                 options: dict | None = None, temperature: float = 0.3, timeout: int = 300) -> str:
        payload = {"model": model, "prompt": prompt, "stream": False,
                   "options": {"temperature": temperature, **(options or {})}}
        if images:
            payload["images"] = images
        try:
            r = requests.post(f"{self.host}/api/generate", json=payload, timeout=timeout)
            r.raise_for_status()
            return r.json().get("response", "")
        except requests.RequestException as e:
            raise OllamaError(f"Ollama 生成失败: {e}") from e

    # ---------- Embedding ----------
    def embed(self, model: str, text: str) -> list[float]:
        try:
            r = requests.post(f"{self.host}/api/embeddings",
                              json={"model": model, "prompt": text}, timeout=60)
            r.raise_for_status()
            return r.json().get("embedding", [])
        except requests.RequestException as e:
            raise OllamaError(f"Embedding 失败: {e}") from e

    # ---------- Modelfile 打包（c 方案） ----------
    def create_model(self, name: str, modelfile: str) -> bool:
        """通过 Modelfile 创建自定义模型"""
        payload = {"name": name, "modelfile": modelfile}
        try:
            r = requests.post(f"{self.host}/api/create", json=payload, timeout=600)
            if r.status_code == 200:
                log.info(f"自定义模型创建成功: {name}")
                return True
            log.error(f"创建模型失败: {r.text[:300]}")
            return False
        except requests.RequestException as e:
            log.error(f"创建模型失败: {e}")
            return False

    @staticmethod
    def default_embedding_model() -> str:
        """推荐 embedding 模型（bge-m3 中文效果好）"""
        return "bge-m3"

    def ensure_embedding_model(self, model: str | None = None) -> str:
        """确保 embedding 模型可用，不可用则提示。

        注意：Ollama 的 /api/tags 返回的模型名常带 tag（如 bge-m3:latest），
        而 embed() 传不带 tag 的名字（bge-m3）也能被 Ollama 自动补 :latest。
        因此这里做"去 tag 归一化"匹配，避免误判模型不存在。
        """
        m = model or self.default_embedding_model()

        def norm(name: str) -> str:
            return (name or "").split(":", 1)[0].strip()

        norm_m = norm(m)
        models = {x.get("name", "") for x in self.list_models()}
        pool = set(models)
        pool.update(norm(x) for x in models)  # 同时收录去 tag 形态

        if m in pool or norm_m in pool:
            return m
        # 常见别名
        for cand in ("bge-m3", "nomic-embed-text", "mxbai-embed-large"):
            if cand in pool or norm(cand) in pool:
                return cand
        raise OllamaError(
            f"缺少 embedding 模型 {m}，请在终端执行: ollama pull {m}"
        )


ollama = OllamaClient()
