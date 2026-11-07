using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace JyutPingPopup;

/// <summary>
/// OCR 自检：画一张含中文的图，离线识别，验证 Windows.Media.Ocr 的 zh-Hans 引擎在本机可用。
/// 若系统没有 zh-Hans 识别包，写 SKIPPED 并以 0 退出（不阻塞构建）。
/// </summary>
public static class SelfTestOcr
{
    public static void Run()
    {
        var log = new List<string>();
        log.Add("supported\t" + Ocr.IsChineseSupported());
        if (!Ocr.IsChineseSupported())
        {
            log.Add("result\tSKIPPED (no zh-Hans OCR engine on this machine)");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest_ocr.txt"), log);
            System.Windows.Application.Current.Shutdown();
            return;
        }

        using var bmp = new Bitmap(420, 130, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var font = new Font("Microsoft YaHei", 48, FontStyle.Bold);
            g.DrawString("你好粤語", font, Brushes.Black, 10, 25);
        }

        // 在 UI 线程用 GetResult 阻塞会死锁（OCR 的 await 要回到原线程续跑）；
        // 丢到线程池执行，避免与 WPF 的 UI 同步上下文互锁。真实触发路径是正确 await，不会死锁。
        var text = Task.Run(() => Ocr.RecognizeAsync(bmp)).GetAwaiter().GetResult();
        log.Add("recognized\t" + text);
        log.Add("hasNi\t" + text.Contains("你"));
        log.Add("hasYue\t" + text.Contains("粤"));
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest_ocr.txt"), log);
        System.Windows.Application.Current.Shutdown();
    }
}
