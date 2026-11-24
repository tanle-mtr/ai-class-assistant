# AI 课堂助手 ｜ AI Classroom Assistant

> Windows 桌面课堂助手：托盘常驻，跟随 ClassIsland 感知上课/下课，共享方式采集麦克风/摄像头/屏幕，下课后本地生成课堂总结、导学案、思维导图与分贝报告；支持课件上传、教科书知识库、座位表、OpenList 挂载与 Cloudflare Tunnel 远程访问。

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue)
![Python](https://img.shields.io/badge/Python-3.11%2B-green)
![.NET](https://img.shields.io/badge/.NET-8-blue)
![Vue](https://img.shields.io/badge/Vue-3-brightgreen)

**v2 全量重写版**（2026-11-24）：三层代码从零重构，架构与接口契约见 [`docs/重写架构契约.md`](docs/重写架构契约.md)。

## 功能

- 📚 通过 [ClassIsland](https://github.com/ClassIsland/ClassIsland) 官方 IPC 获取当前课程，IPC 不可用时直读配置文件兜底
- 🎙️ 麦克风 / 摄像头 / 屏幕以**共享模式**采集，不独占设备；分贝采样 + ASR 转写 + 就座率分析 + 拖堂判定
- 🤖 本地 [Ollama](https://ollama.com) 推理，模型由用户选择，可按内置技能 Modelfile 打包专属模型；数据不出本机
- 📝 下课自动产出 Markdown：老师总结（质量/细讲/缩短/防拖堂）、学生总结（重难点）、导学案、思维导图、分贝报告；按课型分流（新授课 / 评讲课 / 考试自习课）
- 🔒 **隐私默认**：无人上课时取流仅用于分贝/就座率分析，**不落盘**；仅在课次内录制，保留 `retention_days` 天后自动清理
- 📤 课件按科目自动分类上传，无需口令
- 🗺️ 座位表图片 OCR 初始化 + 人脸建档 + 换座检测 + 导出
- 📖 教科书知识库：官方免费教材匹配 + PDF 导入 + bge-m3 向量检索问答
- 🌐 OpenList 挂载 + Cloudflare Tunnel 内网穿透 + 端口代理规则 + 公网 IP 查询

## 目录结构与端口

```
assistant_core/   Python 核心：config/events/models/cli + perception/ai/classisland/services/api
webui/            Vue 3 控制台：总览/课件上传/课堂总结/监控/设置/教科书/座位表 + panel.html
tray/             C# 托盘 + 原生主面板 + ClassIsland IPC 桥
scripts/          开机启动 / 关机导出 / 任务注册
docs/             需求规格、UI 效果图、架构契约、代码审查报告
```

| 端口 | 用途 |
|---|---|
| **18760** | Web 控制台 API + 静态页面（被占用自动顺延，写回 config） |
| **18761** | ClassIsland 桥（HTTP） |
| **5244** | OpenList（可选） |

运行时数据在 `data/`（已 gitignore），含 `config/`、`exports/`、`recordings/`、`seats/`、`courseware/`、`textbooks/`、`logs/`。

## 快速开始

```powershell
# 1) Python 依赖
pip install -r requirements.txt

# 2) Ollama 模型（低配机器选轻量模型）
ollama pull qwen3:1.7b       # 主力
ollama pull gemma3:1b        # 视觉（OCR / 识图）
ollama pull bge-m3           # 教科书知识库向量检索（可选增强）

# 3) 构建 C# 托盘与桥
dotnet build tray/ClassIslandBridge/ClassIslandBridge.csproj -c Release
dotnet build tray/AssistantTray/AssistantTray.csproj -c Release

# 4) 构建 Vue 控制台（核心会托管 webui/dist）
cd webui; npm install; npm run build; cd ..

# 5) 运行
python -m assistant_core.cli
# 或直接启动托盘：tray\AssistantTray\bin\Release\net8.0-windows\AssistantTray.exe
```

打开 `http://127.0.0.1:18760`。托盘首次运行会注册开机自启；`scripts\install-tasks.ps1`（管理员）注册关机自动导出任务。

### 开发模式

```powershell
cd webui; npm run dev     # Vite 5173，/api 代理到 18760
```

```powershell
python -m compileall -q assistant_core run.py    # Python 语法校验
python -m assistant_core.cli --export-only        # 离线导出（不起感知/Web）
```

### 打包

```powershell
pip install pyinstaller
pyinstaller assistant-core.spec --noconfirm
# 产物 dist\assistant-core\assistant-core.exe（先 npm run build 打包 webui/dist）
```

## API

共 29 个端点，完整请求/响应契约见 [`docs/重写架构契约.md`](docs/重写架构契约.md) 第 4 节。核心：`/api/health`、`/api/state`、`/api/db`、`/api/schedule`、`/api/monitor`、`/api/shutdown`、`/api/models`、`/api/summaries`、`/api/upload`、`/api/textbook/*`、`/api/seatmap/*`、`/api/tunnel/*`。

## 技能库

`assistant_core/skills/*.md` 为 Markdown 技能包，注入 Ollama 作为系统提示词：课堂总结、导学案、思维导图、分贝报告、课程内容检查、识图 OCR、座位表解析、教科书问答等。放入新 `.md` 文件即可扩展。

## 隐私与合规

- 所有敏感数据**仅本机处理**，原始音视频不上传；人脸默认不落库。
- 录像仅课次内写入，保留期满自动删除；`monitor_always_record` 可在设置中开启无人值守录制（默认关闭）。
- 校园部署请告知班级监控安排，并遵守当地隐私与知情同意要求。

## License

[MIT](LICENSE) © 2026 AI Classroom Assistant Contributors
