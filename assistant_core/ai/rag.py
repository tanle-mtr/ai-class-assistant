"""教科书本地知识库（RAG）
- 教材匹配：年级 + 地区 → 教材版本清单
- 索引：文本切块 → ollama embedding → 本地持久化（npz + json）
- 检索：余弦相似度 top-k
"""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import numpy as np

from ..config import config
from ..logger import get_logger
from .ollama_client import ollama, OllamaError

log = get_logger("ai.rag")

CHUNK_SIZE = 500
CHUNK_OVERLAP = 50

# 常见地区教材版本映射（可被用户配置覆盖）
# region -> {学科: 版本}
REGION_TEXTBOOKS: dict[str, dict[str, str]] = {
    "广州": {
        "语文": "人教版（部编版）", "数学": "人教版", "英语": "人教版（PEP）",
        "物理": "人教版", "化学": "人教版", "生物": "人教版",
        "历史": "人教版", "地理": "人教版", "道德与法治": "人教版",
    },
    "北京": {"语文": "人教版（部编版）", "数学": "人教版", "英语": "人教版（PEP）", "物理": "人教版", "化学": "人教版"},
    "上海": {"语文": "沪教版", "数学": "沪教版", "英语": "沪教版（牛津）"},
    "江苏": {"语文": "苏教版（部编版）", "数学": "苏教版", "英语": "译林版"},
    "浙江": {"语文": "人教版（部编版）", "数学": "浙教版", "英语": "人教版（PEP）"},
    "四川": {"语文": "人教版（部编版）", "数学": "人教版", "英语": "外研版"},
}
GRADE_STAGES = ["小学", "初中", "高中"]


def match_textbooks(grade: str, region: str) -> dict[str, str]:
    """根据年级+地区返回教材版本建议清单"""
    region_key = None
    for k in REGION_TEXTBOOKS:
        if k in (region or ""):
            region_key = k
            break
    if region_key is None:
        # 默认人教版
        return {s: "人教版" for s in ["语文", "数学", "英语", "物理", "化学", "生物", "历史", "地理"]}
    books = dict(REGION_TEXTBOOKS[region_key])
    if grade and "小学" in grade:
        books = {k: v for k, v in books.items() if k in ("语文", "数学", "英语")}
    elif grade and "高中" in grade:
        books["物理"] = "人教版（新教材）"
        books["化学"] = "人教版（新教材）"
        books["生物"] = "人教版（新教材）"
    return books


def _split_text(text: str) -> list[str]:
    text = re.sub(r"\s+", " ", text).strip()
    if len(text) <= CHUNK_SIZE:
        return [text] if text else []
    chunks = []
    start = 0
    while start < len(text):
        end = min(start + CHUNK_SIZE, len(text))
        chunks.append(text[start:end])
        if end >= len(text):
            break
        start = end - CHUNK_OVERLAP
    return chunks


class TextbookIndex:
    """教材知识库索引（embeddings + 原文）"""

    def __init__(self):
        self._dir = config.dir("textbooks")
        self._dir.mkdir(parents=True, exist_ok=True)
        self._meta_path = self._dir / "index.json"
        self._vec_path = self._dir / "vectors.npy"
        self._meta: dict = {"embed_model": "", "chunks": [], "books": {}}
        self._vecs: np.ndarray = np.zeros((0, 0), dtype=np.float32)
        self._load()

    def _load(self):
        if self._meta_path.exists() and self._vec_path.exists():
            try:
                self._meta = json.loads(self._meta_path.read_text(encoding="utf-8"))
                self._vecs = np.load(self._vec_path, allow_pickle=False)
            except Exception as e:
                log.warning(f"知识库加载失败: {e}")

    def save(self):
        try:
            self._meta_path.write_text(json.dumps(self._meta, ensure_ascii=False, indent=2), encoding="utf-8")
            np.save(self._vec_path, self._vecs)
        except Exception as e:
            log.error(f"知识库保存失败: {e}")

    # ---------- 建库 ----------
    def add_textbook(self, title: str, chapters: list[dict], embed_model: str | None = None) -> int:
        """chapters: [{"chapter": "第1章...", "text": "..."}]，返回新增片段数"""
        try:
            em = ollama.ensure_embedding_model(embed_model)
        except OllamaError as e:
            log.warning(str(e))
            return 0
        self._meta["embed_model"] = em
        n = 0
        for ch in chapters:
            for chunk in _split_text(ch.get("text", "")):
                try:
                    vec = ollama.embed(em, chunk)
                except Exception as e:
                    log.warning(f"embedding 失败（跳过该块）: {e}")
                    continue
                self._meta["chunks"].append({
                    "book": title,
                    "chapter": ch.get("chapter", ""),
                    "text": chunk,
                })
                if self._vecs.size == 0:
                    self._vecs = np.array([vec], dtype=np.float32)
                else:
                    self._vecs = np.vstack([self._vecs, np.array(vec, dtype=np.float32)])
                n += 1
        self._meta["books"][title] = {"chapters": [c.get("chapter") for c in chapters], "chunks": n}
        self.save()
        log.info(f"知识库新增 {n} 个片段（{title}）")
        return n

    # ---------- 检索 ----------
    def retrieve(self, query: str, top_k: int = 5) -> list[dict]:
        if not self._meta["chunks"] or self._vecs.size == 0:
            return []
        try:
            qv = ollama.embed(self._meta["embed_model"], query)
        except Exception as e:
            log.warning(f"查询向量化失败: {e}")
            return []
        q = np.array(qv, dtype=np.float32)
        denom = (np.linalg.norm(self._vecs, axis=1) * np.linalg.norm(q)) + 1e-9
        sims = (self._vecs @ q) / denom
        idx = np.argsort(-sims)[:top_k]
        out = []
        for i in idx:
            if sims[i] < 0.3:  # 相似度下限
                continue
            ch = self._meta["chunks"][i]
            out.append({
                "book": ch["book"],
                "chapter": ch["chapter"],
                "text": ch["text"][:500],
                "score": round(float(sims[i]), 3),
            })
        return out

    def stats(self) -> dict:
        return {
            "books": self._meta.get("books", {}),
            "chunks": len(self._meta["chunks"]),
            "embed_model": self._meta.get("embed_model", ""),
        }


