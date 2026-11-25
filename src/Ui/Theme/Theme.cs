using System.Drawing;

namespace AiClassAssistant.Ui.Theming;

/// <summary>
/// 视觉契约常量（架构契约 §5）。全部颜色/字体/尺寸集中于此，GDI+ 绘制统一引用。
/// 效果图标杆：图 01 托盘主面板 / 图 02 数据中心 / 图 03 课件上传 / 图 04 课堂总结。
/// </summary>
public static class Theme
{
    // ---------- 品牌色 ----------
    /// <summary>主色 深青 #0F766E。</summary>
    public static readonly Color Primary = Color.FromArgb(0x0F, 0x76, 0x6E);
    /// <summary>强调色 青绿 #14B8A6。</summary>
    public static readonly Color Accent = Color.FromArgb(0x14, 0xB8, 0xA6);
    /// <summary>浅青底 #E3F6F3。</summary>
    public static readonly Color Soft = Color.FromArgb(0xE3, 0xF6, 0xF3);
    /// <summary>页面背景 #F1F5F9。</summary>
    public static readonly Color Bg = Color.FromArgb(0xF1, 0xF5, 0xF9);
    /// <summary>卡片白 #FFFFFF。</summary>
    public static readonly Color Card = Color.FromArgb(0xFF, 0xFF, 0xFF);
    /// <summary>顶栏深墨绿 #0A4E48。</summary>
    public static readonly Color HeaderBg = Color.FromArgb(0x0A, 0x4E, 0x48);

    // ---------- 文本 / 描边 ----------
    /// <summary>主文本 #0F172A。</summary>
    public static readonly Color Text = Color.FromArgb(0x0F, 0x17, 0x2A);
    /// <summary>次文本 #334155。</summary>
    public static readonly Color Text2 = Color.FromArgb(0x33, 0x41, 0x55);
    /// <summary>描边 #E2E8F0。</summary>
    public static readonly Color Border = Color.FromArgb(0xE2, 0xE8, 0xF0);
    /// <summary>成功绿 #16A34A。</summary>
    public static readonly Color Green = Color.FromArgb(0x16, 0xA3, 0x4A);
    /// <summary>警示橙 #F59E0B。</summary>
    public static readonly Color Amber = Color.FromArgb(0xF5, 0x9E, 0x0B);
    /// <summary>危险红 #EF4444。</summary>
    public static readonly Color Danger = Color.FromArgb(0xEF, 0x44, 0x44);

    // ---------- 派生色（由品牌色过渡而来，供渐变/禁用态使用） ----------
    /// <summary>主色高亮（渐变终点） #0D9488。</summary>
    public static readonly Color PrimaryLight = Color.FromArgb(0x0D, 0x94, 0x88);
    /// <summary>强调色亮端（渐变起点） #2DD4BF。</summary>
    public static readonly Color AccentLight = Color.FromArgb(0x2D, 0xD4, 0xBF);
    /// <summary>浅青描边（高亮气泡用） #99E3D6。</summary>
    public static readonly Color SoftBorder = Color.FromArgb(0x99, 0xE3, 0xD6);
    /// <summary>阴影 rgba(15,23,42,.06) —— alpha≈15。</summary>
    public static readonly Color Shadow = Color.FromArgb(15, 0x0F, 0x17, 0x2A);
    /// <summary>淡分隔线 rgba(15,23,42,.06)。</summary>
    public static readonly Color Divider = Color.FromArgb(12, 0x0F, 0x17, 0x2A);
    /// <summary>禁用灰 #CBD5E1。</summary>
    public static readonly Color Muted = Color.FromArgb(0xCB, 0xD5, 0xE1);
    /// <summary>金色（星级） #F59E0B。</summary>
    public static readonly Color Star = Color.FromArgb(0xF5, 0x9E, 0x0B);
    /// <summary>空心星灰 #D7DEE8。</summary>
    public static readonly Color StarEmpty = Color.FromArgb(0xD7, 0xDE, 0xE8);

    // ---------- 尺寸 ----------
    /// <summary>圆角半径 12px。</summary>
    public const int Radius = 12;
    /// <summary>小圆角半径 8px。</summary>
    public const int RadiusSm = 8;
    /// <summary>胶囊圆角（足够大）。</summary>
    public const int RadiusPill = 999;
    /// <summary>顶栏高度 56px。</summary>
    public const int HeaderHeight = 56;
    /// <summary>状态条高度 44px。</summary>
    public const int StatusHeight = 44;
    /// <summary>主面板尺寸 1080x648。</summary>
    public static readonly Size PanelSize = new(1080, 648);
    /// <summary>控制台尺寸 1200x780。</summary>
    public static readonly Size ConsoleSize = new(1200, 780);

    // ---------- 字体 ----------
    public const string FontFamily = "Microsoft YaHei UI";
    public const string FontFamilyFallback = "Segoe UI";

    /// <summary>按字号/样式创建字体（调用方负责生命周期；控件缓存则长期持有）。</summary>
    public static Font Font(float size, FontStyle style = FontStyle.Regular) => new(FontFamily, size, style);

    /// <summary>常用字体：巨号科目名 42。</summary>
    public static Font SubjectFont => Font(42f, FontStyle.Bold);
    /// <summary>常用字体：视图大标题 26。</summary>
    public static Font TitleFont => Font(26f, FontStyle.Bold);
    /// <summary>常用字体：区块标题 16。</summary>
    public static Font SectionFont => Font(16f, FontStyle.Bold);
    /// <summary>常用字体：正文 13。</summary>
    public static Font BodyFont => Font(13f);
    /// <summary>常用字体：次要 12。</summary>
    public static Font SubFont => Font(12f);
    /// <summary>常用字体：小字 11。</summary>
    public static Font TinyFont => Font(11f);
    /// <summary>常用字体：数字 32。</summary>
    public static Font NumberFont => Font(32f, FontStyle.Bold);
}
