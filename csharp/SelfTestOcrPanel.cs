using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>诊断用：真弹一次 OCR 结果面板并截图，确认删掉冗余「关闭」按钮后布局正常。</summary>
public static class SelfTestOcrPanel
{
    public static void Run()
    {
        var dir = AppContext.BaseDirectory;
        var lines = new List<string>();

        var w = new OcrPanel();
        w.ShowResults("来自 14 个国家的欧洲传统美食六");
        w.Show();
        Pump(800);

        lines.Add("IsVisible\t" + w.IsVisible);
        lines.Add("IsLoaded\t" + w.IsLoaded);
        lines.Add("RawText\t" + w.RawText.Text);
        lines.Add("CellCount\t" + w.Cells.Children.Count);
        lines.Add("ButtonCount\t" + CountButtons(w));

        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        foreach (var s in System.Windows.Forms.Screen.AllScreens)
        {
            minX = System.Math.Min(minX, s.Bounds.X); minY = System.Math.Min(minY, s.Bounds.Y);
            maxX = System.Math.Max(maxX, s.Bounds.Right); maxY = System.Math.Max(maxY, s.Bounds.Bottom);
        }
        using var bmp = new Bitmap(maxX - minX, maxY - minY, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(minX, minY, 0, 0, new System.Drawing.Size(maxX - minX, maxY - minY));
        }
        bmp.Save(Path.Combine(dir, "selftest_ocrpanel.png"), ImageFormat.Png);

        w.Close();
        File.WriteAllLines(Path.Combine(dir, "selftest_ocrpanel.txt"), lines);
        System.Windows.Application.Current.Shutdown();
    }

    private static int CountButtons(DependencyObject root)
    {
        var n = root is System.Windows.Controls.Button ? 1 : 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject d) n += CountButtons(d);
        }
        return n;
    }

    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        using var t = new System.Threading.Timer(_ => frame.Continue = false, null, ms, Timeout.Infinite);
        Dispatcher.PushFrame(frame);
    }
}
