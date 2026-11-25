using System.Drawing;
using System.Windows.Forms;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>
/// 效果图 03：课件上传 —— 拖拽/选择文件、科目胶囊、上传进度列表、开始上传。
/// </summary>
public sealed class CoursewareView : ViewBase
{
    private sealed class UploadItem
    {
        public string Name = "";
        public double Progress;
        public bool Done;
    }

    private readonly List<string> _subjects = new() { "语文", "数学", "英语", "物理", "化学", "生物", "历史", "地理", "政治" };
    private readonly List<UploadItem> _uploads = new();
    private readonly System.Windows.Forms.Timer _timer;
    private string _selected = "数学";
    private string _notice = "课件将按科目自动分类，无需登录，访问密钥由老师分享。";

    public CoursewareView() : base("课件上传")
    {
        AllowDrop = true;
        _timer = new System.Windows.Forms.Timer { Interval = 150 };
        _timer.Tick += (_, _) => TickProgress();
        _timer.Start();

        // 预置演示数据，避免空态白屏（真实数据可由 AddFiles 替换）。
        _uploads.Add(new UploadItem { Name = "数学-二次函数.pptx", Progress = 78 });
        _uploads.Add(new UploadItem { Name = "语文-文言文阅读.pdf", Progress = 100, Done = true });
    }

    // ---------- 数据注入点 ----------
    public Func<IReadOnlyList<string>> SubjectsProvider { get; set; } = () => Array.Empty<string>();
    public Action<string>? SubjectAdded { get; set; }
    public Action<IReadOnlyList<string>, string>? UploadRequested { get; set; }

    public override void OnShow()
    {
        base.OnShow();
        try
        {
            IReadOnlyList<string> extra = SubjectsProvider();
            foreach (string s in extra)
            {
                if (!_subjects.Contains(s))
                {
                    _subjects.Add(s);
                }
            }
        }
        catch
        {
            // 保持默认科目。
        }

        Invalidate();
    }

    /// <summary>外部添加待上传文件。</summary>
    public void AddFiles(IEnumerable<string> files)
    {
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            if (_uploads.Any(u => u.Name == name))
            {
                continue;
            }

            _uploads.Add(new UploadItem { Name = name });
        }

