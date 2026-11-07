using System.Runtime.InteropServices;

namespace JyutPingPopup;

/// <summary>
/// 一个快捷键组合：可选 Ctrl/Shift/Alt 修饰键 + 一个主键（VK 码）。
/// 序列化用紧凑形式 "c,s,a:vk"（如 "110:81" = Ctrl+Shift+Q），便于存注册表。
/// 特殊情形：主键为 0 且带单个修饰键，表示「孤立敲击修饰键」（只按一下 Ctrl / Shift / Alt），
/// 这种组合在 key-up 时触发（需保证按下到松开期间没按过别的键，否则会和 Ctrl+C 这类组合键混淆）。
/// </summary>
public sealed class Hotkey
{
    public bool Ctrl, Shift, Alt;
    public uint Key;
    public string KeyName = "";

    public string Format()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Key > 0)
            parts.Add(string.IsNullOrEmpty(KeyName) ? $"0x{Key:X2}" : KeyName);
        return parts.Count == 0 ? "（未设置）" : string.Join(" + ", parts);
    }

    public string ToCompact() => $"{(Ctrl ? 1 : 0)}{(Shift ? 1 : 0)}{(Alt ? 1 : 0)}:{Key}";

    public static Hotkey ParseCompact(string? s, Hotkey fallback)
    {
        if (s != null)
        {
            var sp = s.Split(':');
            if (sp.Length == 2 && int.TryParse(sp[0], out var m) && uint.TryParse(sp[1], out var key))
            {
                var ctrl = (m / 100 % 10) == 1;
                var shift = (m / 10 % 10) == 1;
                var alt = (m % 10) == 1;
                // 普通组合：key > 0；孤立修饰键：key == 0 但至少一个修饰键置位
                if (key > 0 || (key == 0 && (ctrl || shift || alt)))
                {
                    return new Hotkey
                    {
                        Ctrl = ctrl,
                        Shift = shift,
                        Alt = alt,
                        Key = key,
                        KeyName = key == 0 ? BareModifierName(ctrl, shift, alt) : KeyNameOf(key),
                    };
                }
            }
        }
        return fallback;
    }

    public static Hotkey DefaultQuit() => new() { Ctrl = true, Shift = true, Key = 0x51, KeyName = "Q" }; // Q
    public static Hotkey DefaultPause() => new() { Ctrl = true, Shift = true, Key = 0x50, KeyName = "P" }; // P
    public static Hotkey DefaultOcr() => new() { Ctrl = true, Shift = true, Key = 0x4C, KeyName = "L" }; // L

    private static string BareModifierName(bool ctrl, bool shift, bool alt) =>
        alt ? "Alt" : shift ? "Shift" : ctrl ? "Ctrl" : "";

    public static string KeyNameOf(uint vk)
    {
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();   // A-Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();   // 0-9
        if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);       // F1-F12
        return vk switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x1B => "Esc",
            _ => $"0x{vk:X2}"
        };
    }
}

/// <summary>
/// 低级鼠标/键盘钩子。钩子回调必须尽快返回，所以事件在专用线程上抛出，
/// 由调用方自己切回 UI 线程；钩子所在的线程必须自带消息泵，否则 Windows 会静默摘钩。
/// </summary>
public sealed class InputHooks : IDisposable
{
    private const uint VkEscape = 0x1B;
    // 编辑类按键：按下说明用户在改东西（选区多半已经没了），直接收起弹窗
    private const uint VkBackspace = 0x08;
    private const uint VkDelete = 0x2E;
    private const uint VkReturn = 0x0D;   // 主键盘与小键盘的 Enter 都是它
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12; // Alt
    // 左右修饰键的物理 VK（低级钩子里 Ctrl/Shift/Alt 常报这些，而非中性的 0x10/0x11/0x12）
    private const uint VkLShift = 0xA0;
    private const uint VkRShift = 0xA1;
    private const uint VkLControl = 0xA2;
    private const uint VkRControl = 0xA3;
    private const uint VkLMenu = 0xA4;
    private const uint VkRMenu = 0xA5;

    /// <summary>每次左键松开都触发，只用来收起弹窗，不取词。</summary>
    public event Action<int, int>? LeftButtonUp;
    /// <summary>只有「按下→移动→松开」这种划选手势才触发，才去取词。</summary>
    public event Action<int, int>? SelectionMade;
    public event Action? EscapePressed;
    /// <summary>Backspace / Delete / Enter 按下：用户在编辑，收起弹窗。</summary>
    public event Action? DismissRequested;
    public event Action? QuitRequested;
    public event Action? PauseToggleRequested;
    public event Action? OcrRequested;
    /// <summary>「自定义」模式捕获到新组合键时触发；参数为 null 表示用户按 Esc 取消。</summary>
    public event Action<Hotkey?>? HotkeyCaptured;

