using System.IO;

namespace JyutPingPopup;

/// <summary>
/// 剪贴板竞态自测：回答「降级 Ctrl+C 那 500ms 里，别人写进来的东西会不会被我们抹掉」。
/// S1 外部写入文本（模拟用户此刻按了 Ctrl+C）→ 看最终剪贴板是原始快照还是用户新复制的。
/// S2 外部写入非文本（这里用清空剪贴板模拟，等价于截图工具放入图片）→ 看跳过还原的保护是否生效。
/// </summary>
public static class SelfTestClipRace
{
    public static void Run()
    {
        var log = new List<string>();

        // 关键标定：一次「标准写入」（Empty+Set）到底让剪贴板序号跳几格。
        // 若跳 2 格，就不能用 delta==1 判定「这次写入是我发的 Ctrl+C」，否则正常路径会被误判。
        var s0 = Native.GetClipboardSequenceNumber();
        Native.WriteClipboard("DELTA_PROBE");
        var s1 = Native.GetClipboardSequenceNumber();
        log.Add($"seq-delta-per-write\t{s1 - s0}");

        log.AddRange(new[]
        {
            "S1-foreign-text-150ms\t" + Race("USER_NEW_COPY", 150),
            "S2-foreign-nontext-150ms\t" + Race("", 150),
            "S3-foreign-late-700ms\t" + Race("LATE_COPY", 700),
        });
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest_cliprace.txt"), log);
        System.Windows.Application.Current.Shutdown();
    }

    private static string Race(string foreign, int delayMs)
    {
        Native.WriteClipboard("ORIGINAL_USER_DATA");

        // 在 Ctrl+C 的轮询窗口内，抢先写入，模拟「用户/截图工具此刻也动了剪贴板」
        var racer = new Thread(() =>
        {
            Thread.Sleep(delayMs);
            Native.WriteClipboard(foreign);
        }) { IsBackground = true };
        racer.Start();

        var copied = CaptureService.ReadByCopy();
        racer.Join();

        var after = ClipboardBackup.Describe();
        var foreignSurvived = foreign.Length > 0 && after.Contains(foreign);
        return $"returned=\"{Sanitize(copied)}\" after={after} "
             + $"foreignSurvived={foreignSurvived} "
             + $"originalSurvived={after.Contains("ORIGINAL_USER_DATA")}";
    }

    private static string Sanitize(string s) =>
        (s.Length > 30 ? s[..30] + "..." : s)
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
