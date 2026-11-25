using System.Drawing;
using System.Windows.Forms;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>
/// 自绘输入框：视觉完全由 GDI+ 绘制（圆角底 + 描边 + 占位符 + 光标）。
/// 内部用一个不可见的原生 TextBox 代理键盘与中文输入法，仅作输入源，不显示任何原生外观。
/// </summary>
public sealed class InputField : Canvas
{
    private readonly TextBox _proxy;
    private string _value = "";

    public InputField()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        _proxy = new TextBox
        {
            Location = new Point(-2000, -2000),
            Size = new Size(1, 1),
            BorderStyle = BorderStyle.None,
            TabStop = false
        };
        _proxy.TextChanged += (_, _) =>
        {
            _value = _proxy.Text;
            ValueChanged?.Invoke(_value);
            Invalidate();
        };
        Controls.Add(_proxy);
    }

    /// <summary>输入内容。</summary>
    public string Value
    {
        get => _value;
        set
        {
            _value = value ?? "";
            if (_proxy.Text != _value)
            {
                _proxy.Text = _value;
            }

            Invalidate();
        }
    }

    /// <summary>占位提示文字。</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>是否密码模式。</summary>
    public bool Password
    {
        get => _proxy.UseSystemPasswordChar;
        set => _proxy.UseSystemPasswordChar = value;
    }

    /// <summary>内容变更回调。</summary>
    public Action<string>? ValueChanged { get; set; }

    /// <summary>回车确认回调。</summary>
    public Action? Confirmed { get; set; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _proxy.Focus();
        base.OnMouseDown(e);
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        bool focused = _proxy.Focused;
        Painter.RoundRect(g, rect, Theme.RadiusSm, Theme.Card,
            focused ? Theme.Accent : Theme.Border, focused ? 1.6f : 1f);

        AddHit(rect, () => _proxy.Focus());

        string shown = Password ? new string('●', _value.Length) : _value;
        RectangleF tr = new(rect.X + 10, rect.Y, rect.Width - 20, rect.Height);
        if (shown.Length > 0)
        {
            Painter.Text(g, shown, tr, Theme.BodyFont, Theme.Text, ContentAlignment.MiddleLeft);
        }
        else
        {
            Painter.Text(g, Placeholder, tr, Theme.BodyFont, Theme.Muted, ContentAlignment.MiddleLeft);
        }

        if (focused && shown.Length > 0)
        {
            float w = Painter.MeasureText(g, shown, Theme.BodyFont);
            float cx = rect.X + 10 + Math.Min(w, rect.Width - 20) + 2;
            if (cx < rect.Right - 6)
            {
                using var pen = new Pen(Theme.Primary, 1.4f);
                g.DrawLine(pen, cx, rect.Y + 6, cx, rect.Bottom - 6);
            }
        }
    }

    /// <summary>由外部在代理输入框上绑定回车确认（供对话框调用）。</summary>
    public void BindConfirm(Action action)
    {
        Confirmed = action;
        _proxy.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                action();
                e.SuppressKeyPress = true;
            }
        };
    }
}
