using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace JyutPingPopup;

public partial class App : System.Windows.Application
{
    private const string MutexName = "JyutPingPopupSingleton";
    private const string ActivateEventName = "JyutPingPopupActivate"; // 重复启动的实例用来"戳"已有实例
    private const int MaxChars = 80; // 实测：选一整页会渲染上千个格子，必须截断
    private const string AppKey = @"Software\JyutPingPopup";
    private const string HotkeyQuitValue = "HotkeyQuit";
    private const string HotkeyPauseValue = "HotkeyPause";
    private const string HotkeyOcrValue = "HotkeyOcr";

    private enum CaptureTarget { Quit, Pause, Ocr }

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static Drawing.Bitmap? _trayBitmap;

    private PopupWindow? _popup;
    // 弹窗世代号：每收起一次 +1。取词是异步的（剪贴板兜底最长约 1.3s），
    // 拿它判断「结果回来时用户有没有已经把弹窗收掉」，过期结果不再弹出来。
    private long _popupEpoch;
    private CaptureService? _capture;
    private OcrPanel? _ocrPanel; // 当前打开的截图识字结果面板，避免连续 OCR 时一个叠一个
    private RegionSelector? _regionSelector; // 当前打开的拖框遮罩，避免连按热键叠两层
    private InputHooks? _hooks;
    private Forms.NotifyIcon? _tray;
    private MainWindow? _main;
    private bool _paused; // 暂停时不取词、不碰剪贴板，进程仍常驻
    private Hotkey _quitHotkey = Hotkey.DefaultQuit();
    private Hotkey _pauseHotkey = Hotkey.DefaultPause();
    private Hotkey _ocrHotkey = Hotkey.DefaultOcr();
    private CaptureTarget _captureTarget;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && e.Args[0] == "--selftest")
        {
            SelfTest.Run(Path.Combine(AppContext.BaseDirectory, "selftest_cs.txt"));
            Shutdown();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-ui")
        {
            SelfTestUi.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-clip")
        {
            SelfTestClip.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-cliprace")
        {
            SelfTestClipRace.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-footer")
        {
            SelfTestFooter.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-ocr")
        {
            SelfTestOcr.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-region")
        {
            SelfTestRegion.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-ocrpanel")
        {
            SelfTestOcrPanel.Run();
            return;
        }

        if (e.Args.Length > 0 && e.Args[0] == "--selftest-clipimage")
        {
            SelfTestClipImage.Run();
            return;
        }

        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // 已有实例在跑：戳它一下让它在托盘弹气泡，本实例安静退出（绝不打开第二个）
            try
            {
                using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
                ev.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { /* 旧版没在监听，直接退出即可 */ }
            Shutdown();
            return;
        }

        // 本实例是首个：建一个可被重复启动实例"戳"的事件，用于弹托盘提示
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        StartActivateListener();

        _popup = new PopupWindow();
        _popup.Show();   // 先建出 HWND，之后用 Visibility 控制显隐
        _popup.HidePopup();

        _capture = new CaptureService();
        _capture.Captured += OnCaptured;
        _hooks = new InputHooks();
        _hooks.LeftButtonUp += OnLeftButtonUp;      // 每次松开：只收起弹窗
        _hooks.SelectionMade += OnSelectionMade;     // 只有划选：才去取词
        _hooks.EscapePressed += () => DismissPopup();
        _hooks.DismissRequested += () => DismissPopup();   // Backspace / Delete / Enter
        _hooks.QuitRequested += () => Dispatcher.BeginInvoke(new Action(RequestExit));
        _hooks.PauseToggleRequested += () => Dispatcher.BeginInvoke(new Action(TogglePause));
        _hooks.OcrRequested += () => Dispatcher.BeginInvoke(new Action(StartOcr));

        // 从注册表恢复自定义热键（缺省 Ctrl+Shift+Q / Ctrl+Shift+P / Ctrl+Shift+L）
        _quitHotkey = LoadHotkey(HotkeyQuitValue, Hotkey.DefaultQuit());
        _pauseHotkey = LoadHotkey(HotkeyPauseValue, Hotkey.DefaultPause());
        _ocrHotkey = LoadHotkey(HotkeyOcrValue, Hotkey.DefaultOcr());
        _hooks.SetQuitHotkey(_quitHotkey);
        _hooks.SetPauseHotkey(_pauseHotkey);
        _hooks.SetOcrHotkey(_ocrHotkey);
        _hooks.HotkeyCaptured += hk => Dispatcher.BeginInvoke(new Action(() => OnHotkeyCaptured(hk)));

        _main = new MainWindow(); // 默认不 Show，仅在点击托盘时弹出
        _main.PauseToggleRequested += TogglePause;
        _main.OcrRequested += StartOcr;
        _main.CustomizeQuit += () => StartCustomize(CaptureTarget.Quit);
        _main.CustomizePause += () => StartCustomize(CaptureTarget.Pause);
        _main.CustomizeOcr += () => StartCustomize(CaptureTarget.Ocr);
        _main.RestoreDefaultsRequested += RestoreDefaultHotkeys;
        _main.CaptureCancelled += () => _hooks?.StopCapture();
        _main.SetPauseState(_paused);
        _main.SetQuitHotkeyDisplay(_quitHotkey.Format());
        _main.SetPauseHotkeyDisplay(_pauseHotkey.Format());
        _main.SetOcrHotkeyDisplay(_ocrHotkey.Format());

        _tray = new Forms.NotifyIcon
        {
            Icon = MakeTrayIcon(),
            Text = "粤拼弹窗（运行中）\n左键点托盘开/关窗口，右键可退出",
            Visible = true,
            ContextMenuStrip = MakeTrayMenu(),
        };
        _tray.MouseClick += (_, ev) =>
        {
            if (ev.Button == Forms.MouseButtons.Left)
                ToggleMain();
        };
    }

    // 后台线程：等重复启动的实例发来信号，再回到 UI 线程弹托盘气泡
    private void StartActivateListener()
    {
        var listener = new Thread(() =>
        {
            try
            {
                while (_activateEvent!.WaitOne())
                    Dispatcher.BeginInvoke(() => ShowAlreadyRunningBalloon());
            }
            catch (ObjectDisposedException) { /* 退出时事件被释放，线程自然结束 */ }
        }) { IsBackground = true };
        listener.Start();
    }

    private void ShowAlreadyRunningBalloon()
    {
        if (_tray == null) return;
        _tray.BalloonTipTitle = "粤拼弹窗";
        _tray.BalloonTipText = "已经在运行了，看右下角托盘的「粤」图标。";
        _tray.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _tray.ShowBalloonTip(3000);
    }

    // 左键点托盘：窗口可见则收起，否则弹出并置顶
    private void ToggleMain()
    {
        if (_main == null) return;
        if (_main.IsVisible)
            _main.Hide();
        else
        {
            _main.Show();
            _main.Activate();
        }
    }

    // 暂停/恢复切换：Ctrl+Shift+P 与设置窗口按钮共用此入口
    private void TogglePause()
    {
        _paused = !_paused;
        UpdateTrayForPause();
        _main?.SetPauseState(_paused);
    }

    // 只刷新托盘 tooltip（含当前暂停热键），不弹气泡——用于热键变更等不需要提示的场景
    private void UpdateTrayText()
    {
        if (_tray == null) return;
        _tray.Text = _paused
            ? $"粤拼弹窗（已暂停）\n选中汉字不再弹粤拼；按 {_pauseHotkey.Format()} 恢复"
            : "粤拼弹窗（运行中）\n左键点托盘开/关窗口，右键可退出";
    }

    private void UpdateTrayForPause()
    {
        UpdateTrayText();
        if (_tray == null) return;
        _tray.BalloonTipTitle = "粤拼弹窗";
        _tray.BalloonTipText = _paused ? "已暂停捕获" : "已恢复捕获";
        _tray.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _tray.ShowBalloonTip(1500);
    }

    // 设置窗口点「自定义」后进入监听；钩子捕获到组合键或 Esc 取消后回调
    private void StartCustomize(CaptureTarget target)
    {
        _captureTarget = target;
        if (target == CaptureTarget.Quit) _main?.SetCapturingQuit(true);
        else if (target == CaptureTarget.Pause) _main?.SetCapturingPause(true);
        else _main?.SetCapturingOcr(true);
        _hooks?.StartCapture();
    }

    private void OnHotkeyCaptured(Hotkey? hk)
    {
        _hooks?.StopCapture();
        // 先复位三个「自定义」按钮，避免监听中切换目标时旧按钮卡在监听态
        _main?.SetCapturingQuit(false);
        _main?.SetCapturingPause(false);
        _main?.SetCapturingOcr(false);
        if (hk == null) return; // Esc 取消
        if (_captureTarget == CaptureTarget.Quit)
        {
            _quitHotkey = hk;
            SaveHotkey(HotkeyQuitValue, hk);
            _hooks?.SetQuitHotkey(hk);
            _main?.SetQuitHotkeyDisplay(hk.Format());
        }
        else if (_captureTarget == CaptureTarget.Pause)
        {
            _pauseHotkey = hk;
            SaveHotkey(HotkeyPauseValue, hk);
            _hooks?.SetPauseHotkey(hk);
            _main?.SetPauseHotkeyDisplay(hk.Format());
        }
        else
        {
            _ocrHotkey = hk;
            SaveHotkey(HotkeyOcrValue, hk);
            _hooks?.SetOcrHotkey(hk);
            _main?.SetOcrHotkeyDisplay(hk.Format());
        }
    }

    // 「恢复默认热键」：把全局关闭 / 暂停 / 截图识字三个热键都重置成默认值并落盘
    private void RestoreDefaultHotkeys()
    {
        // 若正在监听自定义，先取消，避免之后按键被静默绑架
        _hooks?.StopCapture();
        _main?.SetCapturingQuit(false);
        _main?.SetCapturingPause(false);
        _main?.SetCapturingOcr(false);

        _quitHotkey = Hotkey.DefaultQuit();
        _pauseHotkey = Hotkey.DefaultPause();
        _ocrHotkey = Hotkey.DefaultOcr();
        SaveHotkey(HotkeyQuitValue, _quitHotkey);
        SaveHotkey(HotkeyPauseValue, _pauseHotkey);
        SaveHotkey(HotkeyOcrValue, _ocrHotkey);
        _hooks?.SetQuitHotkey(_quitHotkey);
        _hooks?.SetPauseHotkey(_pauseHotkey);
        _hooks?.SetOcrHotkey(_ocrHotkey);
        _main?.SetQuitHotkeyDisplay(_quitHotkey.Format());
        _main?.SetPauseHotkeyDisplay(_pauseHotkey.Format());
        _main?.SetOcrHotkeyDisplay(_ocrHotkey.Format());
        UpdateTrayText(); // 暂停提示里显示的是当前暂停键，恢复默认后同步（不动暂停状态，不弹气泡）
    }

    /// <summary>截图识字：弹全屏拖框遮罩，松手后离线 OCR，再把中文逐字注上粤拼弹面板。</summary>
    private void StartOcr()
    {
        DismissPopup();   // 拖框时别让上一张卡片还悬在上面挡视线
        _regionSelector?.Close();   // 上一次没完成的拖框遮罩先关掉，避免连按热键叠两层
        var selector = new RegionSelector();
        _regionSelector = selector;
        selector.CaptureCompleted += async (bmp) =>
        {
            if (bmp == null) return; // 取消（框太小或按 Esc）
            // OCR 是异步的，跑的这几百毫秒里用户可能又按了热键或按 Esc。
            // 先记下世代号，结果回来时比对，不一致就丢弃（与划选取词同一套做法）。
            var epoch = Interlocked.Read(ref _popupEpoch);
            try
            {
                // 中文包没装就别白跑 OCR（原先放到 UI 线程识别完才检查，等于先白干一轮）
                if (!Ocr.IsChineseSupported())
                {
                    bmp.Dispose();
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                        System.Windows.MessageBox.Show(
                            "本机未安装中文 OCR 语言包。\n请到 Windows 设置 → 时间和语言 → 语言 → 中文(简体) → 勾选「光学字符识别」后重试。",
                            "截图识字", MessageBoxButton.OK, MessageBoxImage.Warning)));
                    return;
                }

                // OCR 在 WinRT 里是 await；丢到线程池跑，避免与 WPF UI 同步上下文互锁
                var text = await Task.Run(() => Ocr.RecognizeAsync(bmp));
                bmp.Dispose();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 期间被取消（Esc / 再次按热键）→ 丢弃这次结果。否则先手后到时，
                    // 旧 OCR 会晚回来把新结果面板顶掉，用户看到的是上一次的截图内容。
                    if (Interlocked.Read(ref _popupEpoch) != epoch) return;
                    _ocrPanel?.Close();   // 上一次的结果面板还在就先关掉，别叠着开
                    var panel = new OcrPanel();
                    panel.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_ocrPanel, panel)) _ocrPanel = null;
                    };
                    _ocrPanel = panel;
                    panel.ShowResults(text);
                    panel.Show();
                }));
            }
            catch
            {
                bmp.Dispose();
            }
        };
        selector.Show();
        selector.Activate();
    }

    private static Hotkey LoadHotkey(string valueName, Hotkey fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            var raw = key?.GetValue(valueName) as string;
            return Hotkey.ParseCompact(raw, fallback);
        }
        catch { return fallback; }
    }

    private static void SaveHotkey(string valueName, Hotkey hk)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            key?.SetValue(valueName, hk.ToCompact(), RegistryValueKind.String);
        }
        catch { /* 静默：无权限不影响划词主功能 */ }
    }

    private void OnLeftButtonUp(int x, int y)
    {
        // 点任何地方都收起弹窗，但不取词
        DismissPopup();
    }

    /// <summary>收起弹窗并推进世代号，让还在飞行中的取词结果作废。</summary>
    private void DismissPopup()
    {
        Interlocked.Increment(ref _popupEpoch);
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() =>
            {
                _popup?.HidePopup();
                _ocrPanel?.Close(); // 截图识字结果面板也跟着收起（之前只能点关闭按钮）
            });
    }

    private void OnSelectionMade(int x, int y)
    {
        // 在自家窗口里划选（比如设置窗口里选中热键文本）不触发捕获，否则会弹出粤拼卡片
        if (IsOurWindow(x, y)) return;
        // 只有划选手势才取词：避免每次点击都去借一次剪贴板
        DismissPopup();
        if (_paused) return; // 暂停时不取词、不碰剪贴板
        if (!Dispatcher.HasShutdownStarted)
            // 世代号要在收起之后取，这样本次结果才算「有效的」
            _capture?.Request(x, y, Interlocked.Read(ref _popupEpoch));
    }

    // 坐标处的窗口是否属于当前进程：自家窗口的划选不算外部选区
    private static bool IsOurWindow(int x, int y)
    {
        var hwnd = Native.WindowFromPoint(new Native.Point { X = x, Y = y });
        if (hwnd == IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == (uint)System.Environment.ProcessId;
    }

    private void OnCaptured(int x, int y, string text, string source, long epoch)
    {
        // 取词期间用户又收起过弹窗（点了别处 / 按了 Esc / 又划了一次），
        // 这条结果就过期了：再弹出来等于把用户刚关掉的卡片又糊回去。
        if (Interlocked.Read(ref _popupEpoch) != epoch) return;

        var note = "";
        var items = Jyutping.Annotate(text);
        // 先整段注音再截断：反过来先截再注音会把「重量」这类词劈开，
        // 单字「重」查出来是 cung4，而按词查才是正确的 cung5 loeng6。
        if (items.Count > MaxChars)
        {
            note = $"仅显示前 {MaxChars} 字（共 {items.Count} 字）";
            items = items.GetRange(0, MaxChars);
        }
        if (source == "Ctrl+C")
            note = (note.Length > 0 ? note + "   " : "") + "来源：剪贴板";
        if (Dispatcher.HasShutdownStarted) return; // 退出途中不再往 UI 线程塞渲染任务
        Dispatcher.BeginInvoke(() =>
        {
            _popup?.Render(items, note);
            _popup?.ShowAt(x, y);
        });
    }

    // 统一退出入口：先让主窗口放行（否则「常驻后台」会把关闭当成最小化），再结束进程
    private void RequestExit()
    {
        _main?.SetForceExit();
        Shutdown();
    }

    private Forms.ContextMenuStrip MakeTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => Dispatcher.BeginInvoke(new Action(ToggleMain)));
        menu.Items.Add("暂停/继续捕获", null, (_, _) => Dispatcher.BeginInvoke(new Action(TogglePause)));
        menu.Items.Add("截图识字", null, (_, _) => Dispatcher.BeginInvoke(new Action(StartOcr)));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(RequestExit)));
        return menu;
    }

    private static Drawing.Icon MakeTrayIcon()
    {
        // logo.ico 已作为 WPF Resource 打进 exe（含 16-256 多尺寸），托盘取 32px 帧
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/assets/logo.ico"));
        if (info?.Stream != null)
        {
            using var memory = new MemoryStream();
            info.Stream.CopyTo(memory);
            memory.Position = 0;
            return new Drawing.Icon(memory, 32, 32);
        }
        return MakeFallbackIcon();
    }

    // 资源缺失时的兜底：用系统字体现画一个「粤」字图标
    private static Drawing.Icon MakeFallbackIcon()
    {
        var bitmap = new Drawing.Bitmap(64, 64, Drawing.Imaging.PixelFormat.Format32bppArgb);
        _trayBitmap = bitmap;
        using var g = Drawing.Graphics.FromImage(bitmap);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);

        using var bg = new Drawing.SolidBrush(Drawing.Color.White);
        using var border = new Drawing.Pen(Drawing.Color.FromArgb(40, 40, 40), 4);
        g.FillEllipse(bg, 4, 4, 56, 56);
        g.DrawEllipse(border, 4, 4, 56, 56);

        using var font = new Drawing.Font("Microsoft YaHei", 30, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center,
        };
        using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(30, 30, 30));
        g.DrawString("粤", font, brush, new Drawing.RectangleF(0, 0, 64, 64), format);
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _main?.Close();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _trayBitmap?.Dispose(); // 托盘图标用的位图，退出时还回去
        _activateEvent?.Dispose(); // 释放后监听线程的 WaitOne 会抛 ObjectDisposedException 而自然结束
        _hooks?.Dispose();
        _capture?.Dispose();
        base.OnExit(e);
    }
}
