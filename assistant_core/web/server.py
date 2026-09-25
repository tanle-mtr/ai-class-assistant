"""Web 服务（FastAPI）
- 控制台 API：状态/模型/总结列表/座位表/隧道/课表
- 教科书知识库：教材匹配/导入（RAG 检索问答）
- 课件上传：按科目自动分类建文件夹
- 静态托管 Vue 构建产物（webui/dist）
"""
from __future__ import annotations

import json
from datetime import datetime
from pathlib import Path

from fastapi import FastAPI, File, UploadFile, Form
from fastapi.responses import JSONResponse, FileResponse
from fastapi.staticfiles import StaticFiles

from ..ai.ollama_client import ollama
from ..ai.rag import (
    textbook_index, match_textbooks, add_book, guess_region_by_ip, OFFICIAL_FREE_SITE,
)
from ..ai.skill_engine import skill_engine
from ..classisland.file_watcher import today_schedule
from ..config import config, query_ollama_models
from ..logger import get_logger
from ..seatmap import seatmap
from ..tunnel.cloudflared import cloudflared
from ..tunnel.openlist import openlist

log = get_logger("web")

# 无窗口启动（托盘/打包 exe）时 sys.stdout/stderr 可能为 None，
# uvicorn 的日志 formatter 会调用 sys.stdout.isatty() 而崩溃，这里兜底。
import sys as _sys
import os as _os
if _sys.stdout is None:
    _sys.stdout = open(_os.devnull, "w", encoding="utf-8", errors="replace")
if _sys.stderr is None:
    _sys.stderr = open(_os.devnull, "w", encoding="utf-8", errors="replace")

# 由 server.start(app) 注入的 AssistantApp 引用
_app_ref: dict = {"app": None}

WEBUI_DIST = Path(__file__).resolve().parent.parent.parent / "webui" / "dist"


def _db_values(raw: list) -> list[float]:
    """db_samples 元素为 {"sec": int, "db": float}，取出分贝数值"""
    values: list[float] = []
    for s in raw:
        v = s.get("db", 0) if isinstance(s, dict) else s
        try:
            values.append(float(v))
        except (TypeError, ValueError):
            continue
    return values


def _db_per_minute(raw: list) -> list[float]:
    """按每分钟聚合平均分贝（用采样点的 sec 归桶）"""
    buckets: dict[int, list[float]] = {}
    for i, s in enumerate(raw):
        v = s.get("db", 0) if isinstance(s, dict) else s
        try:
            v = float(v)
        except (TypeError, ValueError):
            continue
        sec = s.get("sec", i) if isinstance(s, dict) else i
        try:
            key = int(sec) // 60
        except (TypeError, ValueError):
            key = i // 60
        buckets.setdefault(key, []).append(v)
    return [round(sum(buckets[k]) / len(buckets[k]), 1) for k in sorted(buckets)]


def _build_system_prompt(max_chars: int = 3000) -> str:
    """把所有内置技能的核心指令拼接成系统提示词（用于 Modelfile 打包专属模型）"""
    head = (
        "你是「AI 课堂助手」的课堂专用本地模型，服务对象是一线教师：备课、授课、课后复盘。"
        "全程使用中文，回答结构化、可落地，不编造教材内容。\n\n## 内置技能与要求\n"
    )
    parts: list[str] = []
    total = len(head)
    for meta in skill_engine.list_skills():
        sk = skill_engine.get(meta["name"])
        block = [f"- {meta['name']}：{meta.get('description', '')}"]
        for line in (sk.body if sk else "").splitlines():
            t = line.strip()
            if not t or t.startswith("#") or t.startswith("---") or t.startswith("```"):
                continue
            if len(t) > 100:
                t = t[:100] + "…"
            block.append("  " + t)
        text = "\n".join(block)
        if total + len(text) > max_chars:
            break
        parts.append(text)
        total += len(text)
    prompt = head + "\n".join(parts)
    # 避免破坏 Modelfile 的三引号语法
    return prompt.replace('"""', "'''")


