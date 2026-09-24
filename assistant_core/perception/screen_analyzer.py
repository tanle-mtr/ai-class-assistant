"""屏幕分析（实时活跃度，不保存原始屏幕录像）
- 周期性截屏对比相邻帧差异 → 活跃度 0~1
- is_active() 供拖堂判定与课堂节奏分析
"""
from __future__ import annotations

import threading
import time

import numpy as np

from ..config import config
from ..logger import get_logger

log = get_logger("perception.screen")


class ScreenAnalyzer:
    def __init__(self, interval: float = 30.0):
        self.interval = interval
        self._last_frame: np.ndarray | None = None
        self._active = False
        self._lock = threading.Lock()
        self._thread: threading.Thread | None = None
        self._running = False
        self._activity_history: list[tuple[int, float]] = []
        self._sample_total = 0      # 采样尝试次数
        self._sample_ok = 0         # 抓屏成功次数（电脑未开机/锁屏时抓屏失败）

    def start(self):
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="screen-analyzer")
        self._thread.start()

    def stop(self):
        self._running = False

    def _loop(self):
        while self._running:
            try:
                self._sample()
            except Exception as e:
                log.debug(f"屏幕分析失败: {e}")
            time.sleep(self.interval)

    def _sample(self):
        self._sample_total += 1
        img = self._grab()
        if img is None:
            return
        try:
            gray = np.asarray(img, dtype=np.uint8)
            gray = gray[::4, ::4]  # 降采样提速
            diff = 0.0
            if self._last_frame is not None:
                if self._last_frame.shape == gray.shape:
                    diff = float(np.mean(np.abs(gray.astype(np.int16) - self._last_frame.astype(np.int16))) / 255.0)
            self._last_frame = gray
            self._active = diff > config.get("overtime_screen_threshold", 0.6)
            self._activity_history.append((int(time.time()), round(diff, 3)))
            if len(self._activity_history) > 500:
                self._activity_history = self._activity_history[-500:]
            self._sample_ok += 1
        except Exception as e:
            log.debug(f"屏幕采样计算失败: {e}")

    @staticmethod
    def _grab():
        """截取全屏（PIL ImageGrab，Windows）"""
        try:
            from PIL import ImageGrab
            return ImageGrab.grab(all_screens=False)
        except Exception:
            return None

    def is_active(self) -> bool:
        return self._active

    def coverage(self) -> float:
        """屏幕采样覆盖率 0~1：抓屏成功次数/尝试次数。
        电脑绝大多数时间未开机（无画面/锁屏/关机）时该值趋近 0。"""
        if self._sample_total <= 0:
            return 0.0
        return self._sample_ok / self._sample_total

    def activity_history(self) -> list[tuple[int, float]]:
        return list(self._activity_history)
