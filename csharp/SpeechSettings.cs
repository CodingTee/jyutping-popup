using Microsoft.Win32;

namespace JyutPingPopup;

/// <summary>发音开关与男女声偏好，存 HKCU\Software\JyutPingPopup，默认关闭。</summary>
public static class SpeechSettings
{
    private const string AppKey = @"Software\JyutPingPopup";
    private const string EnabledValue = "SpeakEnabled";

    public static bool GetEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            return key?.GetValue(EnabledValue) is int i && i != 0;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            key?.SetValue(EnabledValue, on ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 静默：无权限不影响主功能 */ }
    }

}
