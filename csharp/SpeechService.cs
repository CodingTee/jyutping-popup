using System.Speech.Synthesis;

namespace JyutPingPopup;

/// <summary>本机粤语语音（SAPI5）：Tracy 女声 / Danny 男声。</summary>
public sealed record CantoneseVoice(string Name, bool IsFemale);

/// <summary>
/// 本地 TTS：枚举系统粤语语音、懒加载 SpeechSynthesizer、按需朗读。
/// 语音包属 Windows 组件，不随软件分发；没有 zh-HK 语音时 HasVoice=false，功能自动降级。
/// </summary>
public static class SpeechService
{
    private static readonly object Gate = new();
    private static SpeechSynthesizer? _synth;
    private static IReadOnlyList<CantoneseVoice>? _voices;

    /// <summary>枚举系统里文化标记为 zh-HK 的语音，失败返回空列表。</summary>
    public static IReadOnlyList<CantoneseVoice> GetVoices()
    {
        if (_voices != null) return _voices;
        try
        {
            using var s = new SpeechSynthesizer();
            var list = new List<CantoneseVoice>();
            foreach (var installed in s.GetInstalledVoices())
            {
                if (!installed.Enabled) continue;
                var info = installed.VoiceInfo;
                var culture = info.Culture?.Name ?? "";
                if (!culture.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)) continue;
                var name = info.Name ?? "";
                var female = name.Contains("Tracy", StringComparison.OrdinalIgnoreCase)
                             || info.Gender == VoiceGender.Female;
                list.Add(new CantoneseVoice(name, female));
            }
            _voices = list;
        }
        catch { _voices = Array.Empty<CantoneseVoice>(); }
        return _voices;
    }

    public static bool HasVoice => GetVoices().Count > 0;

    /// <summary>按偏好选声音：优先请求的性别，没有则退回任意一台粤语语音。</summary>
    private static CantoneseVoice? Pick(bool preferFemale)
    {
        var voices = GetVoices();
        if (voices.Count == 0) return null;
        return voices.FirstOrDefault(v => v.IsFemale == preferFemale) ?? voices[0];
    }

    /// <summary>朗读中文原文。text 应是汉字串（非粤拼），让 zh-HK 语音按粤语读音处理。</summary>
    /// <returns>是否成功发起朗读（无语音/无文本/异常时返回 false）。</returns>
    public static bool Speak(string text, bool preferFemale)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var voice = Pick(preferFemale);
        if (voice == null) return false;
        try
        {
            lock (Gate)
            {
                _synth ??= new SpeechSynthesizer();
                _synth.SelectVoice(voice.Name);
                _synth.SpeakAsyncCancelAll();   // 连点时取消上一段，不叠加
                _synth.SpeakAsync(text);
            }
            return true;
        }
        catch { return false; }
    }

    // 自测观察口：允许注入语音列表，验证选声逻辑不依赖本机是否装了粤语包
    internal static void SetVoicesForTest(IReadOnlyList<CantoneseVoice>? voices) => _voices = voices;
    internal static void ResetForTest() { lock (Gate) { _synth?.Dispose(); _synth = null; } _voices = null; }
    internal static CantoneseVoice? PickForTest(bool preferFemale) => Pick(preferFemale);
}
