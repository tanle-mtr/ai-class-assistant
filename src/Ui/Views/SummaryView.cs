using System.Drawing;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>
/// 效果图 04：课堂总结 —— 老师总结 / 学生总结 / 思维导图三栏 + 分贝面积图。
/// </summary>
public sealed class SummaryView : ViewBase
{
    private readonly StarRating _stars = new();
    private readonly MindmapView _mindmap = new();
    private readonly AreaChart _chart = new();
    private Size _lastSize;
    private SummaryDoc? _doc;

    public SummaryView() : base("课堂总结")
    {
        Controls.Add(_stars);
        Controls.Add(_mindmap);
        Controls.Add(_chart);
    }

    /// <summary>总结文档提供者。</summary>
    public Func<SummaryDoc?> DocumentProvider { get; set; } = () => null;

    /// <summary>后端无数据时是否展示演示数据（便于直观查看效果；接入真实后端后可置 false）。</summary>
    public bool UseDemoWhenEmpty { get; set; } = true;

    protected override Task LoadAsync(CancellationToken ct)
    {
        try
        {
            _doc = DocumentProvider();
            if (_doc is null && UseDemoWhenEmpty)
            {
                _doc = DemoDocument();
            }
        }
        catch
        {
            _doc = UseDemoWhenEmpty ? DemoDocument() : null;
        }

        if (_doc is not null)
        {
            _stars.Stars = _doc.Stars;
            _mindmap.SetMarkdown(_doc.MindmapMarkdown);
            _chart.SetData(_doc.DbSamples, _doc.AvgDb, _doc.PeakDb);
        }
        else
        {
            _stars.Stars = 0;
            _mindmap.SetMarkdown("");
            _chart.SetData(Array.Empty<double>(), 0, 0);
        }

        return Task.CompletedTask;
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        LayoutControls(rect);

        // 顶部标题卡
        Rectangle head = new(20, 16, rect.Width - 40, 88);
        Painter.Shadow(g, head, Theme.Radius, 3, 2);
        Painter.RoundRect(g, head, Theme.Radius, Theme.Card, Theme.Border);

        string title = _doc is null ? "课堂总结" :
            (string.IsNullOrEmpty(_doc.Title) ? "课堂总结" : "课堂总结 · " + _doc.Title);
        Painter.Text(g, title, new RectangleF(head.X + 20, head.Y + 12, head.Width - 40, 40),
            Theme.Font(24f, FontStyle.Bold), Theme.Text, ContentAlignment.MiddleLeft);
        Painter.Text(g, _doc?.Meta ?? "暂无总结数据",
            new RectangleF(head.X + 20, head.Y + 52, head.Width - 40, 26),
            Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);

        if (_doc is null)
        {
            Painter.Text(g, "暂无课堂总结数据", new RectangleF(40, 170, rect.Width - 80, 300),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        int colY = 120;
        int colH = 210;
        int colW = (rect.Width - 40 - 28) / 3;

        DrawTeacherCard(g, new Rectangle(20, colY, colW, colH));
        DrawStudentCard(g, new Rectangle(20 + colW + 14, colY, colW, colH));
        DrawMindmapCard(g, new Rectangle(20 + (colW + 14) * 2, colY, colW, colH));

        // 分贝面积图卡
        Rectangle chartCard = new(20, colY + colH + 14, rect.Width - 40,
            Math.Max(120, rect.Bottom - colY - colH - 30));
        Painter.Shadow(g, chartCard, Theme.Radius, 3, 2);
        Painter.RoundRect(g, chartCard, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "课堂分贝统计（每分钟）",
            new RectangleF(chartCard.X + 18, chartCard.Y + 8, 300, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
    }

    private void DrawTeacherCard(Graphics g, Rectangle card)
    {
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "老师总结", new RectangleF(card.X + 16, card.Y + 8, 180, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        Painter.Text(g, "课堂质量", new RectangleF(card.X + 16, card.Y + 44, 70, 24),
            Theme.SubFont, Theme.Text2, ContentAlignment.MiddleLeft);
        Painter.Text(g, $"{_doc!.Stars}/5", new RectangleF(card.Right - 60, card.Y + 44, 46, 24),
            Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleRight);

        // 虚线浅绿建议框
        Rectangle box = new(card.X + 14, card.Y + 76, card.Width - 28, card.Height - 88);
        Painter.RoundRect(g, box, 10, Theme.Soft);
        Painter.DashedRoundRect(g, box, 10, Theme.SoftBorder, 1.2f);

        int n = Math.Min(3, _doc.Advice.Count);
        if (n == 0)
        {
            Painter.Text(g, "暂无建议", box, Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        for (int i = 0; i < n; i++)
        {
            int y = box.Y + 12 + i * 36;
            if (y + 30 > box.Bottom)
            {
                break;
            }

            IconSet.Check(g, new Rectangle(box.X + 10, y + 3, 16, 16), Theme.Primary, 1.6f);
            Painter.Text(g, _doc.Advice[i], new RectangleF(box.X + 34, y, box.Width - 46, 30),
                Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleLeft);
        }
    }

    private void DrawStudentCard(Graphics g, Rectangle card)
    {
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "学生总结", new RectangleF(card.X + 16, card.Y + 8, 180, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        int y = card.Y + 50;
        foreach (string point in _doc!.KeyPoints.Take(3))
        {
            Painter.Dot(g, card.X + 22, y + 10, 3.5f, Theme.Primary);
            Painter.Text(g, "本节重点：" + point,
                new RectangleF(card.X + 34, y, card.Width - 50, 46),
                Theme.SubFont, Theme.Text2, ContentAlignment.TopLeft);
            y += 50;
        }

        foreach (string point in _doc.Difficulties.Take(3))
        {
            if (y > card.Bottom - 40)
            {
                break;
            }

            Painter.Dot(g, card.X + 22, y + 10, 3.5f, Theme.Amber);
            Painter.Text(g, "本节难点：" + point,
                new RectangleF(card.X + 34, y, card.Width - 50, 46),
                Theme.SubFont, Theme.Text2, ContentAlignment.TopLeft);
            y += 50;
        }
    }

    private void DrawMindmapCard(Graphics g, Rectangle card)
    {
        Painter.Shadow(g, card, Theme.Radius, 3, 2);
        Painter.RoundRect(g, card, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "思维导图", new RectangleF(card.X + 16, card.Y + 8, 180, 30),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
    }

    private void LayoutControls(Rectangle rect)
    {
        if (_lastSize == rect.Size)
        {
            return;
        }

        _lastSize = rect.Size;
        int colY = 120, colH = 210;
        int colW = (rect.Width - 40 - 28) / 3;

        _stars.Bounds = new Rectangle(20 + 96, colY + 40, 120, 26);
        _mindmap.Bounds = new Rectangle(20 + (colW + 14) * 2 + 8, colY + 40, colW - 16, colH - 52);

        Rectangle chartCard = new(20, colY + colH + 14, rect.Width - 40,
            Math.Max(120, rect.Bottom - colY - colH - 30));
        _chart.Bounds = new Rectangle(chartCard.X + 8, chartCard.Y + 36, chartCard.Width - 16, chartCard.Height - 44);
    }

    /// <summary>效果图 04 对应的演示总结数据。</summary>
    public static SummaryDoc DemoDocument() => new()
    {
        Title = "数学 二次函数（新授课）",
        Meta = "高一(3)班  ·  09:35-10:15  ·  2026-09-24",
        Stars = 4,
        Advice = new List<string>
        {
            "建议细讲：二次函数图像与性质推导",
            "可缩短：例题重复演练部分",
            "防拖堂：收尾压缩 2 分钟，提前布置作业"
        },
        KeyPoints = new List<string> { "二次函数图像、开口方向与顶点" },
        Difficulties = new List<string> { "含参数的最值问题" },
        MindmapMarkdown = """
            - 二次函数
              - 定义
                - 一般式
                - 顶点式
              - 图像
                - 开口方向
                - 对称轴
                - 增减性
              - 应用
                - 最值
            """,
        DbSamples = DemoDb(),
        AvgDb = 52,
        PeakDb = 68
    };

    private static List<double> DemoDb()
    {
        var result = new List<double>();
        double[] anchors = { 40, 48, 62, 78, 70, 64, 60, 57, 55, 58 };
        for (int i = 0; i < anchors.Length; i++)
        {
            result.Add(anchors[i]);
            if (i + 1 < anchors.Length)
            {
                for (int j = 1; j < 5; j++)
                {
                    result.Add(anchors[i] + (anchors[i + 1] - anchors[i]) * j / 5.0);
                }
            }
        }

        return result;
    }
}
