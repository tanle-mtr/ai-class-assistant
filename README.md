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
- 🎬 **Monitoring mode** — slices camera + audio **by lesson**, keeps 14 days, auto-cleans; screen is analyzed live but never archived
- 🔒 **Recording is lesson-scoped by default** — the mic/camera streams stay open for dB sampling and seat analysis (needed for overtime detection), but nothing is written to disk unless a lesson is running. See [Recording scope](#recording-scope).
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

Ports: web console **18760** (auto-increments when busy, never grabs **18761**), ClassIsland bridge **18761**, OpenList **5244**.

## Project Layout

```
assistant_core/          Python core (FastAPI web API + perception + AI pipeline)
  perception/            mic / camera / screen capture, face registry
  ai/                    Ollama client, ASR, RAG, summarizer, vision
  classisland/           IPC bridge client + config-file watcher
  web/                   FastAPI server + API used by the web console
  skills/                Markdown skill packs injected into the model
tray/AssistantTray/      C# tray app (autostart, resident panel, process supervisor)
tray/ClassIslandBridge/  C# console bridge → local HTTP 18761
webui/                   Vue 3 + Vite web console (build to webui/dist)
scripts/                 PowerShell start / stop / install-tasks
docs/                    requirement spec, UI design, screenshots
icons/                   app icon (.ico / .png)
```

Runtime data (git-ignored) lives in `data/`:

| Path | Contents |
|---|---|
| `data/config/config.json` | All tunable options (see below) |
| `data/recordings/<date>/` | Monitoring slices (.avi / .wav), auto-purged after `retention_days` |
| `data/exports/<date>/` | Generated Markdown deliverables, grouped by subject |
| `data/seats/seatmap.json` | Seat map + face registry |
| `data/logs/assistant.log` | Runtime log |

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

# 4) Build Vue console (the core serves webui/dist, so this must run at least once)
cd webui; npm install; npm run build

# 5) Run the tray
tray\AssistantTray\bin\Release\net8.0-windows\AssistantTray.exe
```

> The tray registers itself for autostart on first run and spawns the Python core + bridge.
> Run `scripts\install-tasks.ps1` as admin to register the **shutdown auto-export** scheduled task.

### Run the core directly (development)

```powershell
python -m assistant_core.main
```

Open the web console at `http://127.0.0.1:18760`.

Useful scripts:

| Script | Purpose |
|---|---|
| `scripts\start-assistant.ps1` | Start core + bridge in the background |
| `scripts\stop-assistant.ps1` | Stop them (also call this before rebooting — the bridge has a 20s self-heal timer) |
| `scripts\install-tasks.ps1` | Register the shutdown auto-export scheduled task (admin) |

### Build the packaged core

```powershell
pip install pyinstaller
pyinstaller assistant-core.spec --noconfirm
```

Output lands in `dist\assistant-core\assistant-core.exe`. Remember to `npm run build` in `webui/` first — the spec bundles `webui/dist` into the exe.

### Develop the web console with HMR

```powershell
cd webui; npm run dev
```

The Vite dev server runs on its own port; the Python core still serves the built console from `webui/dist`.

## Configuration

All options live in `data/config/config.json` (created on first run from `assistant_core/config.py`). Edit and restart, or toggle the recording-related ones live from the web console.

| Key | Default | Meaning |
|---|---|---|
| `monitor_enabled` | `true` | Master monitoring switch; can be flipped from the tray panel / console |
| `record_camera` | `true` | Record the camera **during a lesson** |
| `record_audio` | `true` | Record audio **during a lesson** (also feeds ASR) |
| `monitor_always_record` | `false` | *Also* auto-slice camera+audio when no lesson is running. Off by default — see below |
| `retention_days` | `14` | Days monitoring footage is kept before auto-purge |
| `web_port` | `18760` | Web console port (auto-increments if taken) |
| `classisland_bridge_port` | `18761` | Local HTTP port of the C# bridge |
| `db_sample_interval` / `db_windows` | `1` / `60` | dB sampling interval and per-minute aggregation window |
| `overtime_check_seconds` | `120` | How long to keep watching after the bell |
| `overtime_screen_threshold` | `0.6` | Screen-activity ratio that counts as "still teaching" |
| `overtime_seated_ratio` | `0.7` | Share of students still seated that counts as "still teaching" |

## Recording scope

This is the one deliberate deviation from the original spec (`docs/需求规格.md` §6, which asked for recording to start at boot).

- **Default (`monitor_always_record: false`)** — the camera and microphone threads stay open so the app can sample dB levels and seat occupancy (the overtime detector needs both), but **no video or audio file is written unless a lesson is in progress**. Class结束后 immediately closes the writer.
- **Spec behavior (`monitor_always_record: true`)** — restores 24/7 auto-slicing outside lessons. Turn it off again from the console and any in-flight "监控" slice is closed right away.

If you enable always-record, remember that footage accumulates even at night — keep `retention_days` in mind and tell the class, per the spec's compliance note.

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

- All sensitive data is processed **locally**; raw audio/video is never uploaded. Faces are not stored by default.
- Monitoring footage stays on this machine and is auto-deleted after `retention_days` (14).
- Recording is lesson-scoped by default — nothing is written to disk outside of class. The only way to change that is the explicit `monitor_always_record` option.
- When deploying in schools, inform the class that monitoring is active.

## License

[MIT](LICENSE) © 2026 AI Classroom Assistant Contributors

## Disclaimer

This project is provided as-is. Make sure classroom monitoring complies with your school's / region's privacy and consent requirements.
