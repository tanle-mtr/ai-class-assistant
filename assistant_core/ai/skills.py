"""Markdown 技能库（SKILL.md 风格）。

skills/*.md = frontmatter(name/description) + 正文指令。
run(skill, input, context) 把正文作为 system 提示词交给 Ollama。
技能文件是数据，保留既有 10 个 md 内容不变。
"""
from __future__ import annotations

import re
from pathlib import Path

from .. import get_logger
from ..config import config
from .ollama import ollama

log = get_logger("ai.skills")

SKILLS_DIR = Path(__file__).resolve().parent.parent / "skills"


class Skill:
    def __init__(self, name: str, path: Path, description: str = "", body: str = "") -> None:
        self.name = name
        self.path = path
        self.description = description
        self.body = body

    def __repr__(self) -> str:
        return f"<Skill {self.name}>"


def _parse_md(path: Path) -> Skill:
    text = path.read_text(encoding="utf-8")
    name, description, body = path.stem, "", text
    match = re.match(r"^---\s*\n(.*?)\n---\s*\n", text, re.S)
    if match:
        meta = match.group(1)
        body = text[match.end():]
        name_match = re.search(r"^name\s*:\s*(.+)$", meta, re.M)
        desc_match = re.search(r"^description\s*:\s*(.+)$", meta, re.M)
        if name_match:
            name = name_match.group(1).strip()
        if desc_match:
            description = desc_match.group(1).strip()
    return Skill(name=name, path=path, description=description, body=body.strip())


class SkillEngine:
    def __init__(self, skills_dir: Path | None = None) -> None:
        self.skills_dir = skills_dir or SKILLS_DIR
        self._skills: dict[str, Skill] = {}
        self.reload()

    def reload(self) -> None:
        self._skills = {}
        if not self.skills_dir.exists():
            return
        for path in sorted(self.skills_dir.glob("*.md")):
            try:
                skill = _parse_md(path)
                self._skills[skill.name] = skill
            except Exception as exc:
                log.warning(f"技能解析失败 {path.name}: {exc}")
        log.info(f"技能库加载完成: {len(self._skills)} 个技能")

    def list_skills(self) -> list[dict[str, str]]:
        return [{"name": s.name, "description": s.description} for s in self._skills.values()]

    def get(self, name: str) -> Skill | None:
        return self._skills.get(name)

    def run(self, skill_name: str, user_input: str, *, context: str = "",
            model: str | None = None, temperature: float = 0.3) -> str:
        """执行技能：正文（+上下文）作为 system，任务输入作为 user。"""
        skill = self.get(skill_name)
        if skill is None:
            raise ValueError(f"技能不存在: {skill_name}")
        chosen = model or config.get("model") or ""
        if not chosen:
            raise ValueError("未配置主力模型")
        system = skill.body
        if context:
            system += "\n\n【上下文资料】\n" + context
        return ollama.chat(
            chosen,
            [{"role": "system", "content": system}, {"role": "user", "content": user_input}],
            temperature=temperature,
        )

    def build_system_prompt(self, max_chars: int = 3000) -> str:
        """拼接所有技能要点，作为 Modelfile 打包专属模型的 SYSTEM 提示词。"""
        head = (
            "你是「AI 课堂助手」的课堂专用本地模型，服务对象是一线教师：备课、授课、课后复盘。"
            "全程使用中文，回答结构化、可落地，不编造教材内容。\n\n## 内置技能与要求\n"
        )
        parts: list[str] = []
        total = len(head)
        for meta in self.list_skills():
            skill = self.get(meta["name"])
            block = [f"- {meta['name']}：{meta.get('description', '')}"]
            for line in (skill.body if skill else "").splitlines():
                item = line.strip()
                if not item or item.startswith(("#", "---", "```")):
                    continue
                if len(item) > 100:
                    item = item[:100] + "…"
                block.append("  " + item)
            text = "\n".join(block)
            if total + len(text) > max_chars:
                break
            parts.append(text)
            total += len(text)
        return (head + "\n".join(parts)).replace('"""', "'''")


skill_engine = SkillEngine()
