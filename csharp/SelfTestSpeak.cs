using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>发音功能自测：语音选声、开关存储、按钮显隐。不发实际声音，结果写 selftest_speak.txt。</summary>
public static class SelfTestSpeak
{
    private static readonly List<string> Log = new();

    public static void Run()
    {
        var dir = AppContext.BaseDirectory;

        // 1. 选声逻辑：注入两台假语音，验证男女偏好与缺性别时的回退
        SpeechService.SetVoicesForTest(new[]
        {
            new CantoneseVoice("Microsoft Tracy Desktop", true),
            new CantoneseVoice("Microsoft Danny Desktop", false),
        });
        Check("pick_female", SpeechService.PickForTest(true)?.Name ?? "", "Microsoft Tracy Desktop");
        Check("pick_male", SpeechService.PickForTest(false)?.Name ?? "", "Microsoft Danny Desktop");

        SpeechService.SetVoicesForTest(new[] { new CantoneseVoice("Microsoft Tracy Desktop", true) });
        Check("pick_fallback_to_only", SpeechService.PickForTest(false)?.Name ?? "", "Microsoft Tracy Desktop");

        SpeechService.SetVoicesForTest(System.Array.Empty<CantoneseVoice>());
        Check("pick_none", SpeechService.PickForTest(true) == null ? "null" : "nonnull", "null");
        Check("speak_no_voice", SpeechService.Speak("你好", true) ? "true" : "false", "false");

        // 2. 设置存储：写 -> 读 -> 还回原值，验证默认与往返
        var wasEnabled = SpeechSettings.GetEnabled();
        var wasFemale = SpeechSettings.GetPreferFemale();
        SpeechSettings.SetEnabled(true);
        Check("set_enabled_on", SpeechSettings.GetEnabled() ? "true" : "false", "true");
        SpeechSettings.SetPreferFemale(false);
        Check("set_voice_male", SpeechSettings.GetPreferFemale() ? "true" : "false", "false");
        SpeechSettings.SetEnabled(wasEnabled);
        SpeechSettings.SetPreferFemale(wasFemale);

        // 3. 弹窗按钮显隐 + 命中标记（用 Dispatcher 跑，窗口需要 UI 线程）
        var done = false;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        dispatcher.BeginInvoke(new System.Action(() =>
        {
            var popup = new PopupWindow();
            var items = Jyutping.Annotate("你好");

            // 无语音 + 开关关：按钮必须隐藏
            SpeechSettings.SetEnabled(true);
            SpeechService.SetVoicesForTest(System.Array.Empty<CantoneseVoice>());
            popup.Render(items, "");
            Check("btn_hidden_no_voice", popup.SpeakBtnVisibilityForTest.ToString(), "Collapsed");

            // 有语音 + 开关开：按钮显示，且朗读文本是原文汉字
            SpeechService.SetVoicesForTest(new[] { new CantoneseVoice("Microsoft Tracy Desktop", true) });
            popup.Render(items, "");
            Check("btn_shown_with_voice", popup.SpeakBtnVisibilityForTest.ToString(), "Visible");
            Check("speak_text", popup.SpeakTextForTest, "你好");
            Check("card_not_hittable", popup.CardHitTestForTest ? "true" : "false", "false");
            Check("btn_hittable", popup.SpeakBtnHitTestForTest ? "true" : "false", "true");

            // 真实命中测试：Show 出来后，按钮中心必须命中 SpeakBtn，卡片中心不能命中它。
            // 这是「卡片穿透、按钮可点」的核心证据，光看 IsHitTestVisible 属性不够。
            popup.Show();
            popup.UpdateLayout();
            popup.UpdateLayout();
            var btnCenter = popup.SpeakBtnPointForTest();
            var hitBtn = System.Windows.Media.VisualTreeHelper.HitTest(popup, btnCenter);
            var hitName = FindNamedAncestor(hitBtn?.VisualHit) ?? "null";
            Check("hit_test_button_center", hitName, "SpeakBtn");

            // 卡片左上角内侧一点（避开按钮），应命中不到 SpeakBtn
            var cardPoint = popup.CardSafePointForTest();
            var hitCard = System.Windows.Media.VisualTreeHelper.HitTest(popup, cardPoint);
            var hitCardName = (hitCard?.VisualHit as System.Windows.FrameworkElement)?.Name ?? "null";
            Check("hit_test_card_area_not_button", hitCardName == "SpeakBtn" ? "SpeakBtn" : "not_button", "not_button");

            // 截图留证：用多音字样本（有 footer），肉眼确认按钮没压住文字
            var shotItems = Jyutping.Annotate("兩好");
            popup.Render(shotItems, "");
            popup.UpdateLayout();
            var shot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)System.Math.Ceiling(popup.ActualWidth), (int)System.Math.Ceiling(popup.ActualHeight),
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            shot.Render(popup);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
            using (var fs = File.Create(Path.Combine(dir, "selftest_speak.png"))) enc.Save(fs);
            popup.Hide();

            // 开关关：即使有语音也不显示
            SpeechSettings.SetEnabled(false);
            popup.Render(items, "");
            Check("btn_hidden_when_off", popup.SpeakBtnVisibilityForTest.ToString(), "Collapsed");

            SpeechSettings.SetEnabled(wasEnabled);
            SpeechSettings.SetPreferFemale(wasFemale);
            SpeechService.ResetForTest();

            File.WriteAllLines(Path.Combine(dir, "selftest_speak.txt"), Log);
            done = true;
        }));

        var frame = new DispatcherFrame();
        var timer = new System.Threading.Timer(_ =>
        {
            if (done || !frame.Continue) frame.Continue = false;
        }, null, 1200, 100);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private static void Check(string name, string actual, string expected)
    {
        Log.Add($"{name}\texpected={expected}\tactual={actual}\t{(actual == expected ? "PASS" : "FAIL")}");
    }

    private static string? FindNamedAncestor(System.Windows.DependencyObject? obj)
    {
        while (obj != null)
        {
            if (obj is System.Windows.FrameworkElement fe && !string.IsNullOrEmpty(fe.Name)) return fe.Name;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
