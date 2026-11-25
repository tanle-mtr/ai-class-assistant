using System.Drawing;
using System.Drawing.Drawing2D;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>
/// 分贝面积图：渐变填充 + 折线 + 数据点 + 峰值橙点 + 平均值虚线 + 坐标轴（效果图 04）。
/// </summary>
public sealed class AreaChart : Canvas
{
    private IReadOnlyList<double> _samples = Array.Empty<double>();

    public IReadOnlyList<double> Samples
    {
        get => _samples;
        set
        {
            _samples = value ?? Array.Empty<double>();
            Invalidate();
        }
    }

    public double MaxDb { get; set; } = 80.0;
    public double AvgDb { get; set; }
    public double PeakDb { get; set; }

    /// <summary>是否在右上绘制“平均/峰值”图例。</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>x 轴单位文字。</summary>
    public string XUnit { get; set; } = "分钟";

    public void SetData(IReadOnlyList<double> samples, double avg, double peak)
    {
        _samples = samples ?? Array.Empty<double>();
        AvgDb = avg;
        PeakDb = peak;
        Invalidate();
    }

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        const int leftPad = 46, rightPad = 18, topPad = 26, bottomPad = 28;
        int px = rect.X + leftPad;
        int py = rect.Y + topPad;
        int pw = rect.Width - leftPad - rightPad;
        int ph = rect.Height - topPad - bottomPad;
        if (pw < 30 || ph < 30)
        {
            return;
        }

        int baseline = py + ph;

        // 横向网格 + y 轴刻度
        using (var grid = new Pen(Theme.Border, 1f) { DashStyle = DashStyle.Dash })
        {
            for (int db = 0; db <= (int)MaxDb; db += 20)
            {
                float y = baseline - ph * (db / (float)MaxDb);
                g.DrawLine(grid, px, y, px + pw, y);
                Painter.Text(g, db.ToString(), new RectangleF(rect.X, y - 9, leftPad - 10, 18),
                    Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleRight);
            }
        }
        Painter.TextAt(g, "dB", rect.X + 4, rect.Y + 4, Theme.TinyFont, Theme.Text2);

        // x 轴刻度
        int n = _samples.Count;
        int ticks = 5;
        using var tickPen = new Pen(Theme.Border, 1f);
        for (int i = 0; i <= ticks; i++)
        {
            float x = px + pw * i / (float)ticks;
            g.DrawLine(tickPen, x, baseline, x, baseline + 4);
            Painter.Text(g, (i * 10).ToString(), new RectangleF(x - 18, baseline + 5, 36, 16),
                Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleCenter);
        }
        Painter.Text(g, XUnit, new RectangleF(px + pw - 30, baseline + 5, 30, 16),
            Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleRight);

        // 平均值虚线
        double avg = AvgDb > 0 ? AvgDb : Average(_samples);
        if (avg > 0)
        {
            float ay = baseline - ph * (float)Math.Clamp(avg / MaxDb, 0, 1);
            using var avgPen = new Pen(Theme.Accent, 1.2f) { DashStyle = DashStyle.Dash };
            g.DrawLine(avgPen, px, ay, px + pw, ay);
        }

        if (n < 1)
        {
            Painter.Text(g, "暂无分贝数据", new RectangleF(px, py, pw, ph), Theme.SubFont, Theme.Text2, ContentAlignment.MiddleCenter);
            return;
        }

        PointF[] points = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float x = n == 1 ? px + pw / 2f : px + pw * i / (float)(n - 1);
            double db = Math.Clamp(_samples[i], 0, MaxDb);
            float y = baseline - ph * (float)(db / MaxDb);
            points[i] = new PointF(x, y);
        }

        // 面积渐变填充
        using (var areaPath = new GraphicsPath())
        {
            areaPath.AddLine(points[0], new PointF(points[0].X, baseline));
            for (int i = 0; i < n - 1; i++)
            {
                areaPath.AddLine(points[i], points[i + 1]);
            }

            areaPath.AddLine(points[n - 1], new PointF(points[n - 1].X, baseline));
            areaPath.CloseFigure();
            using var fill = new LinearGradientBrush(new Rectangle(px, py, Math.Max(1, pw), Math.Max(1, ph)),
                Color.FromArgb(110, Theme.AccentLight), Color.FromArgb(12, Theme.Accent), 90f);
            g.FillPath(fill, areaPath);
        }

        // 折线
        using (var curve = new Pen(Theme.Primary, 2f) { LineJoin = LineJoin.Round })
        {
            g.DrawLines(curve, points);
        }

        // 数据点 + 峰值橙点
        int peakIndex = PeakIndex(_samples);
        for (int i = 0; i < n; i++)
        {
            bool isPeak = i == peakIndex;
            float d = isPeak ? 7f : 4.5f;
            Color c = isPeak ? Theme.Amber : Theme.Primary;
            using var brush = new SolidBrush(c);
            g.FillEllipse(brush, points[i].X - d / 2f, points[i].Y - d / 2f, d, d);
            using var halo = new Pen(Color.White, 1.4f);
            g.DrawEllipse(halo, points[i].X - d / 2f, points[i].Y - d / 2f, d, d);
        }

        // 右上图例
        if (ShowLegend)
        {
            double peak = PeakDb > 0 ? PeakDb : (n > 0 ? _samples[peakIndex] : 0);
            Painter.Text(g, $"平均 {avg:0} dB  •  峰值 {peak:0} dB",
                new RectangleF(px + pw - 210, rect.Y + 2, 206, 20),
                Theme.SubFont, Theme.Text2, ContentAlignment.MiddleRight);
        }
    }

    private static double Average(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (double v in values)
        {
            sum += v;
        }

        return sum / values.Count;
    }

    private static int PeakIndex(IReadOnlyList<double> values)
    {
        int idx = 0;
        double best = double.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > best)
            {
                best = values[i];
                idx = i;
            }
        }

        return idx;
    }
}