        _notice = $"已添加 {_uploads.Count} 个文件，目标科目：{_selected}";
        Invalidate();
    }

    /// <summary>外部更新真实上传进度。</summary>
    public void SetProgress(string name, double percent, bool done)
    {
        UploadItem? item = _uploads.FirstOrDefault(u => u.Name == name);
        if (item is not null)
        {
            item.Progress = Math.Clamp(percent, 0, 100);
            item.Done = done || item.Progress >= 99.5;
            Invalidate();
        }
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        base.OnDragEnter(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddFiles(files);
        }

        base.OnDragDrop(e);
    }

    private void TickProgress()
    {
        bool changed = false;
        foreach (UploadItem item in _uploads)
        {
            if (!item.Done && item.Progress < 100)
            {
                item.Progress = Math.Min(100, item.Progress + 3);
                if (item.Progress >= 100)
                {
                    item.Done = true;
                }

                changed = true;
            }
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();

        // 拖拽框
        Rectangle drop = new(20, 16, rect.Width - 40, 124);
        bool dropHover = IsHovered(drop);
        Painter.DashedRoundRect(g, drop, Theme.Radius, dropHover ? Theme.Accent : Theme.Border, 1.4f);
        if (dropHover)
        {
            Painter.RoundRect(g, drop, Theme.Radius, Color.FromArgb(6, Theme.Accent));
        }

        IconSet.CloudUpload(g, new Rectangle(rect.Width / 2 - 28, drop.Y + 16, 56, 50), Theme.Primary);
        Painter.Text(g, "拖拽课件到此处，或点击上传",
            new RectangleF(drop.X, drop.Y + 68, drop.Width, 28), Theme.Font(15f, FontStyle.Bold),
            Theme.Text, ContentAlignment.MiddleCenter);
        Painter.Text(g, "支持 PDF、PPT、Word、图片，大小不限",
            new RectangleF(drop.X, drop.Y + 94, drop.Width, 22), Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);
        AddHit(drop, PickFiles, () => Invalidate());

        // 选择科目
        Painter.Text(g, "选择科目", new RectangleF(20, 152, 140, 28), Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        int x = 20;
        int chipY = 188;
        foreach (string subject in _subjects)
        {
            int w = (int)Painter.MeasureText(g, subject, Theme.SubFont) + 26;
            if (x + w > rect.Width - 130)
            {
                break;
            }

            Rectangle chip = new(x, chipY, w, 32);
            bool sel = subject == _selected;
            if (sel)
            {
                Painter.Pill(g, chip, subject, Theme.SubFont, Theme.Primary, Color.White);
            }
            else
            {
                Painter.OutlinePill(g, chip, subject, Theme.SubFont, Theme.Border, Theme.Text2);
            }

            string captured = subject;
            AddHit(chip, () => { _selected = captured; Invalidate(); }, () => Invalidate());
            x += w + 10;
        }

        int newW = 108;
        Rectangle newBtn = new(rect.Right - 20 - newW, chipY, newW, 32);
        bool nh = IsHovered(newBtn);
        Painter.RoundRect(g, newBtn, Theme.RadiusSm, nh ? Theme.Soft : Theme.Card, Theme.Primary, 1f);
        IconSet.Plus(g, new Rectangle(newBtn.X + 8, newBtn.Y + 9, 14, 14), Theme.Primary, 1.6f);
        Painter.Text(g, "新建科目", new RectangleF(newBtn.X + 24, newBtn.Y, newBtn.Width - 26, newBtn.Height),
            Theme.TinyFont, Theme.Primary, ContentAlignment.MiddleLeft);
        AddHit(newBtn, () =>
        {
            string? name = InputDialog.Ask("新建科目", "请输入科目名称");
            if (!string.IsNullOrEmpty(name) && !_subjects.Contains(name))
            {
                _subjects.Add(name);
                _selected = name;
                SubjectAdded?.Invoke(name);
                Invalidate();
            }
        }, () => Invalidate());

        // 上传列表
        Painter.Text(g, "上传列表", new RectangleF(20, 238, 140, 28), Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);
        int rowY = 274;
        int rowH = 46;
        int maxRows = Math.Max(1, Math.Min(_uploads.Count, (rect.Height - 360) / rowH));
        for (int i = 0; i < maxRows; i++)
        {
            UploadItem item = _uploads[i];
            Rectangle row = new(20, rowY + i * rowH, rect.Width - 40, rowH - 6);
            Painter.RoundRect(g, row, 9, Color.FromArgb(0xF8, 0xFA, 0xFC), Theme.Border);
            IconSet.Document(g, new Rectangle(row.X + 12, row.Y + 10, 24, 24), Theme.Text2);

            Rectangle bar = new(row.X + 220, row.Y + 19, Math.Max(80, row.Width - 360), 9);
            Painter.RoundRect(g, bar, 4, Theme.Border);
            int fillW = (int)(bar.Width * item.Progress / 100.0);
            if (fillW > 0)
            {
                Painter.GradientRoundRect(g, new Rectangle(bar.X, bar.Y, fillW, bar.Height), 4,
                    Theme.Primary, Theme.Accent, 0f);
            }

            Painter.Text(g, item.Name, new RectangleF(row.X + 46, row.Y, 168, row.Height),
                Theme.SubFont, Theme.Text, ContentAlignment.MiddleLeft);

            if (item.Done)
            {
                Painter.Dot(g, row.Right - 72, row.Y + row.Height / 2f, 8f, Theme.Green);
                IconSet.Check(g, new Rectangle(row.Right - 78, row.Y + row.Height / 2 - 6, 12, 12), Color.White, 1.6f);
                Painter.Text(g, "已完成", new RectangleF(row.Right - 62, row.Y, 58, row.Height),
                    Theme.TinyFont, Theme.Green, ContentAlignment.MiddleLeft);
            }
            else
            {
                Painter.Text(g, $"{(int)item.Progress}%", new RectangleF(row.Right - 64, row.Y, 52, row.Height),
                    Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleRight);
            }
        }

        // 底部提示 + 开始上传
        Painter.Text(g, _notice, new RectangleF(20, rect.Bottom - 84, rect.Width - 40, 22),
            Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);
        Rectangle uploadBtn = new(rect.Width / 2 - 100, rect.Bottom - 56, 200, 44);
        bool uh = IsHovered(uploadBtn);
        Painter.GradientRoundRect(g, uploadBtn, Theme.Radius,
            uh ? Theme.PrimaryLight : Theme.Primary, uh ? Theme.Accent : Theme.PrimaryLight, 90f);
        Painter.Text(g, "开始上传", uploadBtn, Theme.Font(15f, FontStyle.Bold), Color.White, ContentAlignment.MiddleCenter);
        AddHit(uploadBtn, () =>
        {
            if (_uploads.Count == 0)
            {
                _notice = "请先选择要上传的课件文件";
                Invalidate();
                return;
            }

            UploadRequested?.Invoke(_uploads.Select(u => u.Name).ToList(), _selected);
            _notice = $"已开始上传 {_uploads.Count} 个文件到「{_selected}」";
            Invalidate();
        }, () => Invalidate());
    }

    private void PickFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "选择课件文件",
            Filter = "课件文件|*.pdf;*.ppt;*.pptx;*.doc;*.docx;*.png;*.jpg;*.jpeg|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddFiles(dialog.FileNames);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
