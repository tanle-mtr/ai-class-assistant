# AI 课堂助手 - 独立未归档导出兜底（核心未运行时由关机脚本调用）
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from assistant_core.config import config
from assistant_core.exporter import Exporter
from assistant_core.logger import setup_logger

setup_logger(log_dir=config.dir("logs"))
e = Exporter()
n = e.export_unclassified()
removed = e.cleanup()
print(f"兜底导出 {n} 个未归档文件，清理 {removed} 个过期录像")
