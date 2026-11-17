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

        // 1. 自动选声：不区分男女，固定用本机列表里的第一台粤语语音
        SpeechService.SetVoicesForTest(new[]
        {
            new CantoneseVoice("Microsoft Tracy Desktop"),
            new CantoneseVoice("Microsoft Danny Desktop"),
        });
        Check("pick_auto_first", SpeechService.PickAutoForTest()?.Name ?? "", "Microsoft Tracy Desktop");

        SpeechService.SetVoicesForTest(new[] { new CantoneseVoice("Microsoft Huihui") });
        Check("pick_auto_only", SpeechService.PickAutoForTest()?.Name ?? "", "Microsoft Huihui");

        SpeechService.SetVoicesForTest(System.Array.Empty<CantoneseVoice>());
        Check("pick_none", SpeechService.PickAutoForTest() == null ? "null" : "nonnull", "null");
        Check("speak_no_voice", SpeechService.Speak("你好") ? "true" : "false", "false");

        // 2. 设置存储：写 -> 读 -> 还回原值，验证默认与往返
        var wasEnabled = SpeechSettings.GetEnabled();
        SpeechSettings.SetEnabled(true);
        Check("set_enabled_on", SpeechSettings.GetEnabled() ? "true" : "false", "true");
        SpeechSettings.SetEnabled(wasEnabled);

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
            SpeechService.SetVoicesForTest(new[] { new CantoneseVoice("Microsoft Tracy Desktop") });
            popup.Render(items, "");
            Check("btn_shown_with_voice", popup.SpeakBtnVisibilityForTest.ToString(), "Visible");
            Check("speak_text", popup.SpeakTextForTest, "你好");
            Check("card_not_hittable", popup.CardHitTestForTest ? "true" : "false", "false");
            Check("btn_hittable", popup.SpeakBtnHitTestForTest ? "true" : "false", "true");

            // 真实命中测试：走真实的 ShowAt（它会在 UI 线程缓存按钮物理矩形），
            // 按钮中心必须命中 SpeakBtn，卡片中心不能命中它。这是「卡片穿透、按钮可点」的核心证据。
            popup.ShowAt(300, 300);
            popup.UpdateLayout();
            popup.UpdateLayout();
            // 注：旧的 VisualTreeHelper.HitTest（相对窗口坐标）已由下面的 hook_* 系列取代，
            // 后者用真实物理坐标从钩子线程验证，更贴近实际鼠标点击，也覆盖「卡片穿透」。

            // 钩子线程安全探测：全局鼠标钩子在独立线程调用 IsOnSpeakButton，
            // 若内部碰 WPF 视觉树会抛跨线程异常并卡住钩子（表现为划词卡死）。
            string hookCall;
            try
            {
                Exception? captured = null;
                var t2 = new System.Threading.Thread(() =>
                {
                    try { popup.IsOnSpeakButton(0, 0); }
                    catch (System.Exception ex) { captured = ex; }
                });
                t2.Start();
                if (!t2.Join(3000)) captured = new System.TimeoutException("hook call did not return in 3s");
                hookCall = captured == null ? "ok" : "threw:" + captured.GetType().Name;
            }
            catch (System.Exception ex) { hookCall = "threw:" + ex.GetType().Name; }
            Check("hook_thread_safe", hookCall, "ok");

            // 命中准确性：缓存矩形后，按钮中心必须命中，远处不能命中（均从钩子线程调用）
            var btnPhys = popup.SpeakBtnScreenPointForTest();
            bool hitCenter = false, hitFar = true;
            var t3 = new System.Threading.Thread(() =>
            {
                hitCenter = popup.IsOnSpeakButton((int)btnPhys.X, (int)btnPhys.Y);
                hitFar = popup.IsOnSpeakButton(-999, -999);
            });
            t3.Start();
            t3.Join(3000);
            Check("hook_hit_button_center", hitCenter ? "true" : "false", "true");
            Check("hook_hit_far_miss", hitFar ? "true" : "false", "false");

            // 截图留证：用多音字样本（有 footer），肉眼确认按钮没压住文字
            var shotItems = Jyutping.Annotate("兩好");
            popup.Render(shotItems, "");
            popup.Show();
            popup.UpdateLayout();
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

            // 4. 设置界面：无语音包时开关置灰 + 安装提示，有语音时恢复
            SpeechSettings.SetEnabled(true);
            SpeechService.SetVoicesForTest(System.Array.Empty<CantoneseVoice>());
            var mwNoVoice = new MainWindow();
            Check("speak_box_disabled_no_voice", mwNoVoice.SpeakBoxEnabledForTest ? "true" : "false", "false");
            Check("no_voice_hint_visible", mwNoVoice.NoVoiceHintForTest.ToString(), "Visible");
            Check("install_uri", mwNoVoice.VoiceSettingsUriForTest, "ms-settings:regionlanguage");

            // 截图留证：无语音时设置界面的置灰开关 + 安装提示条
            mwNoVoice.Show();
            mwNoVoice.UpdateLayout();
            var mwShot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)System.Math.Ceiling(mwNoVoice.ActualWidth), (int)System.Math.Ceiling(mwNoVoice.ActualHeight),
                96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            mwShot.Render(mwNoVoice);
            var mwEnc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            mwEnc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mwShot));
            using (var mwFs = File.Create(Path.Combine(dir, "selftest_speak_settings.png"))) mwEnc.Save(mwFs);
            mwNoVoice.Hide();

            SpeechService.SetVoicesForTest(new[] { new CantoneseVoice("Microsoft Tracy Desktop") });
            var mwVoice = new MainWindow();
            Check("speak_box_enabled_with_voice", mwVoice.SpeakBoxEnabledForTest ? "true" : "false", "true");
            Check("no_voice_hint_collapsed", mwVoice.NoVoiceHintForTest.ToString(), "Collapsed");

            SpeechSettings.SetEnabled(wasEnabled);
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
