"""人脸底库：Haar 检测 + dHash 指纹建档 / 匹配 / 座位记录。

所有图像操作都对失败做兜底：无摄像头 / cv2 不可用 / 索引损坏时不影响主流程。
"""
from __future__ import annotations

import json
import threading
from pathlib import Path
from typing import Any

from .. import get_logger
from ..config import config

log = get_logger("perception.faces")

try:
    import cv2
    import numpy as np
except Exception as exc:  # pragma: no cover - 依赖缺失时降级
    cv2 = None  # type: ignore[assignment]
    np = None  # type: ignore[assignment]
    log.warning(f"人脸库依赖不可用（cv2/numpy）: {exc}")

INDEX_FILE = "faces_index.json"


def dhash(image: Any, size: int = 64) -> str:
    """计算 64 位感知哈希（dHash）。失败返回全 0，绝不抛异常。"""
    if cv2 is None or np is None or image is None:
        return "0" * size
    try:
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        small = cv2.resize(gray, (9, 8), interpolation=cv2.INTER_AREA)
        return "".join("1" if small[y, x] > small[y, x + 1] else "0"
                       for y in range(8) for x in range(8))
    except Exception:
        return "0" * size


def hamming(a: str, b: str) -> int:
    return sum(x != y for x, y in zip(a, b))


class FaceRegistry:
    """学生人脸底库：detect_faces / observe / rename / list_faces。"""

    def __init__(self) -> None:
        self._dir = config.dir("faces")
        self._index_path = self._dir / INDEX_FILE
        self._faces: dict[str, dict[str, Any]] = {}
        self._cascade: Any = None
        self._lock = threading.RLock()
        self._load()

    # ---------- 持久化 ----------
    def _load(self) -> None:
        if not self._index_path.exists():
            return
        try:
            loaded = json.loads(self._index_path.read_text(encoding="utf-8"))
            if isinstance(loaded, dict):
                self._faces = loaded
        except Exception as exc:
            log.warning(f"人脸索引读取失败（忽略）: {exc}")

    def save(self) -> None:
        try:
            self._index_path.write_text(
                json.dumps(self._faces, ensure_ascii=False, indent=2), encoding="utf-8",
            )
        except Exception as exc:
            log.warning(f"人脸索引保存失败: {exc}")

    def _ensure_cascade(self) -> Any:
        if self._cascade is None and cv2 is not None:
            path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
            cascade = cv2.CascadeClassifier(path)
            self._cascade = cascade if not cascade.empty() else None
        return self._cascade

    # ---------- 检测 / 建档 ----------
    def detect_faces(self, frame: Any) -> list[tuple[int, int, int, int]]:
        """返回人脸框 [(x, y, w, h)]；不可用时返回空列表。"""
        cascade = self._ensure_cascade()
        if cascade is None or frame is None or cv2 is None:
            return []
        try:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            gray = cv2.equalizeHist(gray)
            faces = cascade.detectMultiScale(
                gray, scaleFactor=1.1, minNeighbors=5, minSize=(40, 40),
            )
            return [(int(x), int(y), int(w), int(h)) for x, y, w, h in faces]
        except Exception as exc:
            log.debug(f"人脸检测失败: {exc}")
            return []

    def observe(self, frame: Any, seat_label: str = "", auto_name: str = "") -> list[dict[str, Any]]:
        """处理一帧：检测人脸 → 匹配或建档 → 返回观察记录。

        新面孔自动保存到 data/faces/<id>.jpg，避免重复建档。
        """
        results: list[dict[str, Any]] = []
        faces = self.detect_faces(frame)
        for x, y, w, h in faces:
            crop = frame[max(0, y - 10): y + h + 10, max(0, x - 10): x + w + 10]
            if crop is None or getattr(crop, "size", 0) == 0:
                continue
            fingerprint = dhash(crop)
            with self._lock:
                face_id = self._match(fingerprint)
                if face_id is None:
                    number = len(self._faces) + 1
                    while f"f{number:04d}" in self._faces:
                        number += 1
                    face_id = f"f{number:04d}"
                    self._faces[face_id] = {
                        "name": auto_name or f"学生{number}",
                        "hash": fingerprint,
                        "file": f"{face_id}.jpg",
                        "seats": [],
                    }
                    try:
                        cv2.imwrite(str(self._dir / f"{face_id}.jpg"), crop)
                    except Exception:
                        pass
                    log.info(f"[建档] 新面孔 {face_id} ({self._faces[face_id]['name']})")
                if seat_label:
                    seats = self._faces[face_id].setdefault("seats", [])
                    if seat_label not in seats:
                        seats.append(seat_label)
                name = self._faces[face_id].get("name", "")
                self.save()
            results.append({
                "face_id": face_id,
                "name": name,
                "box": [x, y, w, h],
                "seat": seat_label,
            })
        return results

    def _match(self, fingerprint: str, threshold: int = 8) -> str | None:
        best_id, best_distance = None, 999
        for face_id, info in self._faces.items():
            distance = hamming(fingerprint, str(info.get("hash", "")))
            if distance < best_distance:
                best_id, best_distance = face_id, distance
        return best_id if best_distance <= threshold else None

    def rename(self, face_id: str, name: str) -> None:
        with self._lock:
            if face_id in self._faces:
                self._faces[face_id]["name"] = name
                self.save()

    def list_faces(self) -> dict[str, dict[str, Any]]:
        with self._lock:
            return {
                key: {"name": val.get("name"), "file": val.get("file"), "seats": val.get("seats", [])}
                for key, val in self._faces.items()
            }
