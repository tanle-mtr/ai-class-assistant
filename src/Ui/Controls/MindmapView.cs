using System.Drawing;
using System.Drawing.Drawing2D;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>
/// GDI+ 思维导图：解析 Markdown 缩进树 → 贝塞尔曲线连线 + 圆角 pill 节点，支持横向滚动。
/// 不依赖 Markmap / CDN / 浏览器。
/// </summary>
public sealed class MindmapView : Canvas
{
    private sealed class Node
    {
        public string Text = "";
        public int Depth;
        public readonly List<Node> Children = new();
    }

    private sealed class Laid
    {
        public Node Node = new();
        public int Parent = -1;
        public int X, Y, W, H;
    }

    private string _markdown = "";
    private readonly List<Laid> _laid = new();
    private int _scrollX;

    /// <summary>Markdown 缩进树（每行如 “- 二次函数” / “  - 定义”）。</summary>
    public string Markdown
    {
        get => _markdown;
        set
        {
            _markdown = value ?? "";
            Relayout();
            Invalidate();
        }
    }

    /// <summary>横向滚动偏移（像素）。</summary>
    public int ScrollX
    {
        get => _scrollX;
        set
        {
            _scrollX = Math.Max(0, value);
            Invalidate();
        }
    }

    public void SetMarkdown(string markdown)
    {
        Markdown = markdown;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        int contentW = ContentWidth();
        int max = Math.Max(0, contentW - Width + 20);
        _scrollX = Math.Clamp(_scrollX - e.Delta, 0, max);
        Invalidate();
        base.OnMouseWheel(e);
    }

    private int ContentWidth()
    {
        int max = 0;
        foreach (Laid l in _laid)
        {
            max = Math.Max(max, l.X + l.W);
        }

        return max + 30;
    }

    private void Relayout()
    {
        _laid.Clear();
        Node? root = Parse(_markdown);
        if (root is null)
        {
            return;
        }

        int leaf = 0;
        LayoutNode(root, 0, -1, ref leaf);

        // 让根节点整体垂直居中由绘制阶段处理，这里仅保留相对坐标。
    }

    private static Node? Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var root = new Node { Text = "思维导图" };
        // stack[d] 表示当前深度 d 的最近节点；stack[0] 固定为根。
        var stack = new List<Node> { root };
        using var reader = new StringReader(markdown);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int indent = line.Length - line.TrimStart().Length;
            int depth = indent / 2;
            string text = trimmed.TrimStart('-', '*', ' ').Trim();
            if (text.Length == 0)
            {
                continue;
            }

            var node = new Node { Text = text, Depth = depth + 1 };
            int parentDepth = Math.Min(depth, stack.Count - 1);
            stack[parentDepth].Children.Add(node);
            if (depth + 1 < stack.Count)
            {
                stack[depth + 1] = node;
            }
            else
            {
                stack.Add(node);
            }
        }

        if (root.Children.Count == 1)
        {
            return root.Children[0];
        }

        return root.Children.Count == 0 ? null : root;
    }

    private void LayoutNode(Node node, int depth, int parentIndex, ref int leaf)
    {
        int idx = _laid.Count;
        var laid = new Laid { Node = node, Parent = parentIndex, X = depth * 176, H = 26 };
        _laid.Add(laid);

        if (node.Children.Count == 0)
        {
            laid.Y = leaf * 38;
            leaf++;
        }
        else
        {
            var childIndices = new List<int>();
            foreach (Node child in node.Children)
            {
                LayoutNode(child, depth + 1, idx, ref leaf);
                childIndices.Add(_laid.Count - 1);
            }

            float avg = 0;
            foreach (int ci in childIndices)
            {
                avg += _laid[ci].Y;
            }

            laid.Y = (int)(avg / childIndices.Count);
        }

        using var bmp = new Bitmap(1, 1);
        using Graphics g = Graphics.FromImage(bmp);
        laid.W = (int)Math.Clamp(Painter.MeasureText(g, node.Text, Theme.SubFont) + 20, 56, 150);
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        if (_laid.Count == 0)
        {
            Painter.Text(g, "暂无思维导图", rect, Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (Laid l in _laid)
        {
            minY = Math.Min(minY, l.Y);
            maxY = Math.Max(maxY, l.Y);
        }

        int offsetY = rect.Y + (rect.Height - (maxY - minY + 26)) / 2 - minY;

        g.TranslateTransform(-_scrollX, offsetY);

        // 连线（三次贝塞尔）
        using (var link = new Pen(Theme.SoftBorder, 1.5f))
        {
            for (int i = 0; i < _laid.Count; i++)
            {
                Laid child = _laid[i];
                if (child.Parent < 0)
                {
                    continue;
                }

                Laid parent = _laid[child.Parent];
                PointF p1 = new(parent.X + parent.W, parent.Y + parent.H / 2f);
                PointF p4 = new(child.X, child.Y + child.H / 2f);
                float mid = (p1.X + p4.X) / 2f;
                g.DrawBezier(link, p1, new PointF(mid, p1.Y), new PointF(mid, p4.Y), p4);
            }
        }

        // 节点 pill
        for (int i = 0; i < _laid.Count; i++)
        {
            Laid l = _laid[i];
            Rectangle nodeRect = new(l.X, l.Y, l.W, l.H);
            if (l.Node.Depth == 0)
            {
                Painter.Pill(g, nodeRect, l.Node.Text, Theme.SubFont, Theme.Primary, Color.White);
            }
            else if (l.Node.Depth == 1)
            {
                Painter.Pill(g, nodeRect, l.Node.Text, Theme.SubFont, Theme.Soft, Theme.Primary, Theme.SoftBorder);
            }
            else
            {
                Painter.OutlinePill(g, nodeRect, l.Node.Text, Theme.TinyFont, Theme.Border, Theme.Text2, Theme.Card);
            }
        }

        g.ResetTransform();

        // 右侧渐隐提示可滚动
        if (ContentWidth() > rect.Width)
        {
            using var fade = new LinearGradientBrush(
                new Rectangle(rect.Right - 24, rect.Y, 24, rect.Height),
                Color.FromArgb(0, Theme.Card), Color.FromArgb(220, Theme.Card), 0f);
            g.FillRectangle(fade, rect.Right - 24, rect.Y, 24, rect.Height);
            Painter.Text(g, "›", new RectangleF(rect.Right - 20, rect.Bottom - 24, 18, 20),
                Theme.BodyFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
    }
}
