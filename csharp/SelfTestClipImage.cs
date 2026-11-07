using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace JyutPingPopup;

/// <summary>剪贴板含图时，量 ReadByCopy 的持锁时长与是否改动内容。</summary>
internal static class SelfTestClipImage
{
    public static void Run()
    {
        var lines = new List<string>();
        var original = ClipboardBackup.Take();
        try
        {
            foreach (var (w, h) in new[] { (640, 480), (1920, 1080) })
            {
                PutDib(w, h);
                var seq = Native.GetClipboardSequenceNumber();
                var sw = Stopwatch.StartNew();
                var text = CaptureService.ReadByCopy();
                sw.Stop();
                lines.Add($"size={w}x{h} elapsedMs={sw.Elapsed.TotalMilliseconds:F2} " +
                          $"seqUnchanged={Native.GetClipboardSequenceNumber() == seq} " +
                          $"dibStillPresent={HasDib()} returnedTextLen={text.Length}");
            }
        }
        catch (Exception ex)
        {
            lines.Add("EXCEPTION=" + ex.GetType().Name + ":" + ex.Message);
        }
        finally
        {
            original?.Restore();
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "selftest_clipimage.txt"), lines);
        System.Windows.Application.Current.Shutdown();
    }

    private static void PutDib(int w, int h)
    {
        var buf = new byte[40 + w * h * 4];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), 40);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), w);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), h);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(12, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(14, 2), 32);

        var mem = Native.GlobalAlloc(Native.GmemMoveable, (nuint)buf.Length);
        if (mem == IntPtr.Zero) throw new Exception("GlobalAlloc failed");
        var ptr = Native.GlobalLock(mem);
        if (ptr == IntPtr.Zero) throw new Exception("GlobalLock failed");
        Marshal.Copy(buf, 0, ptr, buf.Length);
        Native.GlobalUnlock(mem);
        if (!Native.OpenClipboard(IntPtr.Zero)) throw new Exception("OpenClipboard failed");
        try
        {
            Native.EmptyClipboard();
            Native.SetClipboardData(Native.CfDib, mem);
        }
        finally { Native.CloseClipboard(); }
    }

    private static bool HasDib()
    {
        if (!Native.OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            uint f = 0;
            while ((f = Native.EnumClipboardFormats(f)) != 0)
                if (f is Native.CfDib or Native.CfDibV5 or Native.CfBitmap) return true;
            return false;
        }
        finally { Native.CloseClipboard(); }
    }
}
