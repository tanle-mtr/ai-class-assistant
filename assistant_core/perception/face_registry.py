"""人脸建档与识别（轻量版）
- OpenCV Haar 人脸检测（内置模型，无需下载）
- dHash 指纹用于去重与匹配（近似，支持用户手动修正）
- 首次上课自动建档：新面孔 → data/faces/<id>.jpg + 指纹索引
"""
from __future__ import annotations

import json
import threading
from pathlib import Path

import cv2
import numpy as np

from ..config import config
from ..logger import get_logger

log = get_logger("perception.face")

INDEX_FILE = "faces_index.json"


def dhash(img_bgr, size: int = 64) -> str:
    """计算感知哈希（dHash 64bit）"""
    try:
        gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
        small = cv2.resize(gray, (9, 8), interpolation=cv2.INTER_AREA)
        bits = []
        for y in range(8):
            for x in range(8):
                bits.append("1" if small[y, x] > small[y, x + 1] else "0")
        return "".join(bits)
    except Exception:
        return "0" * size


def hamming(a: str, b: str) -> int:
    return sum(ca != cb for ca, cb in zip(a, b))


class FaceRegistry:
    """人脸底库：建档 / 匹配 / 座位变化记录"""

    def __init__(self):
        self._dir = config.dir("faces")
        self._dir.mkdir(parents=True, exist_ok=True)
        self._index_path = self._dir / INDEX_FILE
        self._faces: dict[str, dict] = {}   # id -> {name, file, hash, seats:[]}
        self._cascade = None
        self._lock = threading.Lock()
        self._load()

    def _load(self):
        if self._index_path.exists():
            try:
                self._faces = json.loads(self._index_path.read_text(encoding="utf-8"))
            except Exception as e:
                log.warning(f"人脸索引读取失败: {e}")

    def save(self):
        try:
            self._index_path.write_text(json.dumps(self._faces, ensure_ascii=False, indent=2), encoding="utf-8")
        except Exception as e:
            log.warning(f"人脸索引保存失败: {e}")

    def _ensure_cascade(self):
        if self._cascade is None:
            path = cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
            self._cascade = cv2.CascadeClassifier(path)
        return self._cascade

    def detect_faces(self, frame_bgr) -> list[tuple[int, int, int, int]]:
        """返回人脸框列表 (x,y,w,h)"""
        cascade = self._ensure_cascade()
        gray = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2GRAY)
        gray = cv2.equalizeHist(gray)
        faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5, minSize=(40, 40))
        return [(int(x), int(y), int(w), int(h)) for x, y, w, h in faces]

    def observe(self, frame_bgr, seat_label: str = "", auto_name: str = "") -> list[dict]:
        """处理一帧：检测人脸 → 建档/匹配 → 返回 [{face_id, name, seat}]"""
        results = []
        faces = self.detect_faces(frame_bgr)
        for (x, y, w, h) in faces:
            crop = frame_bgr[max(0, y - 10): y + h + 10, max(0, x - 10): x + w + 10]
            if crop.size == 0:
                continue
            fp = dhash(crop)
            face_id = self._match(fp)
            if face_id is None:
                # 新面孔：建档
                face_id = f"f{int(round(float(len(self._faces) + 1))):04d}"
                # 找不重复编号
                n = len(self._faces) + 1
                while f"f{n:04d}" in self._faces:
                    n += 1
                face_id = f"f{n:04d}"
                self._faces[face_id] = {
                    "name": auto_name or f"学生{n}",
                    "hash": fp,
                    "file": f"{face_id}.jpg",
                    "seats": [],
                }
                cv2.imwrite(str(self._dir / f"{face_id}.jpg"), crop)
                log.info(f"[建档] 新面孔 {face_id} ({self._faces[face_id]['name']})")
            # 记录座位出现
            if seat_label:
                seats = self._faces[face_id].setdefault("seats", [])
                if seat_label not in seats:
                    seats.append(seat_label)
            results.append({
                "face_id": face_id,
                "name": self._faces[face_id].get("name", ""),
                "box": [x, y, w, h],
                "seat": seat_label,
            })
        if results:
            self.save()
        return results

    def _match(self, fp: str, threshold: int = 8) -> str | None:
        """指纹匹配，返回已存在 face_id"""
        best_id, best_dist = None, 999
        for fid, info in self._faces.items():
            d = hamming(fp, info.get("hash", ""))
            if d < best_dist:
                best_id, best_dist = fid, d
        return best_id if best_dist <= threshold else None

    def rename(self, face_id: str, name: str):
        with self._lock:
            if face_id in self._faces:
                self._faces[face_id]["name"] = name
                self.save()

    def list_faces(self) -> dict:
        return {k: {"name": v.get("name"), "file": v.get("file"), "seats": v.get("seats")}
                for k, v in self._faces.items()}
