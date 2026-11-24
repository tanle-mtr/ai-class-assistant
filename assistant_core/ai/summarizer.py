"""课型分流总结生成。

- 新授课：老师总结 + 学生总结（重难点）+ 导学案 + 老师导学案 + 思维导图
- 评讲课：老师总结 + 学生总结 + 导学案 + 变式练习（不出思维导图）
- 考试/自习课：只有老师版课堂报告（分钟级分贝折线图 + 平均分贝）

所有 AI 调用失败时降级为本地模板，保证一定有产物、绝不 500。
"""
from __future__ import annotations

from datetime import datetime
from typing import Any

from .. import get_logger
from ..config import config
from ..events import bus
from .skills import skill_engine

log = get_logger("ai.summarizer")


def db_stats(samples: list[dict[str, Any]]) -> dict[str, Any]:
    """分贝统计：每分钟平均、峰值、安静占比、活跃分钟段。"""
    if not samples:
        return {"avg": 0.0, "peak": 0.0, "per_minute": [], "quiet_ratio": 0.0, "active_minutes": []}
    values = [float(s.get("db", 0) or 0) for s in samples if isinstance(s, dict)]
    if not values:
        return {"avg": 0.0, "peak": 0.0, "per_minute": [], "quiet_ratio": 0.0, "active_minutes": []}
    base_minute = int(samples[0].get("sec", 0)) // 60
    buckets: dict[int, list[float]] = {}
    for sample in samples:
        minute = int(sample.get("sec", 0)) // 60 - base_minute
        buckets.setdefault(minute, []).append(float(sample.get("db", 0) or 0))
    minutes = sorted(buckets)
    per_minute = [round(sum(buckets[m]) / len(buckets[m]), 1) for m in minutes]
    quiet = sum(1 for value in values if value < 40) / len(values)
    active = [m for m, value in zip(minutes, per_minute) if value > 60]
    return {
        "avg": round(sum(values) / len(values), 1),
        "peak": round(max(values), 1),
        "per_minute": per_minute,
        "quiet_ratio": round(quiet, 2),
        "active_minutes": active,
    }


def db_chart(stats: dict[str, Any]) -> str:
    values = stats.get("per_minute") or []
    if not values:
        return "（无分贝数据）"
    x = ", ".join(str(i + 1) for i in range(len(values)))
    line = ", ".join(str(v) for v in values)
    low = max(20, min(values) - 10)
    high = min(100, max(values) + 10)
    return (
        "```mermaid\nxychart-beta\n"
        '    title "每分钟平均分贝"\n'
        f"    x-axis [{x}]\n"
        f'    y-axis "分贝(dB)" {low} --> {high}\n'
        f"    line [{line}]\n```"
    )


def build_context(session: Any, screen: Any = None) -> str:
    """组装课堂上下文：科目/课型/转写/分贝/屏幕活跃度。"""
    start = session.start_time
    end = session.end_time or datetime.now()
    lines = [
        f"科目：{session.subject}",
        f"班级：{session.class_name}",
        f"时间：{start.strftime('%Y-%m-%d %H:%M')} ~ {end.strftime('%H:%M')}",
        f"课型：{'考试/自习' if session.is_exam_or_study else ('评讲课' if session.is_review else '新授课')}",
        f"拖堂：{'是' if session.overtime_detected else '否'}",
    ]
    if session.asr_segments:
        text = "".join(str(s.get("text", "")) for s in session.asr_segments)
        lines.append(f"\n【课堂语音转写】\n{text[:8000]}")
    stats = db_stats(session.db_samples)
    lines.append(
        f"\n【分贝统计】平均 {stats['avg']} dB，峰值 {stats['peak']} dB，"
        f"安静时段占比 {stats['quiet_ratio']:.0%}"
    )
    if stats["active_minutes"]:
        lines.append(f"活跃分钟段：{stats['active_minutes']}")
    if screen is not None:
        try:
            history = screen.activity_history()
            if history:
                average = sum(item[1] for item in history) / len(history)
                lines.append(
                    f"\n【屏幕活跃度】采样 {len(history)} 次，"
                    f"平均画面变化率 {average:.1%}，最近一次 {history[-1][1]:.1%}"
                )
        except Exception as exc:
            log.debug(f"屏幕活跃度读取跳过: {exc}")
    return "\n".join(lines)


