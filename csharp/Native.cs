using System.Runtime.InteropServices;
using System.Text;

namespace JyutPingPopup;

internal static partial class Native
{
    // ---- 单实例互斥量 ----
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr CreateMutexW(IntPtr attrs, int initialOwner, string name);

    internal const int ErrorAlreadyExists = 183;

    // ---- 剪贴板（走 Win32，避免 WPF/WinForms 的 STA 限制）----
    internal const uint CfUnicodeText = 13;
    internal const uint CfBitmap = 2;
    internal const uint CfMetaFilePict = 3;
    internal const uint CfEnhMetaFile = 14;
    internal const uint CfOwnerDisplay = 0x0080;
    internal const uint CfDib = 8;
    internal const uint CfDibV5 = 17;
    internal const uint CfDspEnhMetaFile = 0x0083;
    internal const uint GmemMoveable = 0x0002;
    internal const uint ImageBitmap = 0;
    // 0 = 让 CopyImage 复制一份新句柄。不能用 LR_COPYRETURNORG（会返回剪贴板原有的句柄，
    // 之后 Restore 里的 EmptyClipboard 会把它释放掉，再 SetClipboardData 同一个句柄就悬空了）。
    internal const uint CopyImageFlagsCopy = 0;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr SetClipboardData(uint format, IntPtr handle);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GlobalAlloc(uint flags, nuint size);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GlobalFree(IntPtr handle);

    [LibraryImport("user32.dll")]
    internal static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClipboardFormatNameW")]
    internal static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    internal static partial nuint GlobalSize(IntPtr handle);

    // 位图/增强图元文件不是 HGLOBAL，不能按字节拷贝，得用各自的复制函数
    [LibraryImport("user32.dll")]
    internal static partial IntPtr CopyImage(IntPtr handle, uint type, int cx, int cy, uint flags);

    [LibraryImport("gdi32.dll")]
    internal static partial IntPtr CopyEnhMetaFileW(IntPtr hemfSrc, IntPtr filename);

    // 放弃剪贴板快照时释放各自的句柄副本
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteEnhMetaFile(IntPtr hemf);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalUnlock(IntPtr handle);

    internal static string ReadClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return "";
        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero) return "";
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(ptr) ?? ""; }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    internal static void WriteClipboard(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            if (text.Length == 0) return;
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var handle = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (handle == IntPtr.Zero) return;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return;
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                GlobalUnlock(handle);
                SetClipboardData(CfUnicodeText, handle);
            }
            catch { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }

    // ---- 模拟 Ctrl+C ----
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkControl = 0x11;
    private const ushort VkC = 0x43;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyBdInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, AnyInput[] inputs, int size);

    internal static void SendCtrlC()
    {
        AnyInput Make(ushort vk, uint flags) => new()
        {
            Type = InputKeyboard,
            Keyboard = new KeyBdInput { Vk = vk, Flags = flags },
        };
        var inputs = new[]
        {
            Make(VkControl, 0),
            Make(VkC, 0),
            Make(VkC, KeyEventFKeyUp),
            Make(VkControl, KeyEventFKeyUp),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AnyInput>());
    }

    // ---- 鼠标注入（仅测试用：点在自己的测试窗口里）----
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFAbsolute = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct AnyInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse;
        [FieldOffset(8)] public KeyBdInput Keyboard;
    }

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    /// <summary>在指定物理像素坐标注入一次左键单击。测试专用。</summary>
    internal static void SendLeftClick(int pixelX, int pixelY)
    {
        var screenW = GetSystemMetrics(0);
        var screenH = GetSystemMetrics(1);
        AnyInput Make(uint flags, int dx = 0, int dy = 0) => new()
        {
            Type = InputMouse,
            Mouse = new MouseInput
            {
                Dx = dx * 65535 / Math.Max(1, screenW - 1),
                Dy = dy * 65535 / Math.Max(1, screenH - 1),
                Flags = flags,
            },
        };
        var inputs = new[]
        {
            Make(MouseEventFMove | MouseEventFAbsolute, pixelX, pixelY),
            Make(MouseEventFLeftDown, pixelX, pixelY),
            Make(MouseEventFLeftUp, pixelX, pixelY),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AnyInput>());
    }

    // ---- 全局钩子：低级鼠标 / 低级键盘 ----
    internal const int WhMouseLl = 14;
    internal const int WhKeyboardLl = 13;
    internal const int WmLButtonDown = 0x0201;
    internal const int WmLButtonUp = 0x0202;
    internal const int WmKeyDown = 0x0100;
    internal const int WmKeyUp = 0x0101;
    internal const int WmSysKeyDown = 0x0104; // Alt 等系统键的按下走这条，否则裸 Alt 热键永远收不到
    internal const int WmSysKeyUp = 0x0105;

    internal delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public Point Pt;
        public IntPtr MouseData;
        public IntPtr Flags;
        public IntPtr Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial IntPtr SetWindowsHookExW(int hookType, HookProc proc, IntPtr module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int vk);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMessageW(out Msg msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TranslateMessage(ref Msg msg);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr DispatchMessageW(ref Msg msg);

    [LibraryImport("user32.dll")]
    internal static partial void PostQuitMessage(int exitCode);

    internal const uint WmQuit = 0x0012;

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    internal static int MousePointX(IntPtr lParam) => Marshal.PtrToStructure<MouseHookStruct>(lParam).Pt.X;
    internal static int MousePointY(IntPtr lParam) => Marshal.PtrToStructure<MouseHookStruct>(lParam).Pt.Y;
    internal static uint KeyboardVkCode(IntPtr lParam) => Marshal.PtrToStructure<KeyboardHookStruct>(lParam).VkCode;

    // 取坐标处的窗口与它的进程 ID，用来排除「在自家窗口里划选也触发捕获」的误判
    [LibraryImport("user32.dll")]
    internal static partial IntPtr WindowFromPoint(Point pt);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal static bool KeyboardIsRepeat(IntPtr lParam)
    {
        // KF_REPEAT = 0x40000000：该位为 1 表示这次是系统自动重复，不是首次按下
        return (Marshal.PtrToStructure<KeyboardHookStruct>(lParam).Flags & 0x40000000u) != 0;
    }
}
