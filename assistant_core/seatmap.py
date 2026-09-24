"""座位表管理
- 上传图片初始化：image-ocr 识图 → seatmap-parse 技能 → 结构化 JSON
- 换座自动更新：上课时摄像头人脸建档 + 座位关联 → 检测名字出现在新座位 → 更新
- 可导出：JSON + Markdown 表格
"""
from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

from .config import config
from .logger import get_logger
from .ai.vision import vision
from .ai.skill_engine import skill_engine

log = get_logger("seatmap")

SEATMAP_FILE = "seatmap.json"


class SeatMap:
    def __init__(self):
        self._dir = config.dir("seats")
        self._dir.mkdir(parents=True, exist_ok=True)
        self._path = self._dir / SEATMAP_FILE
        self._data: dict = {"class_name": "", "rows": 0, "cols": 0, "updated": "", "seats": [], "unresolved": []}
        self.load()

    def load(self):
        if self._path.exists():
            try:
                self._data = json.loads(self._path.read_text(encoding="utf-8"))
            except Exception as e:
                log.warning(f"座位表读取失败: {e}")

    def save(self):
        self._data["updated"] = datetime.now().strftime("%Y-%m-%d %H:%M")
        self._path.write_text(json.dumps(self._data, ensure_ascii=False, indent=2), encoding="utf-8")

    # ---------- 初始化 ----------
    def init_from_image(self, image_path: str, class_name: str = "") -> dict:
        """图片 → OCR → 技能解析 → 座位表"""
        ocr_text = vision.ocr(image_path)
        if ocr_text.startswith("[识图"):
            return {"ok": False, "msg": ocr_text}
        try:
            raw = skill_engine.run(
                "seatmap-parse",
                f"班级：{class_name}\n图片OCR结果：\n{ocr_text}",
            )
            # 提取 JSON
            start, end = raw.find("{"), raw.rfind("}")
            if start == -1 or end == -1:
                return {"ok": False, "msg": "技能未返回 JSON"}
            parsed = json.loads(raw[start:end + 1])
            self._data = {
                "class_name": parsed.get("class_name", class_name),
                "rows": int(parsed.get("rows", 0)),
                "cols": int(parsed.get("cols", 0)),
                "updated": datetime.now().strftime("%Y-%m-%d %H:%M"),
                "seats": parsed.get("seats", []),
                "unresolved": parsed.get("unresolved", []),
            }
            self.save()
            log.info(f"座位表初始化成功: {self._data['class_name']} "
                     f"{len(self._data['seats'])} 个座位，{len(self._data['unresolved'])} 个待确认")
            return {"ok": True, "seats": len(self._data["seats"]),
                    "unresolved": self._data["unresolved"]}
        except Exception as e:
            log.exception(f"座位表初始化失败: {e}")
            return {"ok": False, "msg": str(e)}

    # ---------- 查询 ----------
    def name_at(self, row: int, col: int) -> str:
        for s in self._data["seats"]:
            if s.get("row") == row and s.get("col") == col:
                return s.get("name", "")
        return ""

    def seat_of(self, name: str) -> tuple[int, int] | None:
        for s in self._data["seats"]:
            if s.get("name") == name:
                return s.get("row"), s.get("col")
        return None

    # ---------- 换座自动更新 ----------
    def update_from_observations(self, observations: list[dict]) -> list[dict]:
        """根据人脸观察结果更新座位：{face_id, name, seat}
        同一人在新座位出现 >= 2 次 → 判定换座并更新"""
        changes = []
        if not self._data["seats"]:
            return changes
        counts: dict[tuple[str, str], int] = {}
        for obs in observations:
            name = obs.get("name", "")
            seat = obs.get("seat", "")
            if not name or not seat:
                continue
            key = (name, seat)
            counts[key] = counts.get(key, 0) + 1
        for (name, seat), c in counts.items():
            if c < 2:
                continue
            old = self.seat_of(name)
            if old is None:
                continue
            new_row, new_col = self._parse_seat(seat)
            if new_row and (new_row, new_col) != old:
                for s in self._data["seats"]:
                    if s.get("name") == name:
                        s["row"], s["col"] = new_row, new_col
                changes.append({"name": name, "from": old, "to": (new_row, new_col)})
        if changes:
            self.save()
            log.info(f"检测到换座 {len(changes)} 人: {changes}")
        return changes

    @staticmethod
    def _parse_seat(seat: str) -> tuple[int | None, int | None]:
        """'r2c3' -> (2,3)；'第2排第3列' -> (2,3)"""
        import re
        m = re.search(r"r(\d+)c(\d+)", seat)
        if m:
            return int(m.group(1)), int(m.group(2))
        m = re.search(r"第(\d+)排第(\d+)列", seat)
        if m:
            return int(m.group(1)), int(m.group(2))
        return None, None

    # ---------- 导出 ----------
    def export_markdown(self) -> str:
        seats = self._data.get("seats", [])
        if not seats:
            return "# 座位表\n\n（尚未初始化，请上传座位表图片）"
        rows = self._data.get("rows", max((s["row"] for s in seats), default=0))
        cols = self._data.get("cols", max((s["col"] for s in seats), default=0))
        grid = [["" for _ in range(cols)] for _ in range(rows)]
        for s in seats:
            r, c = s.get("row", 1), s.get("col", 1)
            if 1 <= r <= rows and 1 <= c <= cols:
                grid[r - 1][c - 1] = s.get("name", "")
        lines = [f"# 座位表 · {self._data.get('class_name', '')}",
                 f"\n> 更新于 {self._data.get('updated', '')}\n"]
        lines.append("|" + "|".join(str(i + 1) for i in range(cols)) + "|")
        lines.append("|" + "|".join("---" for _ in range(cols)) + "|")
        for row in grid:
            lines.append("|" + "|".join(c or "　" for c in row) + "|")
        if self._data.get("unresolved"):
            lines.append("\n## 待确认名单\n" + "、".join(self._data["unresolved"]))
        return "\n".join(lines)

    def export(self) -> Path:
        md_path = self._dir / "座位表.md"
        md_path.write_text(self.export_markdown(), encoding="utf-8")
        return md_path


seatmap = SeatMap()
