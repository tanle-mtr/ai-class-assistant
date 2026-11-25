using System.Drawing;
using System.Windows.Forms;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Rendering;

/// <summary>
/// 双缓冲自绘画布基类：维护命中区域列表，统一处理 hover 高亮与点击分发。
/// 所有视图/控件都继承它，从而天然具备抗锯齿、防闪烁与交互能力。
/// 子类在 Draw 开头调用 ClearHits() 再按当前尺寸 AddHit()，即可实现自适应命中区域。
/// </summary>
public abstract class Canvas : Control
{
    private readonly List<HitRegion> _hits = new();
    private Rectangle? _hoverRect;

    protected Canvas()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Theme.Card;
    }

    /// <summary>命中区域：矩形 + 点击回调 + 可选悬停回调。</summary>
    public sealed class HitRegion
    {
        public HitRegion(Rectangle rect, Action onClick, Action? onHover)
        {
            Rect = rect;
            OnClick = onClick;
            OnHover = onHover;
        }

        public Rectangle Rect { get; }
        public Action OnClick { get; }
        public Action? OnHover { get; }
        public bool Contains(Point p) => Rect.Contains(p);
    }

    /// <summary>注册命中区域（无 hover 回调）。后注册的优先命中。</summary>
    public void AddHit(Rectangle rect, Action onClick) => AddHit(rect, onClick, null);

    /// <summary>注册命中区域（含 hover 回调）。</summary>
    public void AddHit(Rectangle rect, Action onClick, Action? onHover)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        _hits.Add(new HitRegion(rect, onClick, onHover));
    }

    /// <summary>清空命中区域列表（不改悬停态，便于重绘时按新尺寸重建）。</summary>
    public void ClearHits() => _hits.Clear();

    /// <summary>当前悬停区域（按矩形比较，重绘重建后仍保持）。</summary>
    protected Rectangle? HoveredRect => _hoverRect;

    /// <summary>判断指定矩形是否正处于悬停高亮（矩形需与 AddHit 时一致）。</summary>
    public bool IsHovered(Rectangle rect) => _hoverRect.HasValue && _hoverRect.Value == rect;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        HitRegion? found = null;
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            if (_hits[i].Contains(e.Location))
            {
                found = _hits[i];
                break;
            }
        }

        Rectangle? next = found?.Rect;
        if (_hoverRect != next)
        {
            _hoverRect = next;
            found?.OnHover?.Invoke();
            Invalidate();
        }

        Cursor = found is null ? Cursors.Default : Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Contains(e.Location))
                {
                    _hits[i].OnClick.Invoke();
                    break;
                }
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_hoverRect.HasValue)
        {
            _hoverRect = null;
            Invalidate();
        }

        Cursor = Cursors.Default;
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Painter.EnableAntialias(e.Graphics);
        Draw(e.Graphics, ClientRectangle);
    }

    /// <summary>子类实现全部自绘逻辑；建议开头先 ClearHits() 再 AddHit()。</summary>
    protected abstract void Draw(Graphics g, Rectangle rect);
}
