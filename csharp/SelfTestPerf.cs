using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace JyutPingPopup;

/// <summary>划词弹窗连续显示的性能探针：模拟真实流程 Hide -> Render -> ShowAt，逐次计时。</summary>
public static class SelfTestPerf
{
    public static void Run()
    {
        var dir = AppContext.BaseDirectory;
        var log = new List<string>();
        var done = false;
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        dispatcher.BeginInvoke(new System.Action(() =>
        {
            // 首次 HasVoice 的代价：它在 Render() 里被调用，跑在 UI 线程
            var swVoice = Stopwatch.StartNew();
            SpeechService.ResetForTest();
            var has = SpeechService.HasVoice;
            log.Add($"has_voice_first={swVoice.Elapsed.TotalMilliseconds:F1}ms hasVoice={has}");
            swVoice.Restart();
            has = SpeechService.HasVoice;
            log.Add($"has_voice_cached={swVoice.Elapsed.TotalMilliseconds:F1}ms hasVoice={has}");

            var wasEnabled = SpeechSettings.GetEnabled();
            SpeechSettings.SetEnabled(true);
            var items = Jyutping.Annotate("重量");

            var popup = new PopupWindow();
            popup.Show();
            popup.HidePopup();
            popup.UpdateLayout();

            for (var i = 1; i <= 6; i++)
            {
                var sw = Stopwatch.StartNew();
                popup.HidePopup();
                var tHide = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                popup.Render(items, "");
                var tRender = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                popup.ShowAt(300 + i * 8, 300);
                var tShow = sw.Elapsed.TotalMilliseconds;

                log.Add($"run{i}\thide={tHide:F1}ms\trender={tRender:F1}ms\tshowAt={tShow:F1}ms\ttotal={tHide + tRender + tShow:F1}ms");
            }

            popup.Hide();
            SpeechSettings.SetEnabled(wasEnabled);
            SpeechService.ResetForTest();
            File.WriteAllLines(Path.Combine(dir, "selftest_perf.txt"), log);
            done = true;
        }));

        var frame = new DispatcherFrame();
        var timer = new System.Threading.Timer(_ =>
        {
            if (done || !frame.Continue) frame.Continue = false;
        }, null, 3000, 100);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        timer.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}
