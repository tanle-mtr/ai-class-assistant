"""教科书本地知识库（RAG）。

链路：PDF/TXT/MD → pypdf 提取文本 → 分块 → Ollama bge-m3 嵌入 →
numpy 余弦相似度检索 top-k。Embedding 模型或 Ollama 不可用时返回空结果，
接口层据此给出「请先导入教材 / 请先 ollama pull bge-m3」提示，绝不 500。
"""
from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import numpy as np

from .. import get_logger
from ..config import config
from .ollama import OllamaError, normalize_model_name, ollama

log = get_logger("ai.rag")

CHUNK_SIZE = 500
CHUNK_OVERLAP = 50

# 地区 → {学科: 教材版本}（可被用户配置覆盖）
REGION_TEXTBOOKS: dict[str, dict[str, str]] = {
    "广州": {"语文": "人教版（部编版）", "数学": "人教版", "英语": "人教版（PEP）",
             "物理": "人教版", "化学": "人教版", "生物": "人教版",
             "历史": "人教版", "地理": "人教版", "道德与法治": "人教版"},
    "北京": {"语文": "人教版（部编版）", "数学": "人教版", "英语": "人教版（PEP）",
             "物理": "人教版", "化学": "人教版"},
    "上海": {"语文": "沪教版", "数学": "沪教版", "英语": "沪教版（牛津）"},
    "江苏": {"语文": "苏教版（部编版）", "数学": "苏教版", "英语": "译林版"},
    "浙江": {"语文": "人教版（部编版）", "数学": "浙教版", "英语": "人教版（PEP）"},
    "四川": {"语文": "人教版（部编版）", "数学": "人教版", "英语": "外研版"},
}
OFFICIAL_FREE_SITE = "国家中小学智慧教育平台 https://basic.smartedu.cn/"


def match_textbooks(grade: str, region: str) -> dict[str, str]:
    """年级 + 地区 → {学科: 版本}；未知地区默认人教版。"""
    region_key = next((k for k in REGION_TEXTBOOKS if k in (region or "")), None)
    if region_key is None:
        books = {s: "人教版" for s in ("语文", "数学", "英语", "物理", "化学", "生物", "历史", "地理")}
    else:
        books = dict(REGION_TEXTBOOKS[region_key])
    if grade and "小学" in grade:
        books = {k: v for k, v in books.items() if k in ("语文", "数学", "英语")}
    elif grade and "高中" in grade:
        for subject in ("物理", "化学", "生物"):
            books[subject] = "人教版（新教材）"
    return books


def _split_text(text: str) -> list[str]:
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= CHUNK_SIZE:
        return [text] if text else []
    chunks: list[str] = []
    start = 0
    while start < len(text):
        end = min(start + CHUNK_SIZE, len(text))
        chunks.append(text[start:end])
        if end >= len(text):
            break
        start = end - CHUNK_OVERLAP
    return chunks


class TextbookIndex:
    """教材索引：index.json（元数据 + 原文块）+ vectors.npy（向量矩阵）。"""

    def __init__(self) -> None:
        self._dir = config.dir("textbooks")
        self._meta_path = self._dir / "index.json"
        self._vec_path = self._dir / "vectors.npy"
        self._meta: dict[str, Any] = {"embed_model": "", "chunks": [], "books": {}}
        self._vecs = np.zeros((0, 0), dtype=np.float32)
        self._load()

    # ---------- 持久化 ----------
    def _load(self) -> None:
        if not (self._meta_path.exists() and self._vec_path.exists()):
            return
        try:
            self._meta = json.loads(self._meta_path.read_text(encoding="utf-8"))
            self._vecs = np.load(self._vec_path, allow_pickle=False)
        except Exception as exc:
            log.warning(f"知识库加载失败（按空库处理）: {exc}")
            self._meta = {"embed_model": "", "chunks": [], "books": {}}
            self._vecs = np.zeros((0, 0), dtype=np.float32)

    def save(self) -> None:
        try:
            self._meta_path.write_text(
                json.dumps(self._meta, ensure_ascii=False, indent=2), encoding="utf-8",
            )
            np.save(self._vec_path, self._vecs)
        except Exception as exc:
            log.warning(f"知识库保存失败: {exc}")

    # ---------- 建库 ----------
    def add_textbook(self, title: str, chapters: list[dict[str, str]],
                     embed_model: str | None = None) -> int:
        try:
            model = ollama.ensure_embedding_model(embed_model)
        except OllamaError as exc:
            log.warning(str(exc))
            return 0
        self._meta["embed_model"] = model
        added = 0
        for chapter in chapters:
            for chunk in _split_text(chapter.get("text", "")):
                try:
                    vector = ollama.embed(model, chunk)
                except Exception as exc:
                    log.debug(f"Embedding 失败（跳过该块）: {exc}")
                    continue
                if not vector:
                    continue
                self._meta["chunks"].append({
                    "book": title,
                    "chapter": chapter.get("chapter", ""),
                    "text": chunk,
                })
                try:
                    row = np.array(vector, dtype=np.float32).reshape(1, -1)
                    self._vecs = row if self._vecs.size == 0 else np.vstack([self._vecs, row])
                except Exception as exc:
                    log.debug(f"向量拼接失败（跳过）: {exc}")
                    self._meta["chunks"].pop()
                    continue
                added += 1
        self._meta["books"][title] = {
            "chapters": [c.get("chapter", "") for c in chapters], "chunks": added,
        }
        self.save()
        log.info(f"知识库新增 {added} 个片段（{title}）")
        return added

    # ---------- 检索 ----------
    def retrieve(self, query: str, top_k: int = 5) -> list[dict[str, Any]]:
        if not self._meta["chunks"] or self._vecs.size == 0:
            return []
        try:
            query_vec = ollama.embed(self._meta["embed_model"], query)
        except Exception as exc:
            log.warning(f"查询向量化失败: {exc}")
            return []
        if not query_vec:
            return []
        q = np.array(query_vec, dtype=np.float32)
        try:
            denominator = (np.linalg.norm(self._vecs, axis=1) * np.linalg.norm(q)) + 1e-9
            similarities = (self._vecs @ q) / denominator
        except Exception as exc:
            log.warning(f"余弦检索失败: {exc}")
            return []
        indices = np.argsort(-similarities)[:max(1, min(top_k, 20))]
        hits: list[dict[str, Any]] = []
        for i in indices:
            score = float(similarities[i])
            if score < 0.3:
                continue
            chunk = self._meta["chunks"][i]
            chapter = str(chunk.get("chapter", ""))
            page_match = re.search(r"第(\d+)页", chapter)
            hits.append({
                "book": chunk.get("book", ""),
                "chapter": chapter,
                "page": page_match.group(1) if page_match else chapter,
                "text": str(chunk.get("text", ""))[:500],
                "score": round(score, 3),
            })
        return hits

    def stats(self) -> dict[str, Any]:
        books = self._meta.get("books", {})
        return {
            "books": list(books.keys()),
            "book_detail": books,
            "chunks": len(self._meta.get("chunks", [])),
            "embed_model": self._meta.get("embed_model", ""),
            "ready": bool(self._meta.get("chunks")),
        }


