using System.Collections.Concurrent;
using System.Text;
using FlaUI.UIA3;

namespace JyutPingPopup;

/// <summary>
/// 取选中的文本。优先 UIA TextPattern（不碰剪贴板）；读不到汉字时
/// 才降级借一次 Ctrl+C，读完立刻把剪贴板还原成原来的内容。
/// 全程跑在专用 STA 线程上：UIA 是 COM 对象，且剪贴板需要等待，不能卡住钩子线程。
/// </summary>
public sealed class CaptureService : IDisposable
{
    // Epoch = 发起这次请求时弹窗的世代号。取词可能耗时（剪贴板兜底最长约 1.3s），
    // 期间用户若已收起弹窗，世代号会变，结果回来时对不上就应当丢弃，别让弹窗"复活"。
    private readonly BlockingCollection<(int X, int Y, long Epoch)> _queue = new();
    private readonly Thread _thread;
    private UIA3Automation? _automation;

    public event Action<int, int, string, string, long>? Captured;

    /// <summary>
    /// UIA 读不到时是否允许借一次剪贴板（发 Ctrl+C）。关掉后完全不碰剪贴板，
    /// 代价是不支持 UIA 的程序取不到词。由设置窗口的开关控制。
    /// </summary>
    public static bool AllowClipFallback { get; set; } = true;

    public CaptureService()
    {
        _thread = new Thread(WorkerLoop) { IsBackground = true, Name = "JyutPingCapture" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Request(int x, int y, long epoch) => _queue.Add((x, y, epoch));

    private void WorkerLoop()
    {
        _automation = new UIA3Automation();
        foreach (var first in _queue.GetConsumingEnumerable())
        {
            // 积压时只处理最新的一条。旧请求就算取到词也会因世代号过期被丢弃，
            // 却要白白再借一次剪贴板——而每一次借都有机会毁掉用户刚复制的内容。
            // 兜底单次最长约 1.3s，连着划几下就能堆出一串积压。
            var req = first;
            while (_queue.Count > 0) req = _queue.Take();
            var (x, y, epoch) = req;

            string text = "";
            var source = "UIA";
            try
            {
                text = ReadByUia();
            }
            catch
            {
                text = "";
            }

            // 只有 UIA 完全读不到东西才降级 Ctrl+C。UIA 读到了文本（哪怕只是英文）
            // 说明这条路是通的，不用再动剪贴板；读到的又没有中文，直接跳过，
            // 否则每点一下鼠标都会往别的程序发一次 Ctrl+C。
            if (text.Length == 0 && AllowClipFallback)
            {
                var copied = ReadByCopy();
                if (HasCjk(copied))
                {
                    text = copied;
                    source = "Ctrl+C";
                }
            }

            var kept = Jyutping.KeepCjk(text);
            if (kept.Length > 0) Captured?.Invoke(x, y, kept, source, epoch);
        }
    }

    private string ReadByUia()
    {
        var focused = _automation!.FocusedElement();
        var pattern = focused?.Patterns.Text.PatternOrDefault;
        if (pattern == null) return "";
        var sb = new StringBuilder();
        foreach (var range in pattern.GetSelection())
        {
            var piece = range.GetText(-1);
            if (!string.IsNullOrEmpty(piece)) sb.Append(piece);
        }
        return sb.ToString();
    }

    // 慢应用（远程桌面 / 某些 PDF / 终端）的 Ctrl+C 常常 500ms 后才填进剪贴板，
    // 窗口太短会造成「既没取到词，又把用户原来的剪贴板弄丢」的双重损失。
    private const int CopyTimeoutMs = 1000;
    private const int CopyPollMs = 30;
    // 等写入收敛用：一次写入能让序号跳好几格（实测 Empty+Set 一次跳 5，
    // 因为系统会连带合成 CF_TEXT / CF_OEMTEXT / CF_LOCALE），立刻读会撞上
    // 「已清空但还没写入」的中间态，读到空文本就误判成「别人放了图片」。
    private const int CopySettleMs = 300;

    /// <summary>借一次剪贴板再还原。应用不支持 UIA 读取时的兜底。</summary>
    internal static string ReadByCopy()
    {
        // 用序号判断有没有真的拷到东西：比前后比对文本更准，
        // 选中一段和剪贴板里一模一样的文字也能识别出来。
        var seqBefore = Native.GetClipboardSequenceNumber();
        // 先看格式表：剪贴板里已有图（用户刚复制的，或截图工具刚放的）就直接放弃。
        // 必须在 Take() 之前，Take 会 OpenClipboard 复制整张图，实测 1080p 持锁 15.8ms，
        // 正好挡住截图工具在同一瞬间写入新图，粘贴出来就成了上一张旧图。
        // 这里只枚举格式 ID、不取数据，锁窗口几乎为零。
        if (ClipboardBackup.ClipboardHasImage()) return "";

        // 剪贴板正忙（如用户正在复制）就拍不到快照，必须立刻放弃，
        // 绝不能继续发 Ctrl+C —— 否则会覆盖掉用户正在复制的内容且无法还原。
        var backup = ClipboardBackup.Take();
        if (backup == null) return "";

        // 快照之后剪贴板已被别的程序改写（典型：截图工具在松开鼠标那一刻把图片
        // 放进剪贴板）。此时发 Ctrl+C 或还原都会毁掉它，直接放弃、释放快照。
        if (Native.GetClipboardSequenceNumber() != seqBefore)
        {
            backup.Discard();
            return "";
        }

        var restore = false;
        try
        {
            Native.SendCtrlC();
            // 轮询剪贴板序号直到变化或超时：慢应用常在固定 sleep 内填不完，轮询更稳。
            var waited = 0;
            var changed = Native.GetClipboardSequenceNumber() != seqBefore;
            while (!changed && waited < CopyTimeoutMs)
            {
                Thread.Sleep(CopyPollMs);
                waited += CopyPollMs;
                changed = Native.GetClipboardSequenceNumber() != seqBefore;
            }

            // 超时：序号没变，说明剪贴板内容本来就等同于快照。此时再 Empty+Set
            // 重建一遍纯属多余，还会丢掉快照没能复制的格式，直接丢弃、不碰剪贴板。
            if (!changed) return "";

            // 等这一次写入收敛（序号不再变化）再读，避免读到半截的中间态。
            var seqNow = Native.GetClipboardSequenceNumber();
            var settled = 0;
            while (settled < CopySettleMs)
            {
                Thread.Sleep(CopyPollMs);
                settled += CopyPollMs;
                var s = Native.GetClipboardSequenceNumber();
                if (s == seqNow) break;
                seqNow = s;
            }

            var copied = Native.ReadClipboard();

            // 序号变了但里面没有文本：不是这次 Ctrl+C 拷到的字，而是别的程序
            // 刚放入的非文本内容（如截图图片）。还原会把那张图抹掉，跳过还原。
            if (copied.Length == 0) return copied;

            // 还原前再验一次序号：读文本这一小段时间里若又有人写入，说明当下这份
            // 内容比快照新，宁可不动，也不要覆盖掉别人刚复制的东西。
            restore = Native.GetClipboardSequenceNumber() == seqNow;
            return copied;
        }
        finally
        {
            // 读取成功或中途抛异常都要处理快照：正常还原，或放弃还原时释放句柄
            if (restore) backup.Restore();
            else backup.Discard();
        }
    }

    private static bool HasCjk(string text)
    {
        foreach (var c in text)
            if (Jyutping.IsCjk(c)) return true;
        return false;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _automation?.Dispose();
    }
}
