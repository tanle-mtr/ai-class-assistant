using System.Drawing;
using System.Drawing.Drawing2D;
using AiClassAssistant.Ui.Rendering;
using Theme = AiClassAssistant.Ui.Theming.Theme;

namespace AiClassAssistant.Ui.Controls;

/// <summary>分贝柱状波形：以中线为轴对称的青绿渐变柱 + 横向刻度网格（效果图 01 中栏）。</summary>
public sealed class DbWaveform : Canvas
{
    private double[] _samples = Array.Empty<double>();

    /// <summary>分贝采样序列（0~MaxDb）。</summary>
    public double[] Samples
    {
        get => _samples;
        set
        {
            _samples = value ?? Array.Empty<double>();
            Invalidate();
        }
    }

    /// <summary>满量程分贝（默认 80）。</summary>
    public double MaxDb { get; set; } = 80.0;

    protected override void Draw(Graphics g, Rectangle rect)
    {
        ClearHits();
        if (rect.Width < 40 || rect.Height < 40)
        {
            return;
        }

        const int leftPad = 34;
        const int rightPad = 8;
        const int topPad = 10;
        const int bottomPad = 18;
        int plotX = rect.X + leftPad;
        int plotY = rect.Y + topPad;
        int plotW = rect.Width - leftPad - rightPad;
        int plotH = rect.Height - topPad - bottomPad;
        if (plotW <= 0 || plotH <= 0)
        {
            return;
        }

        int mid = plotY + plotH / 2;

        // 横向刻度网格 + 左侧标注
        using (var grid = new Pen(Theme.Border, 1f) { DashStyle = DashStyle.Dash })
        {
            foreach (int db in new[] { 80, 40, 0 })
            {
                float y = plotY + plotH * (1f - db / (float)MaxDb);
                g.DrawLine(grid, plotX, y, plotX + plotW, y);
                Painter.Text(g, db.ToString("00"), new RectangleF(rect.X, y - 8, leftPad - 8, 16),
                    Theme.TinyFont, Theme.Text2, ContentAlignment.MiddleRight);
            }
        }

        // 中轴线
        using (var axis = new Pen(Theme.Muted, 1f))
        {
            g.DrawLine(axis, plotX, mid, plotX + plotW, mid);
        }

        int n = Math.Max(1, _samples.Length);
        double slot = plotW / (double)n;
        float bw = (float)Math.Max(2.0, Math.Min(7.0, slot * 0.58));

        for (int i = 0; i < n; i++)
        {
            double db = Math.Clamp(_samples[i], 0, MaxDb);
            float x = (float)(plotX + i * slot + (slot - bw) / 2.0);
            float h = (float)(db / MaxDb * (plotH / 2.0));
            h = Math.Max(2f, h);

            using var upper = new LinearGradientBrush(new RectangleF(x, mid - h, bw, h),
                Theme.AccentLight, Theme.Primary, 90f);
            g.FillRectangle(upper, x, mid - h, bw, h);

            using var lower = new LinearGradientBrush(new RectangleF(x, mid, bw, h),
                Theme.Primary, Theme.Accent, 90f);
            g.FillRectangle(lower, x, mid, bw, h);
        }
    }

    /// <summary>生成一段平滑的演示波形（后端未就绪时空态占位，不白屏）。</summary>
    public static double[] DemoSamples(int count = 60)
    {
        var result = new double[Math.Max(1, count)];
        var rnd = new Random(20260924);
        for (int i = 0; i < result.Length; i++)
        {
            double env = 26 + 30 * Math.Sin(i / 9.0) * Math.Sin(i / 17.0 + 0.6);
            result[i] = Math.Clamp(env + rnd.NextDouble() * 16, 4, 78);
        }

        return result;
    }
}
