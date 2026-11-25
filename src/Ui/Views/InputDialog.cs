using System.Drawing;
using System.Windows.Forms;
using AiClassAssistant.Ui.Controls;
using AiClassAssistant.Ui.Rendering;
using AiClassAssistant.Ui.Theming;

namespace AiClassAssistant.Ui.Views;

/// <summary>完全自绘的简易输入对话框（无原生按钮/输入框外观）。</summary>
public sealed class InputDialog : Form
{
    private readonly InputField _field;
    private readonly DialogButton _ok;
    private readonly DialogButton _cancel;
    private readonly string _prompt;

    private sealed class DialogButton : Canvas
    {
        public string Caption = "";
        public bool Primary;
        public Action? Clicked;

        protected override void Draw(Graphics g, Rectangle rect)
        {
            ClearHits();
            bool hover = IsHovered(rect);
            if (Primary)
            {
                Painter.GradientRoundRect(g, rect, Theme.Radius,
                    hover ? Theme.PrimaryLight : Theme.Primary, Theme.Accent, 90f);
                Painter.Text(g, Caption, rect, Theme.BodyFont, Color.White, ContentAlignment.MiddleCenter);
            }
            else
            {
                Painter.RoundRect(g, rect, Theme.Radius, hover ? Theme.Soft : Theme.Card, Theme.Border);
                Painter.Text(g, Caption, rect, Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleCenter);
            }

            AddHit(rect, () => Clicked?.Invoke(), () => Invalidate());
        }
    }

    public InputDialog(string prompt, string placeholder = "")
    {
        _prompt = prompt;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(400, 200);
        BackColor = Theme.Card;

        _field = new InputField { Location = new Point(24, 78), Size = new Size(352, 42), Placeholder = placeholder };
        _ok = new DialogButton { Caption = "确定", Primary = true, Location = new Point(212, 142), Size = new Size(76, 36) };
        _cancel = new DialogButton { Caption = "取消", Location = new Point(300, 142), Size = new Size(76, 36) };
        _ok.Clicked += () => { DialogResult = DialogResult.OK; Close(); };
        _cancel.Clicked += () => { DialogResult = DialogResult.Cancel; Close(); };
        _field.BindConfirm(() => { DialogResult = DialogResult.OK; Close(); });
        Controls.Add(_field);
        Controls.Add(_ok);
        Controls.Add(_cancel);
    }

    /// <summary>输入内容。</summary>
    public string Result => _field.Value.Trim();

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(Theme.Card))
        {
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        using (var border = new Pen(Theme.Border, 1f))
        {
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }

        Painter.Text(e.Graphics, _prompt, new RectangleF(24, 22, 352, 34),
            Theme.Font(18f, FontStyle.Bold), Theme.Text, ContentAlignment.MiddleLeft);
        base.OnPaint(e);
    }

    /// <summary>弹出对话框；取消或空输入返回 null。</summary>
    public static string? Ask(string prompt, string placeholder = "")
    {
        using var dialog = new InputDialog(prompt, placeholder);
        return dialog.ShowDialog() == DialogResult.OK && dialog.Result.Length > 0 ? dialog.Result : null;
    }
}
