"""AI 层：Ollama / ASR / 教材 RAG / 总结 / 视觉 / 技能库。

各模块在子文件中导出单例（ollama / asr_engine / textbook_index /
vision / skill_engine），此处不 eager import，避免启动期不必要的初始化。
"""
