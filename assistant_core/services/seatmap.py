"""座位表：图片 OCR 初始化、人脸观察驱动的换座检测、Markdown 导出。"""
from __future__ import annotations

import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any

from .. import get_logger
from ..ai.skills import skill_engine
from ..ai.vision import vision
from ..config import config

log = get_logger("services.seatmap")

SEATMAP_FILE = "seatmap.json"


class SeatMap:
    def __init__(self) -> None:
        self._dir = config.dir("seats")
        self._path = self._dir / SEATMAP_FILE
        self._data: dict[str, Any] = {
            "class_name": "", "rows": 0, "cols": 0, "updated": "",
            "seats": [], "unresolved": [],
        }
        self.load()

    # ---------- 持久化 ----------
    def load(self) -> None:
        if not self._path.exists():
            return
        try:
            data = json.loads(self._path.read_text(encoding="utf-8"))
            if isinstance(data, dict):
                self._data = data
        except Exception as exc:
            log.warning(f"座位表读取失败（使用空表）: {exc}")

    def save(self) -> None:
        try:
            self._data["updated"] = datetime.now().strftime("%Y-%m-%d %H:%M")
            self._path.write_text(
                json.dumps(self._data, ensure_ascii=False, indent=2), encoding="utf-8",
            )
        except Exception as exc:
            log.warning(f"座位表保存失败: {exc}")

    # ---------- 初始化（OCR + 技能解析） ----------
    def init_from_image(self, image_path: str, class_name: str = "") -> dict[str, Any]:
        text = vision.ocr(image_path)
        if text.startswith("[识图"):
            return {"ok": False, "seats": 0, "msg": text}
        try:
            raw = skill_engine.run(
                "seatmap-parse", f"班级：{class_name}\n图片OCR结果：\n{text}",
            )
            start, end = raw.find("{"), raw.rfind("}")
            if start < 0 or end < 0:
                return {"ok": False, "seats": 0, "msg": "技能未返回 JSON"}
            parsed = json.loads(raw[start:end + 1])
            self._data = {
                "class_name": parsed.get("class_name", class_name),
                "rows": int(parsed.get("rows", 0) or 0),
                "cols": int(parsed.get("cols", 0) or 0),
                "updated": datetime.now().strftime("%Y-%m-%d %H:%M"),
                "seats": parsed.get("seats", []),
                "unresolved": parsed.get("unresolved", []),
            }
            self.save()
            return {"ok": True, "seats": len(self._data["seats"])}
        except Exception as exc:
            log.warning(f"座位表初始化失败: {exc}")
            return {"ok": False, "seats": 0, "msg": str(exc)}

    # ---------- 查询 ----------
    def name_at(self, row: int, col: int) -> str:
        for seat in self._data.get("seats", []):
            if seat.get("row") == row and seat.get("col") == col:
                return str(seat.get("name", ""))
        return ""

    def seat_of(self, name: str) -> tuple[int, int] | None:
        for seat in self._data.get("seats", []):
            if seat.get("name") == name:
                return int(seat.get("row", 0)), int(seat.get("col", 0))
        return None

    # ---------- 换座检测（上课中 + 下课时调用） ----------
    def update_from_observations(self, observations: list[dict[str, Any]]) -> list[dict[str, Any]]:
        """同一姓名在新座位出现 >=2 次则判定换座并写回座位表。"""
        changes: list[dict[str, Any]] = []
        if not self._data.get("seats") or not observations:
            return changes
        counts: dict[tuple[str, str], int] = {}
        for observation in observations:
            name, seat = observation.get("name", ""), observation.get("seat", "")
            if not name or not seat:
                continue
            key = (str(name), str(seat))
            counts[key] = counts.get(key, 0) + 1
        for (name, seat), count in counts.items():
            if count < 2:
                continue
            old = self.seat_of(name)
            if old is None:
                continue
            row, col = self.parse_seat(seat)
            if row and (row, col) != old:
                for item in self._data["seats"]:
                    if item.get("name") == name:
                        item["row"], item["col"] = row, col
                changes.append({"name": name, "from": old, "to": (row, col)})
        if changes:
            self.save()
            log.info(f"检测到换座 {len(changes)} 人: {changes}")
        return changes

    @staticmethod
    def parse_seat(seat: str) -> tuple[int | None, int | None]:
        match = re.search(r"r(\d+)c(\d+)", str(seat))
        if match:
            return int(match.group(1)), int(match.group(2))
        match = re.search(r"第(\d+)排第(\d+)列", str(seat))
        if match:
            return int(match.group(1)), int(match.group(2))
        return None, None

    # ---------- 契约 /api/seatmap 响应 ----------
    def to_api_dict(self, face_registry: Any = None) -> dict[str, Any]:
        """补齐 id / face_id 字段，兼容 v1 只有 row/col/name 的旧数据。"""
        faces: dict[str, str] = {}
        if face_registry is not None:
            try:
                for face_id, info in face_registry.list_faces().items():
                    if info.get("name"):
                        faces[str(info["name"])] = face_id
            except Exception:
                pass
        seats: list[dict[str, Any]] = []
        for index, seat in enumerate(self._data.get("seats", []), 1):
            name = str(seat.get("name", ""))
            seats.append({
                "id": str(seat.get("id") or f"s{index:03d}"),
                "row": int(seat.get("row", 0) or 0),
                "col": int(seat.get("col", 0) or 0),
                "name": name,
                "face_id": str(seat.get("face_id") or faces.get(name, "")),
            })
        return {"class_name": str(self._data.get("class_name", "")), "seats": seats}

    # ---------- 导出 ----------
    def export_markdown(self) -> str:
        seats = self._data.get("seats", [])
        if not seats:
            return "# 座位表\n\n（尚未初始化，请上传座位表图片）"
        rows = int(self._data.get("rows", 0)) or max((int(s.get("row", 1)) for s in seats), default=0)
        cols = int(self._data.get("cols", 0)) or max((int(s.get("col", 1)) for s in seats), default=0)
        grid = [["" for _ in range(cols)] for _ in range(rows)]
        for seat in seats:
            row, col = int(seat.get("row", 1)), int(seat.get("col", 1))
            if 1 <= row <= rows and 1 <= col <= cols:
                grid[row - 1][col - 1] = str(seat.get("name", ""))
        lines = [
            f"# 座位表 · {self._data.get('class_name', '')}",
            f"\n> 更新于 {self._data.get('updated', '')}\n",
            "|" + "|".join(str(i + 1) for i in range(cols)) + "|",
            "|" + "|".join("---" for _ in range(cols)) + "|",
        ]
        lines.extend("|" + "|".join(cell or "　" for cell in row) + "|" for row in grid)
        if self._data.get("unresolved"):
            lines.append("\n## 待确认名单\n" + "、".join(self._data["unresolved"]))
        return "\n".join(lines)

    def export(self) -> Path:
        path = self._dir / "座位表.md"
        path.write_text(self.export_markdown(), encoding="utf-8")
        return path


seatmap = SeatMap()