def create_app() -> FastAPI:
    app = FastAPI(title="AI 课堂助手", version="0.1.0")

    # ---------- 状态 ----------
    @app.get("/api/health")
    def health():
        return {"ok": True}

    @app.get("/api/state")
    def state():
        a = _app_ref["app"]
        s = a.session if a and a.session else None
        return {
            "session": s.to_dict() if s else None,
            "model": config.get("model", ""),
            "vision_model": config.get("vision_model", ""),
            "monitor_enabled": config.get("monitor_enabled", True),
            "record_camera": config.get("record_camera", True),
            "record_audio": config.get("record_audio", True),
            "monitor_always_record": config.get("monitor_always_record", False),
            "tunnel": cloudflared.status(),
            "public_ip": a.public_ip() if a else "",
        }

    @app.get("/api/db")
    def db_stats():
        """分贝统计（供桌面面板/网页画波形）

        返回最近 120 个分贝数字 + 平均 + 峰值 + 每分钟聚合，无会话时返回空数据不报错。
        """
        try:
            a = _app_ref["app"]
            raw = list(a.session.db_samples[-120:]) if a and a.session else []
            samples = _db_values(raw)  # db_samples 元素为 {"sec": int, "db": float}
            avg = round(sum(samples) / len(samples), 1) if samples else 0
            peak = round(max(samples), 1) if samples else 0
            return {
                "samples": [round(v, 1) for v in samples],
                "avg_db": avg,
                "peak_db": peak,
                "per_minute": _db_per_minute(raw),
                "count": len(samples),
            }
        except Exception as e:
            log.warning(f"分贝统计失败: {e}")
            return {"samples": [], "avg_db": 0, "peak_db": 0, "per_minute": [], "count": 0}

    @app.post("/api/summarize")
    def summarize_now():
        """手动触发：为当前课堂生成总结并导出"""
        import threading
        a = _app_ref["app"]
        if a and a.session:
            s = a.session
            threading.Thread(target=a._run_summary_pipeline, args=(s,), daemon=True).start()
            return {"ok": True}
        return {"ok": False, "error": "当前无课堂会话"}

    @app.post("/api/export")
    def export_now():
        """手动触发：立即导出当前课堂（含监控归档）"""
        import threading
        a = _app_ref["app"]
        if a and a.session:
            s = a.session
            threading.Thread(target=a._run_summary_pipeline, args=(s,), daemon=True).start()
            return {"ok": True}
        return {"ok": False, "error": "当前无课堂会话"}

    # ---------- 模型 ----------
    @app.get("/api/models")
    def models():
        return {
            "models": query_ollama_models(),
            "current": config.get("model", ""),
            "vision_models": [m["name"] for m in query_ollama_models()
                              if any(k in m["name"].lower() for k in ("vl", "llava", "gemma3", "vision", "minicpm"))],
            "hardware": {
                "cpu": config.hardware.cpu,
                "cores": config.hardware.cores,
                "ram_gb": config.hardware.ram_gb,
                "gpu": config.hardware.gpu_name,
                "vram_gb": config.hardware.gpu_vram_gb,
                "advice": config.hardware.model_advice(),
            },
        }

    @app.post("/api/models/select")
    def select_model(payload: dict):
        model = payload.get("model", "")
        if model:
            config.set("model", model)
        return {"ok": True, "model": config.get("model")}

    @app.post("/api/models/create")
    def create_custom_model(payload: dict):
        """需求 §2-c：用内置技能+提示词经 Modelfile 打包专属模型"""
        name = (payload.get("name") or "").strip()
        base = (payload.get("base_model") or "").strip() or config.get("model", "")
        if not name:
            return {"ok": False, "name": "", "msg": "请填写要创建的模型名称"}
        if not base:
            return {"ok": False, "name": name, "msg": "未指定基础模型，请先配置主力模型或传入 base_model"}
        try:
            system = _build_system_prompt()
            modelfile = (
                f"FROM {base}\n"
                f"PARAMETER temperature 0.3\n"
                f'SYSTEM """{system}"""\n'
            )
            ok = ollama.create_model(name, modelfile)
            if ok:
                return {"ok": True, "name": name, "base_model": base,
                        "msg": f"专属模型 {name} 创建成功（基于 {base}）"}
            return {"ok": False, "name": name, "base_model": base,
                    "msg": f"创建失败：Ollama 未响应或基础模型 {base} 不存在，请先执行 ollama pull {base}"}
        except Exception as e:
            log.warning(f"创建专属模型失败: {e}")
            return {"ok": False, "name": name, "base_model": base, "msg": f"创建失败：{e}"}

    # ---------- 导出浏览 ----------
    @app.get("/api/summaries")
    def summaries():
        """按 日期/课程 列出导出目录"""
        root = config.dir("exports")
        out = []
        if root.exists():
            for day in sorted(root.iterdir()):
                if not day.is_dir():
                    continue
                courses = []
                for c in sorted(day.iterdir()):
                    if not c.is_dir():
                        continue
                    files = [{"name": f.name, "size": f.stat().st_size,
                              "url": f"/api/files/{day.name}/{c.name}/{f.name}"}
                             for f in sorted(c.iterdir()) if f.is_file()]
                    courses.append({"name": c.name, "files": files})
                out.append({"date": day.name, "courses": courses})
        return out

    # ---------- 文件下载 ----------
    @app.get("/api/files/{date}/{course}/{filename}")
    def file_download(date: str, course: str, filename: str):
        root = config.dir("exports")
        p = (root / date / course / filename).resolve()
        # 防路径穿越
        if not str(p).startswith(str(root.resolve())):
            return JSONResponse({"error": "非法路径"}, status_code=400)
        if p.is_file():
            return FileResponse(p, filename=p.name)
        return JSONResponse({"error": "文件不存在"}, status_code=404)

    # ---------- 文件内容（前端直接渲染 md 总结） ----------
    @app.get("/api/file/content")
    def file_content(date: str = "", course: str = "", name: str = ""):
        """读取导出目录下的 md 文本（供前端渲染），带路径穿越防护"""
        try:
            root = config.dir("exports")
            p = (root / date / course / name).resolve()
            # 防路径穿越：解析后的路径必须在导出根目录内
            if not str(p).startswith(str(root.resolve())):
                return JSONResponse({"error": "非法路径"}, status_code=400)
            if not p.is_file():
                return JSONResponse({"error": "文件不存在"}, status_code=404)
            return {"name": p.name, "content": p.read_text(encoding="utf-8", errors="ignore")}
        except Exception as e:
            log.warning(f"读取文件内容失败: {e}")
            return JSONResponse({"error": f"读取失败：{e}"}, status_code=400)

    # ---------- 课件上传（按科目分类） ----------
    @app.post("/api/upload")
    async def upload(subject: str = Form(""), file: UploadFile = File(...)):
        subjects = config.get("courseware_subjects", []) or []
        subj = subject.strip() or "未分类"
        if subj not in subjects:
            subjects.append(subj)
            config.set("courseware_subjects", subjects)
        courseware_root = config.dir("courseware")
        subj_dir = courseware_root / subj
        subj_dir.mkdir(parents=True, exist_ok=True)
        # 同时复制一份到桌面"课件"文件夹（老师课件传到此电脑桌面）
        desktop = Path.home() / "Desktop"
        if not desktop.exists():
            desktop = Path.home() / "桌面"
        target = (desktop / "课件" / subj) if desktop.exists() else subj_dir
        target.mkdir(parents=True, exist_ok=True)
        safe_name = "".join(c for c in file.filename if c not in '\\/:*?"<>|') or "课件"
        content = await file.read()
        (subj_dir / safe_name).write_bytes(content)
        (target / safe_name).write_bytes(content)
        log.info(f"课件上传: {subj}/{safe_name}（{len(content)} 字节）")
        return {"ok": True, "subject": subj, "filename": safe_name, "saved_to": str(target)}

    @app.get("/api/courseware")
    def courseware():
        root = config.dir("courseware")
        out = {}
        if root.exists():
            for d in sorted(root.iterdir()):
                if d.is_dir():
                    out[d.name] = [{"name": f.name, "size": f.stat().st_size} for f in sorted(d.iterdir()) if f.is_file()]
        return out

    # ---------- 教科书知识库（RAG） ----------
    @app.get("/api/textbook/status")
    def textbook_status():
        """知识库状态：年级/地区/已入库教材/片段数/embedding 模型"""
        try:
            st = textbook_index.stats()
            return {
                "grade": config.get("grade", ""),
                "region": config.get("region", ""),
                "books": st.get("books", {}),
                "chunks": st.get("chunks", 0),
                "embed_model": st.get("embed_model", ""),
            }
        except Exception as e:
            log.warning(f"知识库状态读取失败: {e}")
            return {"grade": config.get("grade", ""), "region": config.get("region", ""),
                    "books": {}, "chunks": 0, "embed_model": ""}

    @app.post("/api/textbook/match")
    def textbook_match(payload: dict):
        """年级 + 地区 → 教材版本清单（地区为空时按公网 IP 粗略定位，失败回落默认人教版）"""
        try:
            grade = (payload.get("grade") or "").strip() or config.get("grade", "")
            region = (payload.get("region") or "").strip() or config.get("region", "")
            located = False
            if not region:
                region = guess_region_by_ip()  # 不阻塞：超时即返回空
                located = bool(region)
            books = match_textbooks(grade, region)
            if grade:
                config.set("grade", grade)
            if region:
                config.set("region", region)
            return {
                "grade": grade,
                "region": region,
                "located": located,
                "books": books,
                "official_free": OFFICIAL_FREE_SITE,
                "msg": (
                    f"已按{region or '默认'}匹配教材版本；官方免费电子教材可从 {OFFICIAL_FREE_SITE} 获取，"
                    f"若无可下载的免费版，请自行导入 PDF 建立本地知识库"
                ),
            }
        except Exception as e:
            log.warning(f"教材匹配失败: {e}")
            return {"grade": "", "region": "", "located": False, "books": {}, "msg": f"教材匹配失败：{e}"}

    @app.post("/api/textbook/import")
    async def textbook_import(title: str = Form(""), file: UploadFile = File(...)):
        """导入教材 PDF/TXT/MD → 解析文本 → 本地向量建库"""
        try:
            root = config.dir("textbooks")
            root.mkdir(parents=True, exist_ok=True)
            safe_name = "".join(c for c in (file.filename or "") if c not in '\\/:*?"<>|') or "教材.pdf"
            dest = root / safe_name
            content = await file.read()
            dest.write_bytes(content)
            book_title = (title or "").strip() or Path(safe_name).stem
            chunks = add_book(book_title, str(dest))
            if chunks <= 0:
                return {"ok": False, "title": book_title, "chunks": 0,
                        "msg": "未生成知识库片段：请确认已安装 embedding 模型（终端执行 ollama pull bge-m3），或文件为文字版 PDF"}
            return {"ok": True, "title": book_title, "chunks": chunks,
                    "msg": f"《{book_title}》已入库，共 {chunks} 个片段"}
        except Exception as e:
            log.warning(f"教材导入失败: {e}")
            return {"ok": False, "chunks": 0, "msg": str(e)}

    @app.post("/api/textbook/ask")
    def textbook_ask(payload: dict):
        """教材问答：本地检索 top-k → textbook-qa 技能 + 主力模型生成答案"""
        question = (payload.get("question") or "").strip()
        try:
            top_k = int(payload.get("top_k", 5) or 5)
        except (TypeError, ValueError):
            top_k = 5
        if not question:
            return {"answer": "请输入要提问的内容", "sources": []}
        try:
            hits = textbook_index.retrieve(question, max(1, min(top_k, 20)))
        except Exception as e:
            log.warning(f"教材检索失败: {e}")
            return {"answer": f"检索失败：{e}", "sources": []}
        if not hits:
            empty = textbook_index.stats().get("chunks", 0) == 0
            return {
                "answer": "知识库为空，请先导入教材" if empty
                          else "教材知识库中未检索到相关内容，请换个说法或导入更多教材",
                "sources": [],
            }
        context = "\n\n".join(f"【{h['book']}·{h['chapter']}】{h['text']}" for h in hits)
        try:
            answer = skill_engine.run("textbook-qa", question, context=context)
        except Exception as e:
            log.warning(f"教材问答生成失败: {e}")
            answer = f"（模型生成失败：{e}；请确认 Ollama 已启动且已配置主力模型）\n\n检索到的原文：\n{context}"
        return {"answer": answer, "sources": hits}

    # ---------- 今日课表 ----------
    @app.get("/api/schedule")
    def schedule():
        """今日课程时间线（ClassIsland profiles.json；不可用时用当前会话兜底）"""
        date_str = datetime.now().strftime("%Y-%m-%d")
        try:
            sch = today_schedule()
            if sch.get("items"):
                sch["source"] = "classisland"
                return sch
        except Exception as e:
            log.debug(f"课表解析失败: {e}")
        # 兜底：桥/当前会话返回单条
        a = _app_ref["app"]
        subject = ""
        start = ""
        try:
            st = a._bridge.get_state() if a and getattr(a, "_bridge", None) else None
            if st and st.state == "OnClass" and st.subject:
                subject = st.subject
        except Exception:
            pass
        if not subject and a and a.session and a.session.subject:
            subject = a.session.subject
            try:
                start = a.session.start_time.strftime("%H:%M")
            except Exception:
                start = ""
        items = [{"index": 1, "subject": subject, "start": start, "end": "", "state": "now"}] if subject else []
        return {"date": date_str, "items": items, "source": "session" if items else "none"}

    # ---------- 座位表 ----------
    @app.get("/api/seatmap")
    def get_seatmap():
        return {"data": seatmap._data if hasattr(seatmap, "_data") else {}}

    @app.post("/api/seatmap/init")
    async def init_seatmap(file: UploadFile = File(...), class_name: str = Form("")):
        img_path = config.dir("seats") / "upload_seatmap.png"
        content = await file.read()
        img_path.write_bytes(content)
        result = seatmap.init_from_image(str(img_path), class_name)
        return result

    @app.get("/api/seatmap/export")
    def export_seatmap():
        """导出座位表 Markdown 文本"""
        try:
            return {"markdown": seatmap.export_markdown()}
        except Exception as e:
            log.warning(f"座位表导出失败: {e}")
            return {"markdown": f"（座位表导出失败：{e}）"}

    # ---------- 隧道 ----------
    @app.post("/api/tunnel/token")
    def set_token(payload: dict):
        cloudflared.set_token(payload.get("token", ""))
        if payload.get("token"):
            cloudflared.start()
        return {"ok": True, **cloudflared.status()}

    @app.post("/api/tunnel/start")
    def start_tunnel():
        ok = cloudflared.start()
        return {"ok": ok, **cloudflared.status()}

    @app.get("/api/tunnel/rules")
    def tunnel_rules():
        """隧道代理规则 + 隧道状态（含 cloudflared.yml 路径）"""
        try:
            return {"rules": cloudflared.rules(), **cloudflared.status()}
        except Exception as e:
            log.warning(f"读取隧道规则失败: {e}")
            return {"rules": {}, "msg": f"读取失败：{e}"}

    @app.post("/api/tunnel/login")
    def tunnel_login():
        """引导 cloudflared tunnel login（浏览器授权）"""
        try:
            ok = cloudflared.login()
            return {
                "ok": ok,
                "msg": "登录授权完成，可配置 tunnel_id 后启动命名隧道" if ok
                       else "登录未完成或超时：请确认已安装 cloudflared，并在弹出的浏览器中完成授权",
                **cloudflared.status(),
            }
        except Exception as e:
            log.warning(f"cloudflared 登录异常: {e}")
            return {"ok": False, "msg": f"登录失败：{e}"}

    @app.post("/api/tunnel/proxy")
    def add_proxy(payload: dict):
        """添加隧道代理规则（hostname 可选，留空时用 <name>.<tunnel_domain>）"""
        name = payload.get("name", "svc")
        try:
            port = int(payload.get("port", 0) or 0)
        except (TypeError, ValueError):
            port = 0
        if port <= 0:
            return {"ok": False, "msg": "端口号无效", "rules": cloudflared.rules()}
        cloudflared.add_proxy(name, port,
                              hostname=payload.get("hostname", "") or "",
                              path=payload.get("path", "") or "")
        return {"ok": True, "rules": cloudflared.rules(), **cloudflared.status()}

    @app.get("/api/public_ip")
    def public_ip():
        a = _app_ref["app"]
        return {"ip": a.public_ip() if a else ""}

    # ---------- 快捷操作：监控开关（托盘主面板用） ----------
    @app.post("/api/monitor")
    def set_monitor(payload: dict):
        """切换监控开关与「始终录像/录音」。

        monitor_enabled：总开关；上课中尽力即时启停录音，失败仅告警不抛异常。
        always_record：无人上课时是否也自动切片录像/录音，默认关闭。
        """
        enabled = bool(payload.get("enabled", True))
        config.set("monitor_enabled", enabled)

        always = payload.get("always_record")
        if always is not None:
            always = bool(always)
            config.set("monitor_always_record", always)
            a = _app_ref["app"]
            try:
                if a is not None:
                    if not always:
                        # 立刻停掉已在写入的自动切片，关闭后不再产生新文件
                        a._audio.close_auto_writers()
                        a._camera.close_auto_writers()
                    else:
                        # 打开后，等下一次采样/取帧再按新策略起片
                        pass
            except Exception as e:
                log.warning(f"始终录像开关即时生效失败（配置已保存）: {e}")

        a = _app_ref["app"]
        session = getattr(a, "session", None) if a else None
        if session is not None:
            try:
                if enabled:
                    a._audio.start_recording(session)
                else:
                    a._audio.stop_recording(session)
            except Exception as e:
                log.warning(f"监控开关即时生效失败（配置已保存）: {e}")
        return {
            "ok": True,
            "monitor_enabled": enabled,
            "monitor_always_record": config.get("monitor_always_record", False),
        }

    # ---------- 优雅停机（关机脚本调用） ----------
    @app.post("/api/shutdown")
    def shutdown():
        import os
        import threading
        a = _app_ref["app"]
        if a:
            try:
                a.stop()
            except Exception as e:
                log.warning(f"优雅停机异常: {e}")
        threading.Timer(1.5, os._exit, args=(0,)).start()
        return {"ok": True}

    # ---------- 静态托管 Vue ----------
    if WEBUI_DIST.exists():
        app.mount("/", StaticFiles(directory=str(WEBUI_DIST), html=True), name="webui")

    return app


def _port_free(port: int) -> bool:
    """探测本地端口是否空闲"""
    import socket
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        try:
            s.bind(("127.0.0.1", port))
            return True
        except OSError:
            return False


def start(app_ref, host: str = "127.0.0.1", port: int | None = None):
    """启动 web 服务（后台线程）；端口被占用时自动顺延找空闲端口"""
    import threading
    import uvicorn

    _app_ref["app"] = app_ref
    if port is None:
        port = int(config.get("web_port", 18760))
    # 先探测空闲端口（不启动多个实例）；跳过 ClassIsland 桥专用端口 18761
    BRIDGE_PORT = 18761
    for _ in range(30):
        if _port_free(port) and port != BRIDGE_PORT:
            break
        log.warning(f"端口 {port} 被占用或为桥端口，尝试 {port + 1}")
        port += 1
    cfg = uvicorn.Config(create_app(), host=host, port=port, log_level="warning")
    server = uvicorn.Server(cfg)
    t = threading.Thread(target=server.run, daemon=True, name="web-server")
    t.start()
    config.set("web_port", port)
    log.info(f"Web 控制台: http://{host}:{port}")
    return server
