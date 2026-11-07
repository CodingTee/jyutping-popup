using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>
/// 自包含端到端测试：自建窗口放一段中文并选中，先直接触发一次捕获，
/// 再往自己的窗口里注入一次真实左键单击（走完整钩子链路），各自截图留证。
/// 不打开任何外部程序，不影响用户桌面。
/// </summary>
public static class SelfTestUi
{
    private const string Sample = "我学紧粤语，好耐了。";

    private static Window _host = null!;
    private static System.Windows.Controls.TextBox _box = null!;
    private static TextBlock _filler = null!;
    private static PopupWindow _popup = null!;
    private static CaptureService _capture = null!;
    private static InputHooks _hooks = null!;
    private static readonly List<string> Log = new();

    public static void Run()
    {
        var baseDir = AppContext.BaseDirectory;
        _popup = new PopupWindow();
        _popup.Show();
        _popup.HidePopup();

        _capture = new CaptureService();
        _capture.Captured += OnCaptured;

        _hooks = new InputHooks();
        _hooks.LeftButtonUp += (x, y) => _capture.Request(x, y, 0);
        _hooks.EscapePressed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _popup.HidePopup());

        _box = new System.Windows.Controls.TextBox
        {
            Text = Sample,
            FontSize = 16,
            Margin = new Thickness(10),
        };
        _filler = new TextBlock
        {
            Text = "（测试注入的鼠标点击落在这里，不会碰到别的程序）",
            FontSize = 11,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(10, 0, 10, 10),
            Focusable = false,
        };
        var stack = new StackPanel();
        stack.Children.Add(_box);
        stack.Children.Add(_filler);
        _host = new Window
        {
            Title = "粤拼自检",
            Width = 460,
            Height = 170,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 180,
            Top = 160,
            Topmost = true,
            Content = stack,
        };
        _host.Show();
        _host.Activate();
        _box.Focus();
        _box.Select(6, 1); // 单选「好」 -> 多音字，验证标红与底部提示

        // 第一步：直接触发一次捕获（不经过鼠标钩子）
        _capture.Request(500, 420, 0);

        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        timer.Tick += (_, _) =>
        {
            step++;
            if (step == 1)
            {
                Shot(Path.Combine(baseDir, "selftest_popup1.png"));
                _popup.HidePopup();

                // 第二步：真实点击走完整链路（钩子 -> 捕获 -> 弹窗）
                var point = _filler.PointToScreen(new System.Windows.Point(40, 14));
                _host.Activate();
                _box.Focus();
                Native.SendLeftClick((int)point.X, (int)point.Y);
            }
            else if (step == 2)
            {
                Shot(Path.Combine(baseDir, "selftest_popup2.png"));
                File.WriteAllLines(Path.Combine(baseDir, "selftest_ui.txt"), Log);
                timer.Stop();
                _hooks.Dispose();
                _capture.Dispose();
                System.Windows.Application.Current.Shutdown();
            }
        };
        timer.Start();
    }

    // 自测里不推进世代号（恒为 0），所以不会丢弃任何结果，行为与改动前一致
    private static void OnCaptured(int x, int y, string text, string source, long epoch)
    {
        Log.Add($"captured\tsource={source}\ttext={text}");
        var items = Jyutping.Annotate(text);
        Log.Add("annotated\t" + string.Join(";", items.Select(i => i.Char + ":" + i.Reading)));
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _popup.Render(items, "");
            _popup.ShowAt(x, y);
        });
    }

    private static void Shot(string path)
    {
        try
        {
            var width = (int)Math.Ceiling(_popup.ActualWidth);
            var height = (int)Math.Ceiling(_popup.ActualHeight);
            Log.Add($"shot\t{Path.GetFileName(path)}\t{width}x{height}\tleft={_popup.Left:F0} top={_popup.Top:F0}");
            if (width <= 0 || height <= 0) return;
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_popup);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            Log.Add("shot_error\t" + ex.Message);
        }
    }
}
