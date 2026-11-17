using Microsoft.Win32;

namespace JyutPingPopup;

/// <summary>发音开关与男女声偏好，存 HKCU\Software\JyutPingPopup，默认关闭。</summary>
public static class SpeechSettings
{
    private const string AppKey = @"Software\JyutPingPopup";
    private const string EnabledValue = "SpeakEnabled";
    private const string FemaleValue = "SpeakFemale";

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

    public static bool GetPreferFemale()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey);
            if (key?.GetValue(FemaleValue) is int i) return i != 0;
            return true;
        }
        catch { return true; }
    }

    public static void SetPreferFemale(bool female)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppKey);
            key?.SetValue(FemaleValue, female ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { /* 静默 */ }
    }
}
