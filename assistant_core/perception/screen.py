"""屏幕活跃度分析（实时，不保存原始屏幕录像）。

周期性截屏对比相邻帧差异得到活跃度 0~1，供拖堂判定与课堂节奏分析。
截图只在内存中瞬时使用，绝不落盘。
"""
from __future__ import annotations

import threading
import time
from typing import Any

import numpy as np

from .. import get_logger
from ..config import config

log = get_logger("perception.screen")


class ScreenAnalyzer:
    """常驻屏幕活跃度采样。"""

    def __init__(self, interval: float | None = None) -> None:
        self.interval = float(interval or config.get("screen_analysis_interval", 30) or 30)
        self._last_frame: np.ndarray | None = None
        self._active = False
        self._lock = threading.RLock()
        self._thread: threading.Thread | None = None
        self._running = False
        self._activity_history: list[tuple[int, float]] = []
        self._sample_total = 0
        self._sample_ok = 0

    def start(self) -> None:
        if self._running:
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True, name="screen-analyzer")
        self._thread.start()

    def stop(self) -> None:
        self._running = False

    def _loop(self) -> None:
        while self._running:
            try:
                self._sample()
            except Exception as exc:
                log.debug(f"屏幕分析跳过: {exc}")
            time.sleep(self.interval)

    def _sample(self) -> None:
        self._sample_total += 1
        image = self._grab()
        if image is None:
            return
        try:
            gray = np.asarray(image.convert("L"), dtype=np.uint8)[::4, ::4]
            diff = 0.0
            if self._last_frame is not None and self._last_frame.shape == gray.shape:
                diff = float(np.mean(np.abs(
                    gray.astype(np.int16) - self._last_frame.astype(np.int16)
                )) / 255.0)
            self._last_frame = gray
            with self._lock:
                self._active = diff > float(config.get("overtime_screen_threshold", 0.6) or 0.6)
                self._activity_history.append((int(time.time()), round(diff, 3)))
                if len(self._activity_history) > 500:
                    self._activity_history = self._activity_history[-500:]
                self._sample_ok += 1
        except Exception as exc:
            log.debug(f"屏幕采样计算失败: {exc}")

    @staticmethod
    def _grab() -> Any | None:
        """截取全屏（PIL ImageGrab）；无桌面/无权限时返回 None。"""
        try:
            from PIL import ImageGrab
            return ImageGrab.grab(all_screens=False)
        except Exception:
            return None

    def is_active(self) -> bool:
        """最近一次采样是否判定屏幕活跃。"""
        with self._lock:
            return self._active

    def coverage(self) -> float:
        """采样覆盖率（成功次数 / 尝试次数），用于判断电脑是否长期未开机。"""
        with self._lock:
            return self._sample_ok / self._sample_total if self._sample_total > 0 else 0.0

    def activity_history(self) -> list[tuple[int, float]]:
        with self._lock:
            return list(self._activity_history)

    def force_sample(self) -> bool:
        """立即采样一次并返回是否活跃（拖堂窗口内主动触发，避免等 30s 间隔）。"""
        try:
            self._sample()
        except Exception:
            pass
        return self.is_active()