class Summarizer:
    """根据 Session 生成 {类型: markdown}。"""

    def generate(self, session: Any, screen: Any = None) -> dict[str, str]:
        stats = db_stats(session.db_samples)
        context = build_context(session, screen)
        model = config.get("model") or ""

        # 电脑绝大多数时间未开机 → 仅保留监控录像
        try:
            if screen is not None and screen.coverage() < float(config.get("screen_skip_threshold", 0.3) or 0.3):
                log.info(f"[跳过] {session.subject} 屏幕采样覆盖率过低，仅归档监控")
                return {}
        except Exception:
            pass

        if session.is_exam_or_study:
            outputs = {"db": self.exam_report(session, stats)}
            bus.emit("summary.item_generated", kind="db", session=session.subject)
            return outputs

        outputs: dict[str, str] = {}
        try:
            md = self._run("classroom-summary", "请生成本课课堂总结。", context, model)
            outputs["teacher"], outputs["student"] = self._split_summary(md)
        except Exception as exc:
            log.warning(f"课堂总结生成失败，使用模板: {exc}")
            outputs["teacher"] = self.fallback_teacher(session, stats)
            outputs["student"] = self.fallback_student(session)

        if session.is_review:
            # 评讲课：导学案 + 变式练习（需求明确要导学案，且不出思维导图）
            outputs["guide"] = self._run_safe(
                "guide-plan",
                "请生成本课导学案。这是评讲/试卷讲评课，导学案应围绕本次讲评暴露的薄弱点设计。",
                context, model, self.fallback_guide(session),
            )
            outputs["review_questions"] = self._run_safe(
                "review-questions", "请基于本次讲评内容生成类似题目（变式练习）。",
                context, model, self.fallback_review_questions(session),
            )
        else:
            outputs["guide"] = self._run_safe(
                "guide-plan", "请生成本课学生导学案。", context, model, self.fallback_guide(session),
            )
            outputs["teacher_guide"] = self._run_safe(
                "teacher-guide", "请生成本课老师导学案（备课/授课参考）。",
                context, model, self.fallback_teacher_guide(session),
            )
            outputs["mindmap"] = self._run_safe(
                "mindmap", "请生成本课思维导图（Markmap 格式）。", context, model,
                f"# {session.subject} 思维导图\n\n（生成失败，可重试）",
            )

        bus.emit("summary.item_generated", kinds=list(outputs), session=session.subject)
        return outputs

    @staticmethod
    def _run(skill: str, instruction: str, context: str, model: str) -> str:
        return skill_engine.run(skill, instruction, context=context, model=model)

    @staticmethod
    def _run_safe(skill: str, instruction: str, context: str, model: str, fallback: str) -> str:
        try:
            result = Summarizer._run(skill, instruction, context, model)
            return result or fallback
        except Exception as exc:
            log.warning(f"{skill} 生成失败，使用模板: {exc}")
            return fallback

    @staticmethod
    def _split_summary(markdown: str) -> tuple[str, str]:
        index = markdown.find("学生课堂总结")
        if index > 0:
            return markdown[:index].strip(), markdown[index:].strip()
        return markdown.strip(), ""

    # ---------- 降级模板 ----------
    @staticmethod
    def fallback_teacher(session: Any, stats: dict[str, Any]) -> str:
        end = session.end_time or datetime.now()
        return (
            f"# 老师课堂总结 · {session.subject}\n\n"
            f"- 班级：{session.class_name}　时间：{session.start_time:%H:%M}~{end:%H:%M}\n"
            f"- 课型：{'考试/自习' if session.is_exam_or_study else ('评讲' if session.is_review else '新授课')}\n"
            f"- 平均分贝：{stats['avg']} dB　峰值：{stats['peak']} dB\n"
            f"- 拖堂：{'是' if session.overtime_detected else '否'}\n\n"
            "## 课堂质量\n（AI 生成失败，此为自动记录模板）\n\n"
            "## 建议细讲\n（待生成）\n\n## 建议缩短\n（待生成）\n\n## 防拖堂建议\n（待生成）\n"
        )

    @staticmethod
    def fallback_student(session: Any) -> str:
        return (
            f"# 学生课堂总结 · {session.subject}\n\n"
            f"- 班级：{session.class_name}\n\n"
            "## 本节课程总结\n（待生成）\n\n## 本节重点\n（待生成）\n\n## 本节难点\n（待生成）\n"
        )

    @staticmethod
    def fallback_guide(session: Any) -> str:
        return (
            f"# 导学案 · {session.subject}\n\n"
            "## 学习目标\n（待生成）\n\n## 课前预习\n（待生成）\n\n"
            "## 课堂要点\n（待生成）\n\n## 例题/练习\n（待生成）\n"
        )

    @staticmethod
    def fallback_teacher_guide(session: Any) -> str:
        return (
            f"# 老师导学案 · {session.subject}\n\n"
            "## 教学目标\n（待生成）\n\n## 教学重难点\n（待生成）\n\n"
            "## 教学设计建议\n（待生成）\n\n## 课堂练习设计\n（待生成）\n\n"
            "## 易错点预警\n（待生成）\n"
        )

    @staticmethod
    def fallback_review_questions(session: Any) -> str:
        return (
            f"# 讲评课变式练习 · {session.subject}\n\n"
            "## 本次讲评要点\n（待生成）\n\n## 变式练习\n（待生成）\n\n## 方法总结\n（待生成）\n"
        )

    @staticmethod
    def exam_report(session: Any, stats: dict[str, Any]) -> str:
        end = session.end_time or datetime.now()
        duration = max(1, int((end - session.start_time).total_seconds() // 60))
        active = ("分钟段 " + ", ".join(map(str, stats["active_minutes"]))) if stats["active_minutes"] else "无"
        return "\n".join([
            f"# 考试/自习课课堂报告 · {session.subject}",
            "",
            f"- 班级：{session.class_name}",
            f"- 时间：{session.start_time:%Y-%m-%d %H:%M} ~ {end:%H:%M}",
            f"- 时长：约 {duration} 分钟",
            "",
            "## 分贝折线统计图（每分钟）",
            "",
            db_chart(stats),
            "",
            "## 统计指标",
            "",
            f"- 平均分贝：**{stats['avg']} dB**",
            f"- 峰值分贝：**{stats['peak']} dB**",
            f"- 安静时段占比（<40dB）：**{stats['quiet_ratio']:.0%}**",
            f"- 活跃时段（>60dB）：{active}",
            "",
            "## 纪律评估",
            "",
            "课堂安静程度符合考试/自习要求。" if stats["avg"] < 45 else "课堂整体偏活跃，建议关注纪律。",
            "",
        ])
