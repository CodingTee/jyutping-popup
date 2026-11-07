using System.IO;
using System.Text;

namespace JyutPingPopup;

/// <summary>
/// 剪贴板快照/还原自测：拍一张全格式快照，写入测试文本，再还原，
/// 对比前后的文本与格式数。会短暂改动剪贴板，最后一定还原。
/// </summary>
public static class SelfTestClip
{
    public static void Run()
    {
        var log = new List<string>();

        var before = ClipboardBackup.Describe();
        log.Add("before\t" + before);

        var backup = ClipboardBackup.Take();
        log.Add("backup\t" + (backup != null ? "ok" : "FAILED (clipboard locked)"));

        Native.WriteClipboard("剪贴板自测文本 ABC");
        log.Add("afterWrite\t" + ClipboardBackup.Describe());

        backup?.Restore();
        var after = ClipboardBackup.Describe();
        log.Add("afterRestore\t" + after);

        // 逐字段比对：文本必须一字不差，格式数也要对得上
        var textBefore = Field(before, "text");
        var textAfter = Field(after, "text");
        log.Add("textMatch\t" + (textBefore == textAfter));
        log.Add("formatsMatch\t" + (Field(before, "formats") == Field(after, "formats")));

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest_clip.txt"), log);
        System.Windows.Application.Current.Shutdown();
    }

    private static string Field(string describe, string key)
    {
        var marker = key + "=";
        var start = describe.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        var rest = describe[start..];
        var end = rest.IndexOf('\t');
        return end < 0 ? rest : rest[..end];
    }
}
