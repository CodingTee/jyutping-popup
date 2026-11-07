using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace JyutPingPopup;

public partial class MainWindow : Window
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "JyutPingPopup";
    private const string AppKey = @"Software\JyutPingPopup";
    private const string ResidentValue = "Resident";
    private const string ClipFallbackValue = "ClipFallback";

    private bool _forceExit;     // 由「全局关闭」/托盘退出/Ctrl+Shift+Q 置位，允许真正退出
    private bool _suppress;      // 回读时防止 Checked/Unchecked 事件递归
    private bool _capturing;     // 是否正处于热键监听态

    // 设置窗口的「暂停/继续」按钮点击时，通知 App 切换暂停状态
    public event Action? PauseToggleRequested;
    // 「自定义」按钮点击时，通知 App 进入对应热键的捕获模式
    public event Action? CustomizeQuit;
    public event Action? CustomizePause;
    public event Action? CustomizeOcr;
    public event Action? OcrRequested;
    // 「恢复默认热键」点击时，通知 App 把两个热键都重置为默认
    public event Action? RestoreDefaultsRequested;
    // 窗口在监听中途关闭时，通知 App 取消捕获
    public event Action? CaptureCancelled;

    public MainWindow()
    {
        InitializeComponent();
        ResidentBox.IsChecked = GetResident();
        AutoStartBox.IsChecked = GetAutoStart();
        ClipFallbackBox.IsChecked = GetClipFallback();
        CaptureService.AllowClipFallback = GetClipFallback();   // 启动时同步给取词服务
        Closing += MainWindow_Closing;
    }

    // 供 App 在「托盘退出 / Ctrl+Shift+Q」时调用，确保窗口能真正关掉并结束进程
    public void SetForceExit() => _forceExit = true;

    // App 切换暂停后回写 UI：按钮文案 + 顶部状态
    public void SetPauseState(bool paused)
    {
        PauseBtn.Content = paused ? "继续捕获" : "暂停捕获";
        StatusText.Text = paused
            ? "已暂停 · 划选汉字不再弹粤拼"
            : "运行中 · 划选汉字松手即弹粤拼";
    }

    // App 捕获到新热键后回写显示文字
    public void SetQuitHotkeyDisplay(string text) => QuitHotkeyText.Text = text;
    public void SetPauseHotkeyDisplay(string text) => PauseHotkeyText.Text = text;
    public void SetOcrHotkeyDisplay(string text) => OcrHotkeyText.Text = text;

    // 进入/退出「按下新快捷键…」的监听态
    public void SetCapturingQuit(bool on)
    {
        _capturing = on;
        QuitCustomBtn.Content = on ? "按下新快捷键…" : "自定义";
        QuitCustomBtn.IsEnabled = !on;
    }

    public void SetCapturingPause(bool on)
    {
        _capturing = on;
        PauseCustomBtn.Content = on ? "按下新快捷键…" : "自定义";
        PauseCustomBtn.IsEnabled = !on;
    }

    public void SetCapturingOcr(bool on)
    {
        _capturing = on;
        OcrCustomBtn.Content = on ? "按下新快捷键…" : "自定义";
        OcrCustomBtn.IsEnabled = !on;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_capturing) CaptureCancelled?.Invoke(); // 监听中途关窗：取消捕获，避免之后按键被静默绑架
        if (_forceExit) return;                 // 退出按钮/托盘退出/热键：正常结束
        if (GetResident())
        {
            e.Cancel = true;                   // 常驻：关窗口 = 最小化到托盘，进程继续
            Hide();
        }
        else
        {
            e.Cancel = false;                  // 非常驻：关窗口 = 退出整个程序
            System.Windows.Application.Current.Shutdown();
        }
    }

    private static string ExePath => Process.GetCurrentProcess().MainModule?.FileName ?? "";

    // ---- 常驻后台设置（缺省开）----
    private static bool GetResident()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            if (key?.GetValue(ResidentValue) is int i) return i != 0;
            return true;
        }
        catch { return true; }
    }

    private static void SetResident(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            key?.SetValue(ResidentValue, on ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 静默：无权限不影响划词主功能 */ }
    }

    // ---- 开机自启设置 ----
    private static bool GetAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            var v = key?.GetValue(RunValue) as string;
            return !string.IsNullOrEmpty(v) && v.Trim('"') == ExePath;
        }
        catch { return false; }
    }

    private static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enable)
                key.SetValue(RunValue, $"\"{ExePath}\"", RegistryValueKind.String);
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { /* 静默 */ }
    }

    private void ResidentBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _suppress = true;
        SetResident(ResidentBox.IsChecked == true);
        ResidentBox.IsChecked = GetResident();   // 回读，保证 UI 与实际一致
        _suppress = false;
    }

    private void AutoStartBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _suppress = true;
        SetAutoStart(AutoStartBox.IsChecked == true);
        AutoStartBox.IsChecked = GetAutoStart();
        _suppress = false;
    }

    // ---- 剪贴板兜底开关（缺省开）----
    // 关掉后划词只读 UIA，绝不发 Ctrl+C、绝不碰剪贴板，
    // 代价是少数不支持 UIA 的程序（部分 PDF / 远程桌面 / 终端）取不到词。
    private static bool GetClipFallback()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            if (key?.GetValue(ClipFallbackValue) is int i) return i != 0;
            return true;
        }
        catch { return true; }
    }

    private static void SetClipFallback(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            key?.SetValue(ClipFallbackValue, on ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 静默：无权限不影响划词主功能 */ }
    }

    private void ClipFallbackBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _suppress = true;
        SetClipFallback(ClipFallbackBox.IsChecked == true);
        ClipFallbackBox.IsChecked = GetClipFallback();
        CaptureService.AllowClipFallback = GetClipFallback();   // 立即生效，不用重启
        _suppress = false;
    }

    private void OffBtn_Click(object sender, RoutedEventArgs e) => RequestExit();
    private void PauseBtn_Click(object sender, RoutedEventArgs e) => PauseToggleRequested?.Invoke();
    private void OcrBtn_Click(object sender, RoutedEventArgs e) => OcrRequested?.Invoke();
    private void QuitCustomBtn_Click(object sender, RoutedEventArgs e) => CustomizeQuit?.Invoke();
    private void PauseCustomBtn_Click(object sender, RoutedEventArgs e) => CustomizePause?.Invoke();
    private void OcrCustomBtn_Click(object sender, RoutedEventArgs e) => CustomizeOcr?.Invoke();
    private void RestoreBtn_Click(object sender, RoutedEventArgs e) => RestoreDefaultsRequested?.Invoke();

    private void RequestExit()
    {
        _forceExit = true;
        System.Windows.Application.Current.Shutdown();
    }

    private void HideBtn_Click(object sender, RoutedEventArgs e) => Hide();
}
