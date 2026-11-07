using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms; // Screen：取选区所在屏幕的工作区做夹取

namespace JyutPingPopup;

/// <summary>置顶小卡片：每个字上面汉字、下面粤拼，底部列出多音字。</summary>
public partial class PopupWindow : Window
{
    public PopupWindow()
    {
        InitializeComponent();
        // 窗口始终跟随内容自适应。不能手动 Measure + 设 Width/Height：
        // 显式尺寸会把内容测量约束在旧窗口大小里（实测 footer 收起->展开时高度丢失），
        // 隐藏期间布局引擎也不可靠，所以显示后靠两轮 UpdateLayout 让它落地。
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    public void Render(IEnumerable<JyutItem> items, string note)
    {
        Cells.Children.Clear();
        var polyphonic = new List<string>();
        foreach (var item in items)
        {
            // 逐字转香港繁体（与词命中路径一致），避免按整段索引时跳过非汉字导致字形错位
            var trad = S2Hk.Convert(item.Char);

            var cell = new StackPanel
            {
                Margin = new Thickness(5, 2, 5, 4),
                MinWidth = 38, // 保证「jyut6」这类较宽读音不被裁掉
            };
            cell.Children.Add(new TextBlock
            {
                Text = trad,
                FontSize = 17,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });
            var tone = item.All.Count > 1
                ? System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B)
                : System.Windows.Media.Color.FromRgb(0x5A, 0x5A, 0x5A);
            cell.Children.Add(new TextBlock
            {
                Text = item.Reading.Length > 0 ? item.Reading : "—",
                FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new SolidColorBrush(tone),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });
            Cells.Children.Add(cell);
            if (item.All.Count > 1) polyphonic.Add(trad + " " + string.Join("/", item.All));
        }

        var lines = new List<string>();
        if (polyphonic.Count > 0) lines.Add("多音：" + string.Join("   ", polyphonic));
        if (note.Length > 0) lines.Add(note);
        Footer.Text = string.Join("   ", lines);
        Footer.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 自测观察口
    internal string FooterTextForTest => Footer.Text;
    internal Visibility FooterVisibilityForTest => Footer.Visibility;
    internal double FooterActualHeightForTest => Footer.ActualHeight;
    internal string MeasureProbe()
    {
        var border = (System.Windows.Controls.Border)Content;
        return $"borderValid={border.IsMeasureValid} " +
               $"borderDesired={border.DesiredSize.Width:F1}x{border.DesiredSize.Height:F1} " +
               $"footerVis={Footer.Visibility} footerDesired={Footer.DesiredSize.Width:F1}x{Footer.DesiredSize.Height:F1} " +
               $"footerValid={Footer.IsMeasureValid}";
    }


    /// <summary>传入的是鼠标的物理像素坐标，需要换算成 WPF 的设备无关像素。</summary>
    public void ShowAt(int pixelX, int pixelY)
    {
        // 先显示再布局：隐藏期间内容变更不进布局引擎。第一轮 UpdateLayout
        // 完成内容测量，第二轮把 SizeToContent 算出的新尺寸落到窗口上。
        Visibility = Visibility.Visible;
        UpdateLayout();
        UpdateLayout();

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                        ?? Matrix.Identity;
        var point = transform.Transform(new System.Windows.Point(pixelX, pixelY));

        // 用选区所在屏幕的工作区夹取，而非 SystemParameters.WorkArea（那只返回主屏）：
        // 副屏划词时主屏工作区会把卡片夹到主屏右缘，离选区老远。
        var screen = Screen.FromPoint(new System.Drawing.Point(pixelX, pixelY));
        var wa = screen.WorkingArea; // 物理像素矩形
        var tl = transform.Transform(new System.Windows.Point(wa.X, wa.Y));
        var br = transform.Transform(new System.Windows.Point(wa.Right, wa.Bottom));
        var area = new System.Windows.Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
        var left = point.X + 16;
        var top = point.Y + 20;
        if (left + ActualWidth > area.Right) left = Math.Max(area.Left, area.Right - ActualWidth);
        if (top + ActualHeight > area.Bottom) top = Math.Max(area.Top, area.Bottom - ActualHeight);
        Left = Math.Max(area.Left, left);
        Top = Math.Max(area.Top, top);

        Visibility = Visibility.Visible;
    }

    public void HidePopup()
    {
        Visibility = Visibility.Collapsed;
    }
}
