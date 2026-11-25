using System.Drawing;
using AiClassAssistant.Core.Ai;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Contracts;
using AiClassAssistant.Core.Events;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Core.Services;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Views;

namespace AiClassAssistant.App;

/// <summary>
/// 应用组装与生命周期：把 Core 的业务服务、感知层、ClassIsland 源与 GDI+ 界面接起来。
/// 全部依赖在构造函数注入，不引入任何 Web 框架。
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly AppConfig _config;
    private readonly IEventBus _bus;
    private readonly IOllamaClient _ollama;
    private readonly SkillLibrary _skills;
    private readonly SeatmapService _seatmap;
    private readonly CoursewareService _courseware;
    private readonly Exporter _exporter;
    private readonly TextbookService _textbook;
    private readonly TunnelService _tunnel;
    private readonly PrivacyService _privacy;
    private readonly SessionManager _sessions;
    private readonly SummaryPipeline _summary;

    private readonly IAudioMonitor? _audio;
    private readonly ICameraMonitor? _camera;
    private readonly IScreenAnalyzer? _screen;
    private readonly IClassIslandSource? _island;

    private readonly ConsoleShell _shell;
    private readonly PanelWindow _panelWindow;
    private readonly MainPanelView _panel;
    private readonly OverviewView _overview;
    private readonly CoursewareView _coursewareView;
    private readonly SummaryView _summaryView;
    private readonly SummariesView _summariesView;
    private readonly MonitorView _monitorView;
    private readonly SettingsView _settingsView;
    private readonly TextbookView _textbookView;
    private readonly SeatmapView _seatmapView;

    private readonly System.Windows.Forms.Timer _uiTimer;
    private bool _disposed;

    public AppController()
    {
        _config = AppConfig.Instance;
        _bus = new EventBus();
        _ollama = new OllamaClient();
        _skills = new SkillLibrary();
        _seatmap = new SeatmapService(_config);
        _courseware = new CoursewareService(_config);
        _exporter = new Exporter(_config);
        _textbook = new TextbookService(_config, _ollama, _skills);
        _tunnel = new TunnelService(_config);
        _privacy = new PrivacyService(_config);
        _sessions = new SessionManager(_bus, _config, _seatmap);
        _summary = new SummaryPipeline(_config, _ollama, _skills);

        // 感知层用反射加载：Core 不引用感知项目，App 才引用；此处直接 new 具体类。
        _audio = new Perception.Audio.AudioMonitor();
        _camera = new Perception.Camera.CameraMonitor();
        _screen = new Perception.Screen.ScreenAnalyzer();
        _island = new ClassIsland.ClassIslandSource();

        _sessions.AttachPerception(_screen, _camera, _audio);
        _sessions.SetPrivacyService(_privacy);
        _sessions.SetObservationProvider(() => _camera?.CollectObservations() is { } obs ? obs : Array.Empty<(string, string)>());
        _sessions.SetObservationCallback(obs => _seatmap.UpdateFromObservations(obs));

        // 会话结束 → 总结 → 导出（Core 层事件，UI 只读结果）
        _sessions.SessionEnded += OnSessionEnded;

        _panel = new MainPanelView();
        _panelWindow = new PanelWindow(_panel);
        _overview = new OverviewView();
        _coursewareView = new CoursewareView();
        _summaryView = new SummaryView();
        _summariesView = new SummariesView();
        _monitorView = new MonitorView();
        _settingsView = new SettingsView();
        _textbookView = new TextbookView();
        _seatmapView = new SeatmapView();

        _shell = new ConsoleShell();
        RegisterViews();
        WireViews();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => RefreshUi();
    }

    /// <summary>托盘面板（原生 GDI+ 自绘）。</summary>
    public PanelWindow Panel => _panelWindow;

    /// <summary>控制台主窗口（无边框 GDI+ 自绘）。</summary>
    public ConsoleShell Shell => _shell;

    public void Start()
    {
        _seatmap.Load();
        _tunnel.Load();

        _audio?.Start();
        _camera?.Start();
        _screen?.Start();
        _island?.Start();
        _sessions.Start();

        if (_island is not null)
        {
            _island.StateChanged += OnIslandState;
            var initial = _island.TodaySchedule();
            if (initial.Count > 0)
            {
                _sessions.HandleState(_island.State, initial[0]);
            }
        }

        if (_audio is not null)
        {
            _audio.DbSampled += s => _sessions.HandleDbSample(s);
            _audio.AsrSegmented += s => _sessions.HandleAsrSegment(s);
        }

        _uiTimer.Start();
        RefreshUi();
    }

    public void ShowConsole(string? view = null)
    {
        if (!_shell.Visible)
        {
            _shell.Show();
        }

        _shell.BringToFront();
        if (!string.IsNullOrEmpty(view))
        {
            _shell.ShowView(view);
        }
    }

    public void ShowPanel(Form? owner = null)
    {
        if (_panelWindow.Visible)
        {
            _panelWindow.BringToFront();
            return;
        }

        var screen = owner is not null ? Screen.FromControl(owner) : Screen.PrimaryScreen;
        if (screen is not null)
        {
            _panelWindow.PlaceAtBottomRight(screen);
        }

        _panelWindow.Show();
        _panelWindow.BringToFront();
    }

    /// <summary>优雅停机：先关感知与状态机，再导出未归档数据。</summary>
    public void Shutdown()
    {
        _uiTimer.Stop();
        _sessions.Stop();
        _audio?.Stop();
        _camera?.Stop();
        _screen?.Stop();
        _island?.Stop();
        _exporter.Cleanup();
        _exporter.ExportUnclassified();
        _audio?.Dispose();
        _camera?.Dispose();
        _screen?.Dispose();
        (_island as IDisposable)?.Dispose();
        (_ollama as IDisposable)?.Dispose();
    }

    private void RegisterViews()
    {
        _shell.RegisterView("overview", "总览", _overview);
        _shell.RegisterView("courseware", "课程", _coursewareView);
        _shell.RegisterView("summaries", "总结", _summariesView);
        _shell.RegisterView("monitor", "监控", _monitorView);
        _shell.RegisterView("settings", "设置", _settingsView);
        _shell.RegisterView("textbook", "教科书", _textbookView);
        _shell.RegisterView("seat", "座位表", _seatmapView);
        _shell.RegisterView("summary", "课堂总结", _summaryView);
    }

    private void WireViews()
    {
        // 主面板
        _panel.CurrentLesson = () => _sessions.CurrentSession?.Lesson;
        _panel.State = () => _sessions.State;
        _panel.RemainingMinutes = () => RemainingMinutes();
        _panel.ModelName = () => string.IsNullOrWhiteSpace(_config.Get("model", ""))
            ? "未选择模型" : _config.Get("model", "");
        _panel.DbSamples = () => RecentDb(28);
        _panel.TodaySchedule = () => (IReadOnlyList<LessonInfo>?)_island?.TodaySchedule() ?? Array.Empty<LessonInfo>();
        _panel.MonitoringEnabled = () => _config.Get("monitor_enabled", true);
        _panel.OvertimeDetected = () => _sessions.CurrentSession?.OvertimeDetected ?? false;
        _panel.TunnelConnected = () => _tunnel.IsRunning;
        _panel.StorageLabel = () => $"存储: 导出 {DateTime.Now:yyyy-MM-dd}";
        _panel.MonitoringToggled = SetMonitoring;

        // 总览
        _overview.ScheduleProvider = () => (IReadOnlyList<LessonInfo>?)_island?.TodaySchedule() ?? Array.Empty<LessonInfo>();
        _overview.CourseCountProvider = () => (_island?.TodaySchedule() as IReadOnlyList<LessonInfo> ?? Array.Empty<LessonInfo>()).Count;
        _overview.OvertimeCountProvider = () => _sessions.CurrentSession?.OvertimeDetected == true ? 1 : 0;
        _overview.AverageDbProvider = () => AverageDb();
        _overview.ExportCountProvider = () => Directory.Exists(_exporter.ExportRoot)
            ? Directory.GetFiles(_exporter.ExportRoot, "*.md", SearchOption.AllDirectories).Length : 0;
        _overview.SummariesProvider = () => BuildSummaryEntries();
        _overview.FoldersProvider = () => Directory.Exists(_exporter.ExportRoot)
            ? Directory.GetDirectories(_exporter.ExportRoot).Select(Path.GetFileName).OfType<string>().ToList()
            : new List<string>();
        _overview.CurrentLessonIndexProvider = () => CurrentLessonIndex();

        // 课件上传
        _coursewareView.SubjectsProvider = () => _courseware.ListSubjects();
        _coursewareView.SubjectAdded = s => _courseware.EnsureSubject(s);
        _coursewareView.UploadRequested = (files, subject) =>
        {
            _ = Task.Run(async () =>
            {
                foreach (var f in files)
                {
                    try
                    {
                        await using var stream = File.OpenRead(f);
                        var name = await _courseware.SaveAsync(subject, Path.GetFileName(f), stream);
                        _coursewareView.SetProgress(name, 100, true);
                    }
                    catch (Exception ex)
                    {
                        _coursewareView.SetProgress(Path.GetFileName(f), 0, false);
                        System.Diagnostics.Debug.WriteLine($"上传失败: {ex.Message}");
                    }
                }
            });
        };

        // 总结
        _summariesView.ItemsProvider = BuildSummariesItems;
        _summariesView.OpenRequested = path =>
        {
            _summaryView.DocumentProvider = () => LoadSummary(path);
            _summaryView.UseDemoWhenEmpty = false;
            _shell.ShowView("summary");
        };

        // 监控
        _monitorView.DbProvider = () => RecentDb(50);
        _monitorView.CurrentDbProvider = () => _audio?.CurrentDb ?? 0;
        _monitorView.ActiveProvider = () => _sessions.CurrentSession?.IsActive == true;
        _monitorView.OvertimeProvider = () => _sessions.CurrentSession?.OvertimeDetected ?? false;
        _monitorView.MonitoringProvider = () => _config.Get("monitor_enabled", true);
        _monitorView.MonitoringToggled = SetMonitoring;

        // 设置
        _settingsView.ModelsProvider = async () => (await _ollama.ListModelsAsync())
            .Select(m => m.Name).ToList();
        _settingsView.CurrentModelProvider = () => _config.Get("model", "");
        _settingsView.ModelSelected = m => _config.Set("model", m);
        _settingsView.CreateModelRequested = async name =>
        {
            var baseModel = _config.Get("model", "");
            if (string.IsNullOrWhiteSpace(baseModel))
            {
                return false;
            }

            var system = _skills.BuildSystemPrompt("classroom-summary");
            return await _ollama.CreateModelAsync(name, baseModel, system);
        };
        _settingsView.MonitoringProvider = () => _config.Get("monitor_enabled", true);
        _settingsView.AlwaysRecordProvider = () => _privacy.AlwaysRecord;
        _settingsView.TunnelEnabledProvider = () => _config.Get("tunnel_enabled", false);
        _settingsView.MonitoringChanged = SetMonitoring;
        _settingsView.AlwaysRecordChanged = SetAlwaysRecord;
        _settingsView.TunnelEnabledChanged = enabled =>
        {
            _config.Set("tunnel_enabled", enabled);
            if (enabled)
            {
                _tunnel.Start();
            }
            else
            {
                _tunnel.Stop();
            }
        };
        _settingsView.TokenProvider = () => _config.Get("tunnel_token", "");
        _settingsView.TokenSaved = token => _tunnel.SetToken(token);

        // 教科书
        _textbookView.BooksProvider = () => _textbook.GetCatalog(
            _config.Get("grade", ""), _config.Get("region", ""))
            .Select(b => b.Title).ToList();
        _textbookView.AskProvider = q => _textbook.AnswerAsync(q);

        // 座位表
        _seatmapView.SeatsProvider = () => _seatmap.Seats;
    }

    private void OnIslandState(LessonState state, LessonInfo? lesson)
    {
        _sessions.HandleState(state, lesson);
    }

    private void OnSessionEnded(Session session)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var doc = await _summary.GenerateAsync(session);
                _exporter.ExportSession(session, doc);
                _exporter.Cleanup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"总结生成失败: {ex.Message}");
            }
        });
    }

    private void SetMonitoring(bool enabled)
    {
        _config.Set("monitor_enabled", enabled);
        var session = _sessions.CurrentSession;
        if (enabled && session is not null)
        {
            _audio?.StartRecording(session);
            if (_privacy.ShouldWriteMedia(session))
            {
                _camera?.StartRecording(session);
            }
        }
        else if (!enabled)
        {
            _audio?.StopRecording(session!);
            _camera?.StopRecording(session!);
        }
    }

    private void SetAlwaysRecord(bool enabled)
    {
        _privacy.OnPolicyChanged(_sessions.CurrentSession, () =>
        {
            if (!enabled)
            {
                _audio?.CloseAutoWriters();
                _camera?.CloseAutoWriters();
            }
        });
    }

    private void RefreshUi()
    {
        _shell.SetStatus(
            $"监控: {(_config.Get("monitor_enabled", true) ? "开启" : "关闭")}",
            $"存储: 导出 {DateTime.Now:yyyy-MM-dd}",
            _tunnel.IsRunning ? "隧道: 已连接" : "隧道: 未连接");
        _panel.Invalidate();
    }

    private double[] RecentDb(int count) => _audio?.RecentSamples(count).ToArray() ?? Array.Empty<double>();

    private double AverageDb()
    {
        var s = RecentDb(60);
        return s.Length == 0 ? 0 : Math.Round(s.Average(), 1);
    }

    private int CurrentLessonIndex()
    {
        var list = (IReadOnlyList<LessonInfo>?)_island?.TodaySchedule() ?? Array.Empty<LessonInfo>();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Subject == _sessions.CurrentSession?.Lesson.Subject)
            {
                return i;
            }
        }

        return -1;
    }

    private double RemainingMinutes()
    {
        var session = _sessions.CurrentSession;
        if (session is null || !session.IsActive)
        {
            return 0;
        }

        var end = ParseToday(session.Lesson.End);
        if (end == default)
        {
            return 0;
        }

        return Math.Max(0, (end - DateTime.Now).TotalMinutes);
    }

    private static DateTime ParseToday(string hhmm)
    {
        if (string.IsNullOrWhiteSpace(hhmm) || !hhmm.Contains(':'))
        {
            return default;
        }

        var parts = hhmm.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m))
        {
            return default;
        }

        var now = DateTime.Now;
        return new DateTime(now.Year, now.Month, now.Day, h, m, 0);
    }

    private IReadOnlyList<SummaryEntry> BuildSummaryEntries()
    {
        return BuildSummariesItems().Take(4).Select(i => new SummaryEntry
        {
            Title = i.Title,
            Meta = i.Meta,
            Path = i.Path,
            Generated = true,
        }).ToList();
    }

    private IReadOnlyList<SummariesView.Item> BuildSummariesItems()
    {
        var result = new List<SummariesView.Item>();
        if (!Directory.Exists(_exporter.ExportRoot))
        {
            return result;
        }

        foreach (var day in Directory.GetDirectories(_exporter.ExportRoot).OrderByDescending(p => p))
        {
            foreach (var course in Directory.GetDirectories(day))
            {
                foreach (var file in Directory.GetFiles(course, "*.md"))
                {
                    result.Add(new SummariesView.Item
                    {
                        Path = file,
                        Title = Path.GetFileNameWithoutExtension(file),
                        Meta = $"{Path.GetFileName(day)} · {Path.GetFileName(course)}",
                        Date = Path.GetFileName(day),
                    });
                }
            }
        }

        return result;
    }

    private SummaryDoc? LoadSummary(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var session = _sessions.CurrentSession ?? new Session();
            var doc = _summary.GenerateFallback(session);
            doc.RawMarkdown = text;
            doc.Title = Path.GetFileNameWithoutExtension(path);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Shutdown();
        _shell.Dispose();
        _panelWindow.Dispose();
    }
}