textbook_index = TextbookIndex()

# 官方公开免费电子教材渠道（无免费版时提示用户自行导入 PDF，不做盗版绕行）
OFFICIAL_FREE_SITE = "国家中小学智慧教育平台 https://basic.smartedu.cn/"


def extract_text(path: str) -> list[dict]:
    """教材文件 → 片段列表 [{"chapter": "第N页/第N章", "text": "..."}]

    支持 PDF（pypdf 逐页提取）/ txt / md；解析失败给出明确错误。
    """
    p = Path(path)
    if not p.exists():
        raise FileNotFoundError(f"文件不存在: {p}")
    suffix = p.suffix.lower()
    if suffix == ".pdf":
        return _extract_pdf(p)
    if suffix in (".txt", ".md"):
        return _extract_text_file(p)
    raise ValueError(f"暂不支持的文件类型 {suffix or '(无扩展名)'}，仅支持 PDF / TXT / MD")


def _extract_pdf(p: Path) -> list[dict]:
    """PDF 逐页提取文本（pypdf）"""
    try:
        from pypdf import PdfReader
    except ImportError as e:
        raise RuntimeError("缺少 PDF 解析依赖 pypdf，请执行: pip install pypdf") from e
    try:
        reader = PdfReader(str(p))
    except Exception as e:
        raise ValueError(f"PDF 打开失败（文件可能损坏或已加密）: {p.name}（{e}）") from e
    out: list[dict] = []
    for i, page in enumerate(reader.pages, 1):
        try:
            txt = (page.extract_text() or "").strip()
        except Exception as e:
            log.warning(f"第 {i} 页解析失败（跳过）: {e}")
            continue
        if len(txt) < 10:  # 空白页/纯图片页
            continue
        out.append({"chapter": f"第{i}页", "text": txt})
    if not out:
        raise ValueError(
            f"未能从 PDF 提取到文字：{p.name}（可能是扫描件图片版 PDF，"
            f"请改用文字版 PDF，或先做 OCR 后以 TXT/MD 导入）"
        )
    return out


def _extract_text_file(p: Path) -> list[dict]:
    """txt/md → 按章节标题切分；无标题时按“第N节”分段"""
    raw = p.read_text(encoding="utf-8", errors="ignore")
    if not raw.strip():
        raise ValueError(f"文件内容为空: {p.name}")
    # 章节标题：md 的 # 标题，或“第一章 / 第3节”之类
    heading_re = re.compile(r"^\s{0,3}(#{1,6}\s+|第[0-9一二三四五六七八九十百]+[章节篇讲]\s*)")
    chapters: list[dict] = []
    cur_title, buf = "第1节", []
    for line in raw.splitlines():
        if heading_re.match(line):
            text = "\n".join(buf).strip()
            if text:
                chapters.append({"chapter": cur_title, "text": text})
            cur_title = line.strip().lstrip("#").strip() or cur_title
            buf = []
        else:
            buf.append(line)
    text = "\n".join(buf).strip()
    if text:
        chapters.append({"chapter": cur_title, "text": text})
    if not chapters:
        raise ValueError(f"未能解析出内容: {p.name}")
    return chapters


def add_book(title: str, path: str, embed_model: str | None = None) -> int:
    """导入一本教材：解析文本 → 向量建库，返回片段数（0 表示失败）"""
    chapters = extract_text(path)
    n = textbook_index.add_textbook(title, chapters, embed_model)
    if n == 0:
        log.warning(
            f"《{title}》未写入知识库：embedding 模型不可用，请先执行：ollama pull bge-m3"
        )
    return n


def guess_region_by_ip(timeout: float = 3.0) -> str:
    """用公网 IP 粗略定位省份（失败返回空串，绝不抛异常/阻塞）"""
    import requests
    services = [
        ("https://ipapi.co/json/", ("region",)),
        ("http://ip-api.com/json/", ("regionName", "region")),
    ]
    for url, keys in services:
        try:
            data = requests.get(url, timeout=timeout).json()
            for k in keys:
                v = str(data.get(k, "") or "").strip()
                if v:
                    return v
        except Exception as e:
            log.debug(f"IP 定位失败（{url}）: {e}")
    return ""
