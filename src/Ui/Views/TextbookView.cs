using System.Drawing;
using System.Windows.Forms;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Shell;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>教科书视图：教材清单 + PDF 导入 + 本地知识库问答。</summary>
public sealed class TextbookView : ViewBase
{
    private readonly InputField _question = new() { Placeholder = "输入教材相关问题，例如：二次函数的顶点公式？" };
    private IReadOnlyList<string> _books = Array.Empty<string>();
    private string _answer = "请输入问题后点击「提问」，答案将基于本地教材知识库生成。";
    private bool _busy;

    public TextbookView() : base("教科书")
    {
        Controls.Add(_question);
        _question.BindConfirm(() => Ask());
    }

    // ---------- 数据注入点 ----------
    public Func<IReadOnlyList<string>> BooksProvider { get; set; } = () => Array.Empty<string>();
    public Action? ImportPdfRequested { get; set; }
    public Func<string, Task<string>>? AskProvider { get; set; }

    public override void OnShow()
    {
        base.OnShow();
        try
        {
            _books = BooksProvider() ?? Array.Empty<string>();
        }
        catch
        {
            _books = Array.Empty<string>();
        }

        Invalidate();
    }

    private async void Ask()
    {
        string q = _question.Value.Trim();
        if (q.Length == 0 || _busy)
        {
            return;
        }

        _busy = true;
        _answer = "正在检索教材知识库…";
        Invalidate();
        try
        {
            _answer = AskProvider is null
                ? "AI 服务不可用，请检查 Ollama 是否已启动。"
                : await AskProvider(q);
            if (string.IsNullOrWhiteSpace(_answer))
            {
                _answer = "未找到相关内容。";
            }
        }
        catch
        {
            _answer = "AI 服务不可用，请检查 Ollama 是否已启动。";
        }
        finally
        {
            _busy = false;
            Invalidate();
        }
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        Painter.Text(g, "教科书", new RectangleF(20, 16, 160, 32), Theme.TitleFont, Theme.Text, ContentAlignment.MiddleLeft);

        // 书单卡
        Rectangle left = new(20, 62, 300, Math.Max(180, rect.Bottom - 82));
        Painter.Shadow(g, left, Theme.Radius, 3, 2);
        Painter.RoundRect(g, left, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "教材清单", new RectangleF(left.X + 16, left.Y + 10, 180, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        if (_books.Count == 0)
        {
            Painter.Text(g, "暂无教材，请导入 PDF", new RectangleF(left.X + 16, left.Y + 60, left.Width - 32, 80),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
        else
        {
            int y = left.Y + 48;
            foreach (string book in _books.Take(8))
            {
                if (y > left.Bottom - 70)
                {
                    break;
                }

                Rectangle row = new(left.X + 12, y, left.Width - 24, 34);
                Painter.RoundRect(g, row, 8, Color.FromArgb(0xF8, 0xFA, 0xFC));
                IconSet.Book(g, new Rectangle(row.X + 8, row.Y + 6, 22, 22), Theme.Primary);
                Painter.Text(g, book, new RectangleF(row.X + 38, row.Y, row.Width - 46, row.Height),
                    Theme.SubFont, Theme.Text, ContentAlignment.MiddleLeft);
                y += 40;
            }
        }

        Rectangle importBtn = new(left.X + 16, left.Bottom - 52, left.Width - 32, 38);
        bool ih = IsHovered(importBtn);
        Painter.RoundRect(g, importBtn, Theme.RadiusSm, ih ? Theme.Soft : Theme.Card, Theme.Primary, 1.2f);
        IconSet.Plus(g, new Rectangle(importBtn.X + 12, importBtn.Y + 11, 16, 16), Theme.Primary);
        Painter.Text(g, "导入 PDF 教材", new RectangleF(importBtn.X + 36, importBtn.Y, importBtn.Width - 40, importBtn.Height),
            Theme.BodyFont, Theme.Primary, ContentAlignment.MiddleLeft);
        AddHit(importBtn, () => ImportPdfRequested?.Invoke(), () => Invalidate());

        // 问答卡
        Rectangle right = new(336, 62, Math.Max(200, rect.Width - 356), Math.Max(180, rect.Bottom - 82));
        Painter.Shadow(g, right, Theme.Radius, 3, 2);
        Painter.RoundRect(g, right, Theme.Radius, Theme.Card, Theme.Border);
        Painter.Text(g, "教材问答", new RectangleF(right.X + 16, right.Y + 10, 180, 28),
            Theme.SectionFont, Theme.Text, ContentAlignment.MiddleLeft);

        Rectangle askBtn = new(right.Right - 104, right.Y + 50, 84, 36);
        bool ah = IsHovered(askBtn);
        Painter.GradientRoundRect(g, askBtn, Theme.RadiusSm, Theme.Primary, Theme.PrimaryLight, 90f);
        Painter.Text(g, "提问", askBtn, Theme.BodyFont, Color.White, ContentAlignment.MiddleCenter);
        AddHit(askBtn, Ask, () => Invalidate());

        Rectangle answerBox = new(right.X + 16, right.Y + 104, right.Width - 32, right.Height - 120);
        Painter.RoundRect(g, answerBox, Theme.RadiusSm, Color.FromArgb(0xF8, 0xFA, 0xFC), Theme.Border);

        using var brush = new SolidBrush(Theme.Text2);
        using var sf = new StringFormat();
        g.DrawString(_answer, Theme.BodyFont, brush, new RectangleF(answerBox.X + 14, answerBox.Y + 12, answerBox.Width - 28, answerBox.Height - 24), sf);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        Rectangle r = ClientRectangle;
        _question.Bounds = new Rectangle(356, 112, Math.Max(120, r.Width - 500), 36);
    }
}
