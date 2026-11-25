using System.Drawing;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>总结文件列表视图：按日期/课程列出 data/exports 下的 md 文档。</summary>
public sealed class SummariesView : ViewBase
{
    /// <summary>一条总结文件记录。</summary>
    public sealed class Item
    {
        public string Path = "";
        public string Title = "";
        public string Meta = "";
        public string Date = "";
    }

    private IReadOnlyList<Item> _items = Array.Empty<Item>();

    public SummariesView() : base("总结文件") { }

    /// <summary>文件提供者；为 null 时扫描 data/exports 目录。</summary>
    public Func<IReadOnlyList<Item>>? ItemsProvider { get; set; }

    /// <summary>点击查看某份总结。</summary>
    public Action<string>? OpenRequested { get; set; }

    protected override Task LoadAsync(CancellationToken ct)
    {
        try
        {
            _items = ItemsProvider?.Invoke() ?? ScanExports();
        }
        catch
        {
            _items = Array.Empty<Item>();
        }

        return Task.CompletedTask;
    }

    /// <summary>默认扫描 data/exports 下的 md 文件。</summary>
    public static IReadOnlyList<Item> ScanExports()
    {
        try
        {
            string root = AppConfig.Instance.Dir("exports");
            if (!Directory.Exists(root))
            {
                return Array.Empty<Item>();
            }

            var list = new List<Item>();
            foreach (string file in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string dir = Path.GetDirectoryName(file) ?? "";
                string date = new DirectoryInfo(dir).Name;
                if (!date.All(char.IsDigit))
                {
                    date = File.GetLastWriteTime(file).ToString("yyyy-MM-dd");
                }

                list.Add(new Item
                {
                    Path = file,
                    Title = name,
                    Meta = $"{date} · 已生成",
                    Date = date
                });
            }

            return list.OrderByDescending(i => i.Date).ToList();
        }
        catch
        {
            return Array.Empty<Item>();
        }
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Painter.Text(g, "总结文件", new RectangleF(20, 18, 200, 32), Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);

        if (_items.Count == 0)
        {
            Painter.RoundRect(g, new Rectangle(20, 66, rect.Width - 40, rect.Height - 90), Theme.Radius, Theme.Card, Theme.Border);
            Painter.Text(g, "暂无总结文件", new RectangleF(20, 90, rect.Width - 40, 200),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        int y = 68;
        string? lastDate = null;
        foreach (Item item in _items)
        {
            if (y > rect.Bottom - 60)
            {
                break;
            }

            if (item.Date != lastDate)
            {
                lastDate = item.Date;
                Painter.Text(g, item.Date, new RectangleF(22, y, 200, 24), Theme.SectionFont, Theme.Primary, ContentAlignment.MiddleLeft);
                y += 28;
            }

            Rectangle row = new(20, y, rect.Width - 40, 52);
            Painter.RoundRect(g, row, 10, Theme.Card, Theme.Border);
            Rectangle iconBox = new(row.X + 12, row.Y + 10, 32, 32);
            Painter.RoundRect(g, iconBox, 9, Theme.Soft);
            IconSet.Document(g, Rectangle.Inflate(iconBox, -7, -7), Theme.Primary);

            Painter.Text(g, item.Title, new RectangleF(row.X + 54, row.Y + 6, row.Width - 160, 24),
                Theme.BodyFont, Theme.Text, ContentAlignment.BottomLeft);
            Painter.Text(g, item.Meta, new RectangleF(row.X + 54, row.Y + 28, row.Width - 160, 18),
                Theme.TinyFont, Theme.Text2, ContentAlignment.TopLeft);

            Rectangle btn = new(row.Right - 76, row.Y + 13, 62, 26);
            bool hover = IsHovered(btn);
            Painter.Pill(g, btn, "查看", Theme.TinyFont, hover ? Theme.PrimaryLight : Theme.Primary, Color.White);
            string path = item.Path;
            AddHit(btn, () => OpenRequested?.Invoke(path), () => Invalidate());
            y += 58;
        }
    }
}
