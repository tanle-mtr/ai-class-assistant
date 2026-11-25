using System.Drawing;
using AiClassAssistant.Core.Models;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>座位表视图：图片导入初始化 + 网格 pill 节点 + 导出。</summary>
public sealed class SeatmapView : ViewBase
{
    private IReadOnlyList<Seat> _seats = Array.Empty<Seat>();

    public SeatmapView() : base("座位表") { }

    // ---------- 数据注入点 ----------
    public Func<IReadOnlyList<Seat>> SeatsProvider { get; set; } = () => Array.Empty<Seat>();
    public Action? ImportRequested { get; set; }
    public Action? ExportRequested { get; set; }

    /// <summary>无数据时是否显示演示网格（接入真实后端后可置 false）。</summary>
    public bool UseDemoWhenEmpty { get; set; } = true;

    public override void OnShow()
    {
        base.OnShow();
        try
        {
            _seats = SeatsProvider() ?? Array.Empty<Seat>();
        }
        catch
        {
            _seats = Array.Empty<Seat>();
        }

        Invalidate();
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Painter.Text(g, "座位表", new RectangleF(20, 16, 160, 32), Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);

        // 工具栏
        Rectangle importBtn = new(20, 58, 132, 38);
        Rectangle exportBtn = new(162, 58, 132, 38);
        ToolButton(g, importBtn, "导入图片", false);
        ToolButton(g, exportBtn, "导出座位表", true);
        AddHit(importBtn, () => ImportRequested?.Invoke(), () => Invalidate());
        AddHit(exportBtn, () => ExportRequested?.Invoke(), () => Invalidate());

        Painter.Text(g, "讲台", new RectangleF(rect.X, 112, rect.Width, 24),
            Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);

        IReadOnlyList<Seat> seats = _seats.Count > 0 ? _seats : (UseDemoWhenEmpty ? DemoSeats() : Array.Empty<Seat>());
        Rectangle grid = new(20, 146, rect.Width - 40, Math.Max(100, rect.Bottom - 166));
        Painter.RoundRect(g, grid, Theme.Radius, Theme.Card, Theme.Border);

        if (seats.Count == 0)
        {
            Painter.Text(g, "暂无座位表，请导入图片初始化", grid, Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        int maxRow = seats.Max(s => s.Row);
        int maxCol = seats.Max(s => s.Col);
        int rows = Math.Max(1, maxRow + 1);
        int cols = Math.Max(1, maxCol + 1);
        float cellW = grid.Width / (float)cols;
        float cellH = grid.Height / (float)rows;
        int size = (int)Math.Max(16, Math.Min(34, Math.Min(cellW, cellH) - 10));

        foreach (Seat seat in seats)
        {
            float cx = grid.X + (seat.Col + 0.5f) * cellW;
            float cy = grid.Y + (seat.Row + 0.5f) * cellH;
            Rectangle node = new((int)(cx - size / 2f), (int)(cy - size / 2f), size, size);
            bool occupied = !string.IsNullOrEmpty(seat.Name);
            if (occupied)
            {
                Painter.Pill(g, node, seat.Name.Length > 2 ? seat.Name[..2] : seat.Name,
                    Theme.TinyFont, Theme.Soft, Theme.Primary, Theme.SoftBorder);
            }
            else
            {
                Painter.OutlinePill(g, node, "", Theme.TinyFont, Theme.Border, Theme.Text2, Color.FromArgb(0xF8, 0xFA, 0xFC));
            }
        }
    }

    private static void ToolButton(Graphics g, Rectangle r, string text, bool primary)
    {
        if (primary)
        {
            Painter.GradientRoundRect(g, r, Theme.RadiusSm, Theme.Primary, Theme.PrimaryLight, 90f);
            Painter.Text(g, text, r, Theme.SubFont, Color.White, ContentAlignment.MiddleCenter);
        }
        else
        {
            Painter.RoundRect(g, r, Theme.RadiusSm, Theme.Soft, Theme.Border);
            Painter.Text(g, text, r, Theme.SubFont, Theme.Primary, ContentAlignment.MiddleCenter);
        }
    }

    private static List<Seat> DemoSeats()
    {
        string[] names =
        {
            "张伟", "李娜", "王强", "刘洋", "陈静", "赵敏", "孙浩", "周婷",
            "吴磊", "郑爽", "冯刚", "朱琳", "何军", "高燕", "林峰", "马超"
        };
        var result = new List<Seat>();
        int idx = 0;
        for (int row = 0; row < 6; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                string name = row * 8 + col < names.Length && (row + col) % 3 != 0 ? names[idx++] : "";
                result.Add(new Seat
                {
                    Id = $"r{row + 1}c{col + 1}",
                    Row = row,
                    Col = col,
                    Name = name
                });
            }
        }

        return result;
    }
}
