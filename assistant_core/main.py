"""兼容别名：python -m assistant_core.main 等价于 python -m assistant_core.cli。

run.py 与旧脚本可能引用该入口，此处只做转发，不重复实现启动逻辑。
"""
from __future__ import annotations

from .cli import main

if __name__ == "__main__":
    main()
