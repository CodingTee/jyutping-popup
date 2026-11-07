using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>
/// 复现用户报告的 bug：先高亮无多音字（footer 收起），再高亮多音字，
/// footer 应重新出现却没有。直接驱动 PopupWindow，不经过捕获链路。
/// </summary>
public static class SelfTestFooter
{
    public static void Run()
    {
        var log = new List<string>();
        var popup = new PopupWindow();
        popup.Show();
        popup.HidePopup();

        // 第 1 弹：无多音字（的 dik1）
        var items1 = Jyutping.Annotate("的");
        popup.Render(items1, "");
        popup.ShowAt(300, 300);
        Snapshot(log, popup, "popup1(的)");

        popup.HidePopup();

        // 第 2 弹：多音字（两 -> 兩 loeng5/loeng2/loeng6）
        var items2 = Jyutping.Annotate("两");
        log.Add("annotate两\t" + string.Join("/", items2[0].All) + "\tcount=" + items2[0].All.Count);
        // bug1 验证：体 应读 tai2（不是生僻本字的 ban6）；干 应回退自己的 gon1（不是 幹 gon3）
        log.Add("annotate体\t" + Jyutping.Annotate("体")[0].Reading + "\tall=" + string.Join("/", Jyutping.Annotate("体")[0].All));
        log.Add("annotate干\t" + Jyutping.Annotate("干")[0].Reading + "\tall=" + string.Join("/", Jyutping.Annotate("干")[0].All));
        // 截断顺序的意义：按词查 重=cung5；被劈开后单字 重=cung4（错）
        log.Add("annotate重量\t" + string.Join(":", Jyutping.Annotate("重量").Select(i => i.Reading)));
        log.Add("annotate重\t" + Jyutping.Annotate("重")[0].Reading);
        log.Add("annotate什么\t" + string.Join(";", Jyutping.Annotate("什么").Select(i => i.Char + ":" + i.Reading + ":" + string.Join("/", i.All))));
        popup.Render(items2, "");
        log.Add("afterRender\t" + popup.MeasureProbe());
        popup.ShowAt(300, 300);
        Snapshot(log, popup, "popup2(两)");
        log.Add("footerText\t" + popup.FooterTextForTest);

        if (popup.Content is System.Windows.Controls.Border b)
            log.Add($"borderDesired\t{b.DesiredSize.Width:F1}x{b.DesiredSize.Height:F1}");

        popup.HidePopup();

        // 第 3 弹：多音 -> 无多音，窗口要缩回去
        popup.Render(Jyutping.Annotate("的"), "");
        popup.ShowAt(300, 300);
        Snapshot(log, popup, "popup3(的)");
        popup.HidePopup();

        // 第 4 弹：简繁差异（身体 -> 身體），应显示繁体为主 + 小灰字原简体
        var bodyItems = Jyutping.Annotate("身体");
        popup.Render(bodyItems, "");
        popup.ShowAt(300, 300);
        Snapshot(log, popup, "popup4(身体)");
        popup.HidePopup();

        var path = Path.Combine(AppContext.BaseDirectory, "selftest_footer.txt");
        File.WriteAllLines(path, log);
        System.Windows.Application.Current.Shutdown();
    }

    private static void Snapshot(List<string> log, PopupWindow popup, string tag)
    {
        popup.UpdateLayout();
        log.Add($"{tag}\twindow={popup.ActualWidth:F1}x{popup.ActualHeight:F1}\t" +
                $"footerVis={popup.FooterVisibilityForTest}\tfooterActH={popup.FooterActualHeightForTest:F1}");
        try
        {
            var w = (int)Math.Ceiling(popup.ActualWidth);
            var h = (int)Math.Ceiling(popup.ActualHeight);
            if (w <= 0 || h <= 0) return;
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(popup);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(AppContext.BaseDirectory, $"selftest_footer_{tag.Split('(')[0]}.png"));
            encoder.Save(fs);
        }
        catch (Exception ex) { log.Add("shot_error\t" + ex.Message); }
    }
}