    private const int DragThresholdPx = 4; // 手抖容差：位移超过这个才算真的拖动

    private readonly Thread _thread;
    private Native.HookProc? _mouseProc;
    private Native.HookProc? _keyProc;
    private IntPtr _mouseHook;
    private IntPtr _keyHook;
    private uint _threadId;
    private int _downX;
    private int _downY;
    private bool _downTracked;

    private volatile Hotkey _quit = Hotkey.DefaultQuit();
    private volatile Hotkey _pause = Hotkey.DefaultPause();
    private volatile Hotkey _ocr = Hotkey.DefaultOcr();
    private volatile bool _capturing;

    // 正常模式：跟踪「当前是否处于孤立修饰键敲击」——_modDown 是奇数说明有修饰键按着，
    // _bareOtherPressed 标记按下到松开期间是否出现过真正的键，用来区分 Ctrl 与 Ctrl+C。
    private int _modDown;
    private bool _bareOtherPressed;
    // 捕获模式对应的同款跟踪字段（只在钩子线程内访问）
    private int _capModDown;
    private bool _capOtherPressed;

    public InputHooks()
    {
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "JyutPingHooks" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    // ---- 运行时可配置热键 ----
    public void SetQuitHotkey(Hotkey h) => _quit = h;
    public void SetPauseHotkey(Hotkey h) => _pause = h;
    public void SetOcrHotkey(Hotkey h) => _ocr = h;
    public void StartCapture()
    {
        _capturing = true;
        _capModDown = 0;
        _capOtherPressed = false;
    }
    public void StopCapture()
    {
        _capturing = false;
        _capModDown = 0;
        _capOtherPressed = false;
    }

    private void MessageLoop()
    {
        _threadId = Native.GetCurrentThreadId();
        _mouseProc = MouseCallback;
        _keyProc = KeyboardCallback;
        _mouseHook = Native.SetWindowsHookExW(Native.WhMouseLl, _mouseProc, IntPtr.Zero, 0);
        _keyHook = Native.SetWindowsHookExW(Native.WhKeyboardLl, _keyProc, IntPtr.Zero, 0);

        while (Native.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }

        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
    }

    private IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            if (wParam == Native.WmLButtonDown)
            {
                _downX = Native.MousePointX(lParam);
                _downY = Native.MousePointY(lParam);
                _downTracked = true;
            }
            else if (wParam == Native.WmLButtonUp)
            {
                var x = Native.MousePointX(lParam);
                var y = Native.MousePointY(lParam);
                LeftButtonUp?.Invoke(x, y); // 收起弹窗

                // 只有按下后真的移动过才算划选；原地点一下不取词、不动剪贴板
                var dragged = _downTracked &&
                    (Math.Abs(x - _downX) > DragThresholdPx || Math.Abs(y - _downY) > DragThresholdPx);
                _downTracked = false;
                if (dragged) SelectionMade?.Invoke(x, y);
            }
        }
        return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private IntPtr KeyboardCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var vk = Native.KeyboardVkCode(lParam);
            // 普通键与 Alt 等系统键走同一套处理：Alt 的按下/松开是 WM_SYSKEYDOWN/WM_SYSKEYUP，
            // 不处理的话「裸 Alt」热键永远收不到（Alt 的键消息是 SYS 系列，而非普通 0x0100/0x0101）。
            var isKeyDown = wParam is Native.WmKeyDown or Native.WmSysKeyDown;
            var isKeyUp = wParam is Native.WmKeyUp or Native.WmSysKeyUp;

