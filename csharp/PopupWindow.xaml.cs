using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms; // Screen：取选区所在屏幕的工作区做夹取

namespace JyutPingPopup;

/// <summary>置顶小卡片：每个字上面汉字、下面粤拼，底部列出多音字。</summary>
public partial class PopupWindow : Window
{
    private string _speakText = "";      // 本次要朗读的原文汉字（非粤拼）
    private bool _speakBtnVisible;       // 发音按钮当前是否可见，供点击命中判断用
    // 发音按钮的物理像素矩形，在 UI 线程 ShowAt 时算好。
    // 全局钩子在独立线程调用 IsOnSpeakButton，绝不能在那里碰 WPF 视觉树（会抛跨线程异常并卡住钩子）。
    private double _speakBtnPxX, _speakBtnPxY, _speakBtnPxW, _speakBtnPxH;

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

        // 发音按钮：仅在「设置开启 + 本机有粤语语音」时出现；无语音就完全不显示，自动降级
        _speakText = string.Concat(items.Select(i => i.Char));
        _speakBtnVisible = SpeechSettings.GetEnabled() && SpeechService.HasVoice;
        var vis = _speakBtnVisible ? Visibility.Visible : Visibility.Collapsed;
        SpeakBtn.Visibility = vis;
        SpeakSpacer.Visibility = vis;
    }

    private void SpeakBtn_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // 吃掉这次按下，不让全局左键逻辑把它当成划词
        SpeechService.Speak(_speakText);   // 自动用本机已有的粤语语音
    }

    /// <summary>物理像素坐标是否落在发音按钮矩形内。全局钩子线程调用，只做整数比较，不碰视觉树。</summary>
    public bool IsOnSpeakButton(int pixelX, int pixelY)
    {
        if (!_speakBtnVisible) return false;
        return pixelX >= _speakBtnPxX && pixelX <= _speakBtnPxX + _speakBtnPxW
            && pixelY >= _speakBtnPxY && pixelY <= _speakBtnPxY + _speakBtnPxH;
    }

    // 自测观察口
    internal string FooterTextForTest => Footer.Text;
    internal Visibility FooterVisibilityForTest => Footer.Visibility;
    internal double FooterActualHeightForTest => Footer.ActualHeight;
    internal Visibility SpeakBtnVisibilityForTest => SpeakBtn.Visibility;
    internal string SpeakTextForTest => _speakText;
    internal bool CardHitTestForTest => Card.IsHitTestVisible;
    internal bool SpeakBtnHitTestForTest => SpeakBtn.IsHitTestVisible;
    internal System.Windows.Point SpeakBtnPointForTest() =>
        SpeakBtn.TranslatePoint(new System.Windows.Point(SpeakBtn.ActualWidth / 2, SpeakBtn.ActualHeight / 2), this);
    internal System.Windows.Point CardSafePointForTest() =>
        Card.TranslatePoint(new System.Windows.Point(8, 8), this);
    // 按钮中心的物理屏幕坐标：走 WPF 自己的 PointToScreen，与生产代码的计算互相独立
    internal System.Windows.Point SpeakBtnScreenPointForTest() =>
        SpeakBtn.PointToScreen(new System.Windows.Point(
            SpeakBtn.ActualWidth / 2, SpeakBtn.ActualHeight / 2));
    internal string MeasureProbe()
    {
        var border = Card;
        return $"borderValid={border.IsMeasureValid} " +
               $"borderDesired={border.DesiredSize.Width:F1}x{border.DesiredSize.Height:F1} " +
               $"footerVis={Footer.Visibility} footerDesired={Footer.DesiredSize.Width:F1}x{Footer.DesiredSize.Height:F1} " +
               $"footerValid={Footer.IsMeasureValid}";
    }


    /// <summary>传入的是鼠标的物理像素坐标。全程用物理像素定位，不经过 WPF 的 Left/Top。</summary>
    public void ShowAt(int pixelX, int pixelY)
    {
        // 先显示再布局：隐藏期间内容变更不进布局引擎，SizeToContent 要两轮才落地。
        Visibility = Visibility.Visible;
        UpdateLayout();
        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = Native.EffectiveScaleAt(pixelX, pixelY);
        var wa = Screen.FromPoint(new System.Drawing.Point(pixelX, pixelY)).WorkingArea;
        var flags = Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate;

        // 第一遍用旧 DPI 下的尺寸把窗口挪到目标显示器；跨屏后 WPF 按新 DPI 重排，
        // 尺寸会变，第二遍按新尺寸重新夹取。
        PlaceWindow(hwnd, pixelX, pixelY, wa, scale, flags);
        UpdateLayout();
        UpdateLayout();
        PlaceWindow(hwnd, pixelX, pixelY, wa, scale, flags);

        // 发音按钮的物理矩形：窗口原点取 GetWindowRect（物理像素），按钮偏移按目标 DPI 缩放。
        // 钩子线程只做整数比较，绝不在这里之外碰视觉树。
        if (_speakBtnVisible && Native.GetWindowRect(hwnd, out var rect))
        {
            var btnDip = SpeakBtn.TranslatePoint(new System.Windows.Point(0, 0), this);
            _speakBtnPxX = rect.Left + btnDip.X * scale;
            _speakBtnPxY = rect.Top + btnDip.Y * scale;
            _speakBtnPxW = SpeakBtn.ActualWidth * scale;
            _speakBtnPxH = SpeakBtn.ActualHeight * scale;
        }

        Visibility = Visibility.Visible;
    }

    private static void PlaceWindow(IntPtr hwnd, int pixelX, int pixelY,
        System.Drawing.Rectangle workArea, double scale, uint flags)
    {
        if (!Native.GetWindowRect(hwnd, out var rect)) return;
        var (left, top) = ComputePlacement(pixelX, pixelY,
            rect.Right - rect.Left, rect.Bottom - rect.Top, workArea, scale);
        Native.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0, flags);
    }

    /// <summary>纯计算卡片物理落点，供自测在不同 DPI 缩放下验证。</summary>
    internal static (int Left, int Top) ComputePlacement(int pixelX, int pixelY, int winW, int winH,
        System.Drawing.Rectangle workArea, double scale)
    {
        var desiredX = pixelX + (int)Math.Round(16 * scale);
        var desiredY = pixelY + (int)Math.Round(20 * scale);
        var maxX = workArea.Right - winW;
        var maxY = workArea.Bottom - winH;
        var left = maxX < workArea.Left ? workArea.Left : Math.Clamp(desiredX, workArea.Left, maxX);
        var top = maxY < workArea.Top ? workArea.Top : Math.Clamp(desiredY, workArea.Top, maxY);
        return (left, top);
    }

    public void HidePopup()
    {
        Visibility = Visibility.Collapsed;
    }
}