textbook_index = TextbookIndex()


# ---------- 文件解析 ----------
def extract_text(path: str) -> list[dict[str, str]]:
    """文件 → [{chapter, text}]，支持 PDF / TXT / MD。"""
    source = Path(path)
    if not source.exists():
        raise FileNotFoundError(f"文件不存在: {source}")
    suffix = source.suffix.lower()
    if suffix == ".pdf":
        return _extract_pdf(source)
    if suffix in (".txt", ".md"):
        return _extract_text_file(source)
    raise ValueError(f"暂不支持的文件类型 {suffix or '(无扩展名)'}，仅支持 PDF / TXT / MD")


def _extract_pdf(path: Path) -> list[dict[str, str]]:
    try:
        from pypdf import PdfReader
    except ImportError as exc:
        raise RuntimeError("缺少 PDF 解析依赖 pypdf，请执行: pip install pypdf") from exc
    try:
        reader = PdfReader(str(path))
    except Exception as exc:
        raise ValueError(f"PDF 打开失败（可能损坏或加密）: {path.name}（{exc}）") from exc
    chapters: list[dict[str, str]] = []
    for number, page in enumerate(reader.pages, 1):
        try:
            text = (page.extract_text() or "").strip()
        except Exception:
            continue
        if len(text) >= 10:
            chapters.append({"chapter": f"第{number}页", "text": text})
    if not chapters:
        raise ValueError(
            f"未能从 PDF 提取到文字：{path.name}（可能是图片版扫描件，请使用文字版 PDF，"
            f"或先 OCR 后以 TXT/MD 导入）"
        )
    return chapters


def _extract_text_file(path: Path) -> list[dict[str, str]]:
    raw = path.read_text(encoding="utf-8", errors="ignore")
    if not raw.strip():
        raise ValueError(f"文件内容为空: {path.name}")
    heading = re.compile(r"^\s{0,3}(#{1,6}\s+|第[0-9一二三四五六七八九十百]+[章节篇讲]\s*)")
    chapters: list[dict[str, str]] = []
    title, buffer = "第1节", []
    for line in raw.splitlines():
        if heading.match(line):
            text = "\n".join(buffer).strip()
            if text:
                chapters.append({"chapter": title, "text": text})
            title = line.strip().lstrip("#").strip() or title
            buffer = []
        else:
            buffer.append(line)
    text = "\n".join(buffer).strip()
    if text:
        chapters.append({"chapter": title, "text": text})
    if not chapters:
        raise ValueError(f"未能解析出内容: {path.name}")
    return chapters


def add_book(title: str, path: str, embed_model: str | None = None) -> int:
    """导入教材：解析文本 → 向量建库，返回片段数（0 表示失败）。"""
    chapters = extract_text(path)
    count = textbook_index.add_textbook(title, chapters, embed_model)
    if count == 0:
        log.warning(f"《{title}》未入库：embedding 模型不可用，请执行 ollama pull bge-m3")
    return count


def guess_region_by_ip(timeout: float = 3.0) -> str:
    """按公网 IP 粗略定位省份；失败返回空串，绝不阻塞/抛异常。"""
    import requests
    services = (
        ("https://ipapi.co/json/", ("region",)),
        ("http://ip-api.com/json/", ("regionName", "region")),
    )
    for url, keys in services:
        try:
            data = requests.get(url, timeout=timeout).json()
            for key in keys:
                value = str(data.get(key, "") or "").strip()
                if value:
                    return value
        except Exception as exc:
            log.debug(f"IP 定位失败（{url}）: {exc}")
    return ""


# 兼容旧调用方的别名（去 tag 归一化工具对外可用）
normalize_name = normalize_model_name
