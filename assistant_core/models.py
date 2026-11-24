"""数据模型：Session（课堂会话）与 LessonState（ClassIsland 课程状态）。"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any

EXAM_KEYWORDS = ("考试", "自习", "测验", "月考", "期中", "期末", "模拟考", "周测")
REVIEW_KEYWORDS = ("评讲", "讲评", "作业讲评", "试卷", "习题课", "订正", "试卷分析")


def classify_subject(subject: str) -> tuple[bool, bool]:
    """按科目名判定 (是否考试/自习课, 是否评讲课)。"""
    text = subject or ""
    return (
        any(keyword in text for keyword in EXAM_KEYWORDS),
        any(keyword in text for keyword in REVIEW_KEYWORDS),
    )


def lesson_type_of(subject: str) -> str:
    is_exam, is_review = classify_subject(subject)
    return "exam" if is_exam else ("review" if is_review else "new")


@dataclass
class LessonState:
    """当前课程状态（来自 ClassIsland IPC 桥或配置文件解析）。"""
    state: str = "Unknown"          # None / OnClass / Breaking / AfterSchool / Unknown
    subject: str = ""
    class_name: str = ""
    on_class_left_time: int = 0     # 距下课剩余秒数
    current_time: str = ""
    source: str = ""                # ipc / file / none
    ts: datetime = field(default_factory=datetime.now)
    lesson: dict[str, Any] = field(default_factory=dict)  # 完整课表项（契约 classisland.state.lesson）

    @property
    def is_on_class(self) -> bool:
        return self.state == "OnClass"

    @property
    def is_breaking(self) -> bool:
        return self.state == "Breaking"

    def to_dict(self) -> dict[str, Any]:
        return {
            "state": self.state,
            "subject": self.subject,
            "class_name": self.class_name,
            "on_class_left_time": self.on_class_left_time,
            "current_time": self.current_time,
            "source": self.source,
            "ts": self.ts.isoformat(),
            "lesson": self.lesson,
        }


@dataclass
class Session:
    """一节课程会话：感知样本、产出文件与总结产物。"""
    subject: str = ""
    class_name: str = ""
    start_time: datetime = field(default_factory=datetime.now)
    end_time: datetime | None = None
    is_exam_or_study: bool = False      # 考试 / 自习课
    is_review: bool = False             # 评讲 / 试卷讲评课
    overtime_detected: bool = False
    overtime_at: datetime | None = None

    db_samples: list[dict[str, Any]] = field(default_factory=list)       # [{sec:int, db:float}]
    asr_segments: list[dict[str, Any]] = field(default_factory=list)     # [{ts/start:int, text:str}]
    camera_segments: list[Path] = field(default_factory=list)            # AVI 监控切片
    audio_file: Path | None = None                                       # WAV 课堂录音
    outputs: dict[str, str] = field(default_factory=dict)                # {类型: markdown}
    notes: list[str] = field(default_factory=list)

    def elapsed(self) -> int:
        """会话已持续秒数。"""
        end = self.end_time or datetime.now()
        return max(0, int((end - self.start_time).total_seconds()))

    @property
    def lesson_type(self) -> str:
        """课型：exam / review / new。"""
        if self.is_exam_or_study:
            return "exam"
        if self.is_review:
            return "review"
        return "new"

    def to_dict(self) -> dict[str, Any]:
        """供 /api/state 与事件负载使用的精简字典。"""
        return {
            "subject": self.subject,
            "class_name": self.class_name,
            "type": self.lesson_type,
            "start": self.start_time.isoformat(),
            "end": self.end_time.isoformat() if self.end_time else "",
            "is_exam_or_study": self.is_exam_or_study,
            "is_review": self.is_review,
            "overtime": self.overtime_detected,
            "duration_min": round(self.elapsed() / 60, 1),
            "samples": len(self.db_samples),
        }
