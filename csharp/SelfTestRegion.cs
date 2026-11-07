using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>
/// 诊断用：真的把截图遮罩弹出来，截一张全屏存成 PNG，
/// 用来肉眼确认「按了热键到底看不看得见东西」。
/// </summary>
public static class SelfTestRegion
{
    public static void Run()
    {
        var dir = AppContext.BaseDirectory;
        var lines = new List<string>();

        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
        {
            minX = System.Math.Min(minX, s.Bounds.X); minY = System.Math.Min(minY, s.Bounds.Y);
            maxX = System.Math.Max(maxX, s.Bounds.Right); maxY = System.Math.Max(maxY, s.Bounds.Bottom);
        }

        var w = new RegionSelector();
        w.Show();
        Pump(1000); // 让它真的渲染出来

        lines.Add("IsVisible\t" + w.IsVisible);
        lines.Add("IsLoaded\t" + w.IsLoaded);
        lines.Add("Rect\t" + $"{w.Left:0.##},{w.Top:0.##} {w.Width:0.##}x{w.Height:0.##}");
        lines.Add("RenderSize\t" + $"{w.RenderSize.Width:0.##}x{w.RenderSize.Height:0.##}");

        // 模拟拖了一个框（反射调私有方法，避免为测试改生产代码签名）
        var mi = typeof(RegionSelector).GetMethod("DrawSelection",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        lines.Add("DrawSelectionFound\t" + (mi != null));
        // 鼠标按下时才置 Visible，这里手动补上（XAML 生成的字段是 internal，同程序集可访问）
        w.SelRect.Visibility = System.Windows.Visibility.Visible;
        w.SelImg.Visibility = System.Windows.Visibility.Visible;
        mi?.Invoke(w, new object[] { new System.Drawing.Point(minX + 300, minY + 200),
                                     new System.Drawing.Point(minX + 900, minY + 600) });
        Pump(400);

        using var bmp = new Bitmap(maxX - minX, maxY - minY, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(minX, minY, 0, 0, new Size(maxX - minX, maxY - minY));
        }
        bmp.Save(Path.Combine(dir, "selftest_region.png"), ImageFormat.Png);

        // 另存冻结快照（反射取私有字段），用于离线对比变暗层压暗幅度
        var fs = typeof(RegionSelector).GetField("_screen",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(w) as Bitmap;
        fs?.Save(Path.Combine(dir, "selftest_region_bg.png"), ImageFormat.Png);

        w.Close();
        File.WriteAllLines(Path.Combine(dir, "selftest_region.txt"), lines);
        System.Windows.Application.Current.Shutdown();
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        using var t = new System.Threading.Timer(_ => frame.Continue = false, null, ms, Timeout.Infinite);
        Dispatcher.PushFrame(frame);
    }
}
