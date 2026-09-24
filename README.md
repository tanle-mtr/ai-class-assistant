# AI Classroom Assistant ｜ AI 课堂助手

> 常驻托盘的智能课堂辅助软件：监听 ClassIsland 获取当前课程，共享方式采集麦克风/摄像头/屏幕，下课自动生成 Markdown 课堂总结、导学案、思维导图与分贝报告；支持监控切片、OpenList 挂载、Cloudflare Tunnel 内网穿透与课件按科目上传。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue)
![Python](https://img.shields.io/badge/Python-3.11%2B-green)
![.NET](https://img.shields.io/badge/.NET-8-blue)
![Vue](https://img.shields.io/badge/Vue-3-brightgreen)

AI Classroom Assistant is a **multi-language Windows desktop app** that turns a regular classroom PC into an intelligent teaching assistant:

- 📚 Knows the **current lesson** by listening to [ClassIsland](https://github.com/ClassIsland/ClassIsland) (official IPC, no HTTP port needed)
- 🎙️ Captures mic / camera / screen in **shared (non-exclusive) mode**, so the teacher's own software keeps working
- 🤖 Runs **locally** via [Ollama](https://ollama.com) — you pick the model at startup (`ollama list`), nothing leaves the machine
- 📝 Auto-generates Markdown deliverables when class ends:
  - **Teacher summary** — class quality, what to elaborate on, what to shorten, anti-overtime advice
  - **Student summary** — key points & difficulties
  - **Student & teacher guided-learning plan** (导学案)
  - **Review-class variant exercises** (讲评课变式练习)
  - **Mind map** (Markdown + Markmap, new lessons only)
  - **Exam/study-hall report** — per-minute dB line chart + average dB
- 🎬 **Monitoring mode** — slices camera + audio by lesson, keeps 14 days, auto-cleans; screen is analyzed live but never archived
- 🧠 Skips generation for non-learning sessions (movies / free time) or when the PC was mostly off — monitoring footage is still kept
- 🗺️ Seat-map OCR from an uploaded photo + face-based auto seat-change detection
- 📖 Textbook RAG knowledge base (official free e-textbooks or user-imported PDFs)
- 🌐 OpenList mounting + **Cloudflare Tunnel** (no VPS needed) + auto public-IP lookup
- 📤 Courseware upload, auto-categorized by subject, no password

## Tech Stack

| Layer | Language / Framework | Role |
|---|---|---|
| Core | Python 3.11+ | Audio capture / ASR / dB, camera, screen analysis, Ollama, RAG, summary pipeline, FastAPI web API |
| Tray & bridge | C# (.NET 8 WinForms / console) | Autostart, tray resident, ClassIsland IPC bridge → HTTP 18761 |
| Web console | Vue 3 + Vite | Overview / courseware upload / seat map / settings |
| Scripts | PowerShell | Startup task & shutdown auto-export |

Ports: web console **18760**, ClassIsland bridge **18761**, OpenList **5244**.

## Quick Start

```powershell
# 1) Python deps
pip install -r assistant_core/requirements.txt

# 2) Ollama models (lightweight recommended for low-end PCs)
ollama pull qwen3:1.7b      # main model
ollama pull gemma3:1b       # vision (OCR / image description)

# 3) Build C# tray & bridge
cd tray\ClassIslandBridge; dotnet build -c Release
cd ..\AssistantTray;        dotnet build -c Release

# 4) Build Vue console
cd webui; npm install; npm run build

# 5) Run
tray\AssistantTray\bin\Release\net8.0-windows\AssistantTray.exe
```

> The tray registers itself for autostart on first run and spawns the Python core + bridge.
> Run `scripts\install-tasks.ps1` (as admin) to register the **shutdown auto-export** scheduled task.

### Run the core directly (development)

```powershell
python -m assistant_core.main
```

Open the web console at `http://127.0.0.1:18760`.

## Built-in Skills

`assistant_core/skills/*.md` — a Markdown skill system (frontmatter + body) injected into Ollama:

- `classroom-summary` – teacher & student summaries
- `guide-plan` – student guided-learning plan
- `teacher-guide` – teacher guided-learning plan
- `review-questions` – variant exercises for review classes
- `mindmap` – Markmap mind map
- `db-report` – exam/study-hall dB report
- `lesson-content-check` – detects non-learning lessons (movies etc.)
- `image-ocr` – **pure-text-AI image recognition** via local vision model
- `seatmap-parse` – seat-map OCR
- `textbook-qa` – textbook knowledge-base Q&A

Add your own by dropping a new `.md` file into the skills folder.

## Privacy

- All sensitive data is processed **locally**; faces are not stored by default, raw footage is never uploaded.
- Surveillance recordings stay on this machine and are auto-deleted after 14 days.
- When deploying in schools, inform the class that monitoring is active.

## License

[MIT](LICENSE) © 2026 AI Classroom Assistant Contributors

## Disclaimer

This project is provided as-is. Make sure classroom monitoring complies with your school's / region's privacy and consent requirements.
