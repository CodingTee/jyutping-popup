using System.Runtime.InteropServices;
using System.Text;

namespace JyutPingPopup;

/// <summary>
/// 剪贴板全格式快照。降级 Ctrl+C 之前拍一张，读完原样放回去——
/// 不然只还原纯文本会把用户复制的 HTML / RTF / 图片等格式一起抹掉。
/// 一条记录三种形态：普通格式存字节，位图与图元文件存句柄副本，
/// 剩下的（延迟渲染、METAFILEPICT）罕见，直接跳过不还原。
/// </summary>
internal sealed class ClipboardBackup
{
    private readonly List<(uint Format, byte[]? Bytes, IntPtr Handle)> _items = new();

    public static ClipboardBackup? Take()
    {
        if (!Native.OpenClipboard(IntPtr.Zero)) return null;
        var backup = new ClipboardBackup();
        try
        {
            uint format = 0;
            while ((format = Native.EnumClipboardFormats(format)) != 0)
            {
                var data = Native.GetClipboardData(format);
                if (data == IntPtr.Zero) continue;

                if (format == Native.CfBitmap)
                {
                    var copy = Native.CopyImage(data, Native.ImageBitmap, 0, 0, Native.CopyImageFlagsCopy);
                    if (copy != IntPtr.Zero) backup._items.Add((format, null, copy));
                    continue;
                }
                if (format == Native.CfEnhMetaFile)
                {
                    var copy = Native.CopyEnhMetaFileW(data, IntPtr.Zero);
                    if (copy != IntPtr.Zero) backup._items.Add((format, null, copy));
                    continue;
                }
                if (format is Native.CfMetaFilePict or Native.CfOwnerDisplay or Native.CfDspEnhMetaFile)
                    continue;

                var size = (int)Native.GlobalSize(data);
                if (size <= 0) continue;
                var ptr = Native.GlobalLock(data);
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    var bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, size);
                    backup._items.Add((format, bytes, IntPtr.Zero));
                }
                finally { Native.GlobalUnlock(data); }
            }
        }
        finally { Native.CloseClipboard(); }
        return backup;
    }

    /// <summary>只读检查剪贴板格式表里有没有图片，不取数据、不复制。
    /// 打不开剪贴板说明别人正在写，也当作「有图」直接让行。</summary>
    public static bool ClipboardHasImage()
    {
        if (!Native.OpenClipboard(IntPtr.Zero)) return true;
        try
        {
            var sb = new StringBuilder(256);
            uint format = 0;
            while ((format = Native.EnumClipboardFormats(format)) != 0)
            {
                if (format is Native.CfBitmap or Native.CfDib or Native.CfDibV5 or Native.CfEnhMetaFile)
                    return true;
                sb.Clear();
                if (Native.GetClipboardFormatName(format, sb, sb.Capacity) <= 0) continue;
                var n = sb.ToString();
                if (n.Contains("PNG") || n.Contains("JFIF") || n.Contains("GIF") ||
                    n.Contains("TIFF") || n.Contains("DIB") || n.Contains("BITMAP") ||
                    n.Contains("EMF") || n.Contains("WMF"))
                    return true;
            }
            return false;
        }
        finally { Native.CloseClipboard(); }
    }

    public void Restore()
    {
        // 顺序很关键：先把所有内容都备好，全部就位才动剪贴板。
        // 不能先 EmptyClipboard 再造 —— 中途任何一次分配失败，剪贴板就会停在
        // 「已清空但没写回」的状态，用户原来的内容就永久丢了。
        var prepared = new List<(uint Format, IntPtr Handle, bool Allocated)>();
        foreach (var (format, bytes, handle) in _items)
        {
            if (bytes != null)
            {
                var mem = Native.GlobalAlloc(Native.GmemMoveable, (nuint)bytes.Length);
                if (mem == IntPtr.Zero) { ReleasePrepared(prepared); return; }
                var ptr = Native.GlobalLock(mem);
                if (ptr == IntPtr.Zero)
                {
                    Native.GlobalFree(mem);
                    ReleasePrepared(prepared);
                    return;
                }
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Native.GlobalUnlock(mem);
                prepared.Add((format, mem, true));
            }
            else if (handle != IntPtr.Zero)
            {
                prepared.Add((format, handle, false));
            }
        }

        if (!Native.OpenClipboard(IntPtr.Zero)) { ReleasePrepared(prepared); return; }
        try
        {
            Native.EmptyClipboard();
            foreach (var (format, h, _) in prepared)
                Native.SetClipboardData(format, h); // 成功后句柄归系统，不要再释放
        }
        finally { Native.CloseClipboard(); }

        // 句柄已全部移交系统，清掉列表，避免之后再 Discard 时重复释放
        _items.Clear();
    }

    /// <summary>备货失败或打不开剪贴板时，把已经造出来的句柄收回去，避免泄漏。</summary>
    private static void ReleasePrepared(List<(uint Format, IntPtr Handle, bool Allocated)> prepared)
    {
        foreach (var (format, handle, allocated) in prepared)
        {
            if (allocated) Native.GlobalFree(handle);
            else if (format == Native.CfBitmap) Native.DeleteObject(handle);
            else if (format == Native.CfEnhMetaFile) Native.DeleteEnhMetaFile(handle);
        }
        prepared.Clear();
    }

    /// <summary>
    /// 不还原、只释放快照里拷出来的句柄副本。用于发现「剪贴板已被别的程序改写」时
    /// 放弃还原——此时还原会覆盖别人的新内容（如截图工具刚放入的图片）。
    /// </summary>
    public void Discard()
    {
        foreach (var (format, bytes, handle) in _items)
        {
            if (bytes != null || handle == IntPtr.Zero) continue;
            if (format == Native.CfBitmap) Native.DeleteObject(handle);
            else if (format == Native.CfEnhMetaFile) Native.DeleteEnhMetaFile(handle);
        }
        _items.Clear();
    }

    /// <summary>自测用：描述当前剪贴板（格式数 + 文本前 40 字），不改动内容。</summary>
    public static string Describe()
    {
        if (!Native.OpenClipboard(IntPtr.Zero)) return "locked";
        try
        {
            var count = 0;
            var format = 0u;
            while ((format = Native.EnumClipboardFormats(format)) != 0) count++;

            // 已经开着剪贴板，不能再走 ReadClipboard（它会自己 Open 一次），直接读
            var text = "";
            var handle = Native.GetClipboardData(Native.CfUnicodeText);
            if (handle != IntPtr.Zero)
            {
                var ptr = Native.GlobalLock(handle);
                if (ptr != IntPtr.Zero)
                {
                    try { text = Marshal.PtrToStringUni(ptr) ?? ""; }
                    finally { Native.GlobalUnlock(handle); }
                }
            }
            if (text.Length > 40) text = text[..40] + "...";
            return $"formats={count} text=\"{text.Replace("\r", "\\r").Replace("\n", "\\n")}\"";
        }
        finally { Native.CloseClipboard(); }
    }
}
