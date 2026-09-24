"""课堂总结流水线：按课型生成 md 产物（老师总结/学生总结/导学案/思维导图/分贝报告）"""
from __future__ import annotations

from datetime import datetime

from ..config import config
from ..logger import get_logger
from .skill_engine import skill_engine
from ..events import bus

log = get_logger("ai.summarizer")


def _db_stats(samples: list[dict]) -> dict:
    """分贝统计：每分钟平均（相对课堂第 N 分钟）、峰值、安静/活跃占比"""
    if not samples:
        return {"avg": 0, "peak": 0, "per_minute": [], "quiet_ratio": 0, "active_minutes": []}
    dbs = [s["db"] for s in samples]
    base_minute = samples[0]["sec"] // 60
    per_minute: dict[int, list[float]] = {}
    for s in samples:
        rel = (s["sec"] // 60) - base_minute
        per_minute.setdefault(rel, []).append(s["db"])
    mins = sorted(per_minute)
    per_minute_list = [round(sum(per_minute[m]) / len(per_minute[m]), 1) for m in mins]
    quiet = sum(1 for x in dbs if x < 40) / len(dbs)
    active = [m for m, v in zip(mins, per_minute_list) if v > 60]
    return {
        "avg": round(sum(dbs) / len(dbs), 1),
        "peak": round(max(dbs), 1),
        "per_minute": per_minute_list,
        "quiet_ratio": round(quiet, 2),
        "active_minutes": active,
    }


def _mermaid_db_chart(stats: dict) -> str:
    vals = stats["per_minute"]
    if not vals:
        return "（无分贝数据）"
    x = ", ".join(str(i + 1) for i in range(len(vals)))
    line = ", ".join(str(v) for v in vals)
    lo = max(20, min(vals) - 10)
    hi = min(100, max(vals) + 10)
    return (
        "```mermaid\n"
        "xychart-beta\n"
        '    title "每分钟平均分贝"\n'
        f"    x-axis [{x}]\n"
        f'    y-axis "分贝(dB)" {lo} --> {hi}\n'
        f"    line [{line}]\n"
        "```"
    )


def _build_context(session, screen=None) -> str:
    """组装课堂素材上下文"""
    lines = [
        f"科目：{session.subject}",
        f"班级：{session.class_name}",
        f"时间：{session.start_time.strftime('%Y-%m-%d %H:%M')} ~ "
        f"{(session.end_time or datetime.now()).strftime('%H:%M')}",
        f"课型：{'考试/自习' if session.is_exam_or_study else ('评讲课' if session.is_review else '新授课')}",
        f"拖堂：{'是' if session.overtime_detected else '否'}",
    ]
    if session.asr_segments:
        text = "".join(s["text"] for s in session.asr_segments)
        lines.append(f"\n【课堂语音转写】\n{text[:8000]}")
    stats = _db_stats(session.db_samples)
    lines.append(
        f"\n【分贝统计】平均 {stats['avg']} dB，峰值 {stats['peak']} dB，"
        f"安静时段占比 {stats['quiet_ratio']:.0%}"
    )
    if stats["active_minutes"]:
        lines.append(f"活跃分钟段：{stats['active_minutes']}")
    if screen is not None:
        try:
            hist = screen.activity_history()
            if hist:
                avg_activity = sum(d for _, d in hist) / len(hist)
                last_activity = hist[-1][1]
                lines.append(
                    f"\n【屏幕活跃度】采样 {len(hist)} 次，平均画面变化率 {avg_activity:.1%}，"
                    f"最近一次 {last_activity:.1%}"
                )
        except Exception as e:
            log.debug(f"屏幕活跃度读取失败（忽略）: {e}")
    return "\n".join(lines)


class Summarizer:
    """下课产物生成器"""

    def generate(self, session, screen=None) -> dict[str, str]:
        """返回 {类型: markdown 文本}，写入导出由 Exporter 负责。
        若本节无学习内容（看电影等）或电脑绝大多数时间未开机，返回 {}（只保留监控录像）。"""
        stats = _db_stats(session.db_samples)
        context = _build_context(session, screen)
        outputs: dict[str, str] = {}
        model = config.get("model") or ""

        # ---- 跳过检测 1：电脑绝大多数时间未开机（屏幕采样覆盖率过低）----
        try:
            if screen is not None:
                coverage = screen.coverage()
                threshold = config.get("screen_skip_threshold", 0.3)
                if coverage < threshold:
                    log.info(f"[跳过] {session.subject} 屏幕采样覆盖率 {coverage:.0%} < {threshold:.0%}，"
                             "电脑绝大多数时间未开机，仅保留监控录像")
                    return {}
        except Exception as e:
            log.debug(f"屏幕覆盖率检测异常（不拦截）: {e}")

        try:
            if session.is_exam_or_study:
                # 考试/自习：只出老师版课堂报告（分贝图表确定性生成）
                outputs["db"] = self._exam_report(session, stats)
                bus.emit("summary.item_generated", kind="db", session=session.subject)
                return outputs

            # ---- 跳过检测 2：无学习内容（看电影/自由活动/放空等）----
            try:
                verdict = skill_engine.run("lesson-content-check",
                                           "请判断本节课是否包含实际学习内容。", context=context, model=model)
                if "EMPTY" in verdict.upper():
                    log.info(f"[跳过] {session.subject} 判定为无学习内容课程（看电影/自由活动等），仅保留监控录像")
                    return {}
            except Exception as e:
                log.warning(f"课程内容判定失败（不拦截，正常生成）: {e}")

            # 课堂总结（老师+学生）——失败则降级模板
            try:
                md = skill_engine.run("classroom-summary", "请生成本课课堂总结。", context=context, model=model)
                outputs["teacher"], outputs["student"] = self._split_summary(md)
            except Exception as e:
                log.warning(f"课堂总结生成失败，使用模板: {e}")
                outputs["teacher"] = self._fallback_teacher(session, stats)
                outputs["student"] = self._fallback_student(session)

            if session.is_review:
                # 评讲课：导学案（围绕讲评暴露的薄弱点）+ 变式练习，不出思维导图
                try:
                    outputs["guide"] = skill_engine.run(
                        "guide-plan",
                        "请生成本课导学案。这是评讲/试卷讲评课，导学案应围绕本次讲评暴露的薄弱点设计。",
                        context=context, model=model)
                except Exception as e:
                    log.warning(f"讲评课导学案生成失败: {e}")
                    outputs["guide"] = self._fallback_guide(session)
                try:
                    outputs["review_questions"] = skill_engine.run(
                        "review-questions", "请基于本次讲评内容生成类似题目（变式练习）。",
                        context=context, model=model)
                except Exception as e:
                    log.warning(f"讲评变式练习生成失败: {e}")
                    outputs["review_questions"] = self._fallback_review_questions(session)
            else:
                # 新授课：学生导学案 + 老师导学案 + 思维导图
                try:
                    outputs["guide"] = skill_engine.run("guide-plan", "请生成本课学生导学案。", context=context, model=model)
                except Exception as e:
                    log.warning(f"学生导学案生成失败: {e}")
                    outputs["guide"] = self._fallback_guide(session)
                try:
                    outputs["teacher_guide"] = skill_engine.run(
                        "teacher-guide", "请生成本课老师导学案（备课/授课参考）。", context=context, model=model)
                except Exception as e:
                    log.warning(f"老师导学案生成失败: {e}")
                    outputs["teacher_guide"] = self._fallback_teacher_guide(session)
                try:
                    outputs["mindmap"] = skill_engine.run("mindmap", "请生成本课思维导图（Markmap）。", context=context, model=model)
                except Exception as e:
                    log.warning(f"思维导图生成失败: {e}")
                    outputs["mindmap"] = f"# {session.subject} 思维导图\n\n（生成失败，可重试）"
        except Exception as e:
            log.exception(f"总结流水线异常: {e}")

        bus.emit("summary.item_generated", kinds=list(outputs), session=session.subject)
        return outputs

    # ---------- 拆分总结 ----------
    @staticmethod
    def _split_summary(md: str) -> tuple[str, str]:
        """把 classroom-summary 输出拆成老师/学生两份；拆不开则整体归老师"""
        idx = md.find("学生课堂总结")
        if idx > 0:
            return md[:idx].strip(), md[idx:].strip()
        return md.strip(), ""

    # ---------- 降级模板 ----------
    @staticmethod
    def _fallback_teacher(session, stats) -> str:
        return (
            f"# 老师课堂总结 · {session.subject}\n\n"
            f"- 班级：{session.class_name}　时间：{session.start_time:%H:%M}~{(session.end_time or datetime.now()):%H:%M}\n"
            f"- 课型：{'考试/自习' if session.is_exam_or_study else ('评讲' if session.is_review else '新授课')}\n"
            f"- 平均分贝：{stats['avg']} dB　峰值：{stats['peak']} dB\n"
            f"- 拖堂：{'是' if session.overtime_detected else '否'}\n\n"
            "## 课堂质量\n（AI 总结生成失败，此为自动记录模板，请稍后重试生成）\n\n"
            "## 建议细讲\n（待生成）\n\n## 建议缩短\n（待生成）\n\n## 防拖堂建议\n（待生成）\n"
        )

    @staticmethod
    def _fallback_student(session) -> str:
        return (
            f"# 学生课堂总结 · {session.subject}\n\n"
            f"- 班级：{session.class_name}\n\n"
            "## 本节课程总结\n（待生成）\n\n## 本节重点\n（待生成）\n\n## 本节难点\n（待生成）\n"
        )

    @staticmethod
    def _fallback_guide(session) -> str:
        return (
            f"# 导学案 · {session.subject}\n\n"
            "## 学习目标\n（待生成）\n\n## 课前预习\n（待生成）\n\n"
            "## 课堂要点\n（待生成）\n\n## 例题/练习\n（待生成）\n"
        )

    @staticmethod
    def _fallback_teacher_guide(session) -> str:
        return (
            f"# 老师导学案 · {session.subject}\n\n"
            "## 教学目标\n（待生成）\n\n## 教学重难点\n（待生成）\n\n"
            "## 教学设计建议\n（待生成）\n\n## 课堂练习设计\n（待生成）\n\n"
            "## 易错点预警\n（待生成）\n"
        )

    @staticmethod
    def _fallback_review_questions(session) -> str:
        return (
            f"# 讲评课变式练习 · {session.subject}\n\n"
            "## 本次讲评要点\n（待生成）\n\n## 变式练习\n（待生成）\n\n"
            "## 方法总结\n（待生成）\n"
        )

    # ---------- 考试/自习报告 ----------
    @staticmethod
    def _exam_report(session, stats: dict) -> str:
        chart = _mermaid_db_chart(stats)
        end = session.end_time or datetime.now()
        duration_min = max(1, int((end - session.start_time).total_seconds() // 60))
        lines = [
            f"# 考试/自习课课堂报告 · {session.subject}",
            "",
            f"- 班级：{session.class_name}",
            f"- 时间：{session.start_time:%Y-%m-%d %H:%M} ~ {end:%H:%M}",
            f"- 时长：约 {duration_min} 分钟",
            "",
            "## 分贝折线统计图（每分钟）",
            "",
            chart,
            "",
            "## 统计指标",
            "",
            f"- 平均分贝：**{stats['avg']} dB**",
            f"- 峰值分贝：**{stats['peak']} dB**",
            f"- 安静时段占比（<40dB）：**{stats['quiet_ratio']:.0%}**",
            f"- 活跃时段（>60dB）：{('分钟段 ' + ', '.join(map(str, stats['active_minutes']))) if stats['active_minutes'] else '无'}",
            "",
            "## 纪律评估",
            "",
            ("课堂安静程度符合考试/自习要求。" if stats["avg"] < 45
             else "课堂整体偏活跃，建议关注纪律。"),
            "",
        ]
        return "\n".join(lines)