            if (_capturing)
            {
                if (isKeyUp)
                {
                    // 捕获模式：孤立修饰键松手（期间没按过别的键）即绑定为纯修饰键热键
                    if (IsModifierKey(vk))
                    {
                        var wasDown = _capModDown > 0;
                        _capModDown = wasDown ? _capModDown - 1 : 0;
                        if (_capModDown == 0 && !_capOtherPressed && wasDown)
                            EndCapture(ModifierHotkey(vk));
                    }
                }
                else // key down
                {
                    var repeat = Native.KeyboardIsRepeat(lParam);
                    if (vk == VkEscape)
                    {
                        EndCapture(null);
                    }
                    else if (!repeat)
                    {
                        if (IsModifierKey(vk))
                        {
                            _capModDown++;
                            if (_capModDown == 1) _capOtherPressed = false;
                        }
                        else
                        {
                            _capOtherPressed = true;
                            var hk = new Hotkey
                            {
                                Ctrl = IsDown(VkControl),
                                Shift = IsDown(VkShift),
                                Alt = IsDown(VkMenu),
                                Key = vk,
                                KeyName = Hotkey.KeyNameOf(vk),
                            };
                            EndCapture(hk);
                        }
                    }
                }
            }
            else // 正常模式
            {
                if (isKeyUp)
                {
                    // 孤立修饰键在 key-up 触发：必须保证按下到松开期间没按过别的键
                    if (IsModifierKey(vk))
                    {
                        var wasDown = _modDown > 0; // 没见过对应的 key-down 就不算孤立敲击（避免悬空 key-up 误触发）
                        _modDown = wasDown ? _modDown - 1 : 0;
                        if (_modDown == 0 && !_bareOtherPressed && wasDown)
                        {
                            if (BareMatches(_quit, vk)) QuitRequested?.Invoke();
                            else if (BareMatches(_pause, vk)) PauseToggleRequested?.Invoke();
                        }
                        if (_modDown == 0) _bareOtherPressed = false;
                    }
                }
                else if (!Native.KeyboardIsRepeat(lParam))
                {
                    if (vk == VkEscape) EscapePressed?.Invoke();
                    else if (IsModifierKey(vk))
                    {
                        _modDown++;
                        if (_modDown == 1) _bareOtherPressed = false; // 新一轮孤立敲击开始
                    }
                    else
                    {
                        _bareOtherPressed = true; // 出现过真正按键：不再是孤立修饰键
                        if (Matches(_quit, vk)) QuitRequested?.Invoke();
                        else if (Matches(_pause, vk)) PauseToggleRequested?.Invoke();
                        else if (Matches(_ocr, vk)) OcrRequested?.Invoke();
                        // 放在热键之后：用户若把 Enter/Backspace/Delete 绑成了热键，以热键为准
                        else if (IsDismissKey(vk)) DismissRequested?.Invoke();
                    }
                }
            }
        }
        return Native.CallNextHookEx(_keyHook, code, wParam, lParam);
    }

    private void EndCapture(Hotkey? hk)
    {
        _capturing = false;
        _capModDown = 0;
        _capOtherPressed = false;
        HotkeyCaptured?.Invoke(hk);
    }

    private bool Matches(Hotkey h, uint vk) =>
        vk == h.Key && IsDown(VkControl) == h.Ctrl && IsDown(VkShift) == h.Shift && IsDown(VkMenu) == h.Alt;

    /// <summary>把任意修饰键 VK（中性 0x10/0x11/0x12 或左右物理 0xA0..0xA5）映射成逻辑修饰键。</summary>
    private enum Mod { None, Ctrl, Shift, Alt }
    private static Mod ToMod(uint vk) => vk switch
    {
        VkControl or VkLControl or VkRControl => Mod.Ctrl,
        VkShift or VkLShift or VkRShift => Mod.Shift,
        VkMenu or VkLMenu or VkRMenu => Mod.Alt,
        _ => Mod.None
    };

    // 孤立修饰键匹配：主键必须为 0，且松开的修饰键正好是该热键置位的那个
    private static bool BareMatches(Hotkey h, uint vk)
    {
        if (h.Key != 0) return false;
        return ToMod(vk) switch
        {
            Mod.Ctrl => h.Ctrl,
            Mod.Shift => h.Shift,
            Mod.Alt => h.Alt,
            _ => false
        };
    }

    private static Hotkey ModifierHotkey(uint vk) => ToMod(vk) switch
    {
        Mod.Ctrl => new() { Ctrl = true, Key = 0, KeyName = "Ctrl" },
        Mod.Shift => new() { Shift = true, Key = 0, KeyName = "Shift" },
        Mod.Alt => new() { Alt = true, Key = 0, KeyName = "Alt" },
        _ => new() { Key = 0 }
    };

    // 判断一个 vk 是不是修饰键（中性 + 左右物理 VK 都算），捕获时用于忽略、等真正的键
    private static bool IsModifierKey(uint vk) => ToMod(vk) != Mod.None;

    private static bool IsDismissKey(uint vk) => vk is VkBackspace or VkDelete or VkReturn;

    private static bool IsDown(int vk) => (Native.GetKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        // 直接摘钩，线程随后随进程退出
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyHook);
        _mouseHook = IntPtr.Zero;
        _keyHook = IntPtr.Zero;
        _mouseProc = null;
        _keyProc = null;
        // 通知消息泵线程退出（之前只摘钩，线程到进程结束时才回收）
        if (_threadId != 0) Native.PostThreadMessageW(_threadId, Native.WmQuit, IntPtr.Zero, IntPtr.Zero);
    }
}
