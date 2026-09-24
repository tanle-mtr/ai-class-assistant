"""Markdown 技能库引擎（md 编写 skill 机制）
- skills 目录下每个 .md 文件是一个技能：frontmatter(name/description) + 正文指令
- run(skill_name, context) → 组装 system+user → Ollama 生成
"""
from __future__ import annotations

import re
from pathlib import Path

from ..config import config
from ..logger import get_logger
from .ollama_client import ollama

log = get_logger("ai.skills")

SKILLS_DIR = Path(__file__).resolve().parent.parent / "skills"


class Skill:
    def __init__(self, name: str, path: Path, description: str = "", body: str = ""):
        self.name = name
        self.path = path
        self.description = description
        self.body = body

    def __repr__(self):
        return f"<Skill {self.name}>"


def _parse_md(path: Path) -> Skill:
    text = path.read_text(encoding="utf-8")
    name = path.stem
    desc = ""
    body = text
    # frontmatter: ---\nname: xxx\ndescription: xxx\n---
    m = re.match(r"^---\s*\n(.*?)\n---\s*\n", text, re.S)
    if m:
        meta = m.group(1)
        body = text[m.end():]
        nm = re.search(r"^name\s*:\s*(.+)$", meta, re.M)
        ds = re.search(r"^description\s*:\s*(.+)$", meta, re.M)
        if nm:
            name = nm.group(1).strip()
        if ds:
            desc = ds.group(1).strip()
    return Skill(name=name, path=path, description=desc, body=body.strip())


class SkillEngine:
    def __init__(self, skills_dir: Path = SKILLS_DIR):
        self.skills_dir = skills_dir
        self._skills: dict[str, Skill] = {}
        self.reload()

    def reload(self):
        self._skills = {}
        if not self.skills_dir.exists():
            return
        for f in sorted(self.skills_dir.glob("*.md")):
            try:
                s = _parse_md(f)
                self._skills[s.name] = s
            except Exception as e:
                log.warning(f"技能解析失败 {f.name}: {e}")
        log.info(f"技能库加载完成: {len(self._skills)} 个技能")

    def list_skills(self) -> list[dict]:
        return [{"name": s.name, "description": s.description} for s in self._skills.values()]

    def get(self, name: str) -> Skill | None:
        return self._skills.get(name)

    def run(self, skill_name: str, user_input: str, context: str = "",
            model: str | None = None, temperature: float = 0.3) -> str:
        """执行技能：skill 正文作为 system，任务输入作为 user"""
        skill = self.get(skill_name)
        if skill is None:
            raise ValueError(f"技能不存在: {skill_name}，可用: {list(self._skills)}")
        m = model or config.get("model") or ""
        if not m:
            raise ValueError("未配置主力模型")
        system = skill.body
        if context:
            system += "\n\n【上下文资料】\n" + context
        messages = [
            {"role": "system", "content": system},
            {"role": "user", "content": user_input},
        ]
        return ollama.chat(model=m, messages=messages, temperature=temperature)


skill_engine = SkillEngine()
