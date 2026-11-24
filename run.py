"""AI 课堂助手启动入口（PyInstaller spec 也指向此文件）。"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from assistant_core.cli import main

if __name__ == "__main__":
    main()
