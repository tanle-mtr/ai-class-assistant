"""AI 模块（导出各引擎单例）"""
from .asr import asr_engine
from .ollama_client import ollama
from .rag import textbook_index
from .skill_engine import skill_engine
from .vision import vision
