"""ClassIsland 课程状态数据模型"""
from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime


@dataclass
class LessonState:
    """当前课程状态（来自 ClassIsland IPC 或配置文件解析）"""
    state: str = "Unknown"      # None / OnClass / Breaking / AfterSchool / Unknown
    subject: str = ""           # 当前科目
    class_name: str = ""        # 班级名
    on_class_left_time: int = 0 # 距下课剩余秒数（上课时）
    current_time: str = ""      # ClassIsland 侧当前时间
    source: str = ""            # "ipc" / "file" / "none"
    ts: datetime = field(default_factory=datetime.now)

    @property
    def is_on_class(self) -> bool:
        return self.state == "OnClass"

    @property
    def is_breaking(self) -> bool:
        return self.state == "Breaking"

    def to_dict(self) -> dict:
        return {
            "state": self.state,
            "subject": self.subject,
            "class_name": self.class_name,
            "on_class_left_time": self.on_class_left_time,
            "current_time": self.current_time,
            "source": self.source,
            "ts": self.ts.isoformat(),
        }
