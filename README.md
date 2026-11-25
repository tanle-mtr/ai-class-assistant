# AI 课堂助手 ｜ AI Classroom Assistant

> **v3 全原生自研版**：单一 C# 应用，所有界面 **GDI+ 纯代码自绘**——零 HTML、零 Vue、零浏览器控件、零 XAML。托盘常驻，跟随 ClassIsland 感知上课/下课，共享方式采集麦克风/摄像头/屏幕，下课后本地生成课堂总结、导学案、思维导图与分贝报告。

![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8-blue)
![UI](https://img.shields.io/badge/UI-GDI%2B%20native-orange)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

架构契约见 [`docs/v3全原生架构契约.md`](docs/v3全原生架构契约.md)；界面截图见 [`docs/screenshots-v3/`](docs/screenshots-v3/)。

## 为什么是 v3

v1/v2 用 Python 核心 + Vue 网页控制台 + C# 托盘。v3 按用户要求「自研内核，不要 HTML」彻底重写：

| 层 | v1/v2 | v3 |
|---|---|---|
| 界面 | Vue + HTML + 浏览器 | **WinForms + GDI+ 纯代码自绘** |
| 业务核心 | Python + FastAPI | **C# 类库自研**，无 Web 框架 / HTTP API |
| 感知 | Python sounddevice/opencv | C# + NAudio（WASAPI 共享）+ OpenCvSharp |
| 进程 | Python 核心 + 托盘 + 桥 + Vue | **单一 C# 主程序 + 独立 ClassIsland 桥** |
| 远程网页控制台 | 有 | 取消（用户明确不要 HTML） |

AI 推理仍调用本机 Ollama（外部运行时），业务/界面/感知全部自研。

## 工程结构

```
src/
  AiClassAssistant.sln       6 个工程
  Core/                      自研业务核心：配置/事件/状态机/总结/导出/座位/教材/课件/隧道/技能/Ollama 客户端
  Perception/                NAudio 麦克风 + OpenCvSharp 摄像头 + GDI+ 屏幕活跃度
  ClassIsland/               IPC + HTTP 桥 + 配置文件三级兜底
  Bridge/                    独立桥进程（HTTP 18761）
  Ui/                        GDI+ 自绘：主题/绘制器/控件/8 个视图/主面板/控制台壳
  App/                       主程序：托盘、组装、生命周期、主面板窗口
```

运行时数据在 `data/`（已 gitignore），含 `config/`、`exports/`、`recordings/`、`seats/`、`courseware/`、`textbooks/`、`logs/`。旧 data 完全兼容。

## 构建与运行

```powershell
# 构建（6 个工程，警告即错误）
dotnet build src/AiClassAssistant.sln -c Release -warnaserror

# 运行（托盘常驻，单击托盘弹主面板，双击弹控制台）
dotnet run --project src/App/AiClassAssistant.App.csproj -c Release

# 也可以直接运行构建好的 exe
src\App\bin\Release\net8.0-windows\AI课堂助手.exe

# 启动参数
--show-console   # 直接打开控制台
--show-panel     # 直接打开桌面主面板
--export-only    # 离线导出未归档数据（不起设备/界面）
```

依赖的 NuGet 包（还原时自动拉取）：`NAudio 2.2.1`、`OpenCvSharp4 4.10.0.20241107`（含 runtime.win）、`ClassIsland.Shared.IPC 1.6.0`。

AI 功能需本机运行 Ollama 并 pull 模型（如 `ollama pull qwen3:1.7b`、`ollama pull bge-m3`）。**Ollama 未启动时软件照常运行**，AI 功能显示"服务不可用"提示，不崩溃。

## 界面（全部 GDI+ 自绘）

| 效果图 | 视图 |
|---|---|
| 01 桌面托盘主面板 | `Ui/Views/MainPanelView.cs` + `App/PanelWindow.cs` |
| 02 网页控制台总览 | `Ui/Views/OverviewView.cs` |
| 03 课件上传页 | `Ui/Views/CoursewareView.cs` |
| 04 课堂总结视图 | `Ui/Views/SummaryView.cs` |

另有：总结文件 / 监控 / 设置 / 教科书 / 座位表 五个视图。设计 token 统一在 `Ui/Theme/Theme.cs`，图标用 `GraphicsPath` 纯代码绘制（`IconSet.cs`），无图片资源。

## 隐私

- 无人上课时取流仅用于分贝/就座率分析，**不落盘**；仅在课次内录制，保留期满自动清理
- `monitor_always_record` 可在设置中开启无人值守录制（默认关闭）
- 所有数据仅本机处理，原始音视频不上传；人脸默认不落库
- 校园部署请告知班级监控安排，遵守当地隐私与知情同意要求

## License

[MIT](LICENSE) © 2026 AI Classroom Assistant Contributors
