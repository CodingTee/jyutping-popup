using System.IO;
using System.Text;

namespace JyutPingPopup;

/// <summary>
/// 简体转香港繁体。词典码表是繁体的，不转就查不到「学习」这类简体词。
/// 用的是 OpenCC 原始数据（STCharacters / STPhrases / HKVariants），自己做最长匹配，
/// 避免为一次转换拖进带分词器的 NuGet 包（实测会多出 18 MB 的 jieba 资源）。
/// </summary>
public static class S2Hk
{
    /// <summary>三张表打包一起加载，避免多个线程各加载一部分、看到半成品。</summary>
    private sealed class Maps
    {
        public Dictionary<string, string> S2T = new();
        public Dictionary<string, string> T2Hk = new();
        public HashSet<string> Ambiguous = new(); // 一简对多繁的字（如 干 => 幹 乾 干）
        public int S2tMax = 1;
        public int T2hkMax = 1;
    }

    private static readonly Lazy<Maps> _maps = new(LoadMaps, LazyThreadSafetyMode.ExecutionAndPublication);
    private static Maps M => _maps.Value;

    private const string OpenCcDir = "opencc";

    private static Dictionary<string, string> LoadMap(string filename, out int maxKeyLength, HashSet<string>? ambiguous = null)
    {
        var map = new Dictionary<string, string>();
        maxKeyLength = 1;
        var path = Path.Combine(Jyutping.DataDir, OpenCcDir, filename);
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var key = parts[0];
            var candidates = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // 值可能是多个候选（空格分隔），OpenCC 取第一个
            var value = candidates[0];
            if (key.Length == 0 || value.Length == 0) continue;
            map[key] = value;
            if (ambiguous != null && candidates.Length > 1) ambiguous.Add(key);
            if (key.Length > maxKeyLength) maxKeyLength = key.Length;
        }
        return map;
    }

    private static Maps LoadMaps()
    {
        var maps = new Maps();
        maps.S2T = LoadMap("STCharacters.txt", out var a, maps.Ambiguous);
        maps.T2Hk = LoadMap("HKVariants.txt", out var b);
        // 词组表用于消歧，例如「头发」不能按单字转成「頭髮」以外的形式
        var phrases = LoadMap("STPhrases.txt", out var c);
        foreach (var (k, v) in phrases) maps.S2T[k] = v;
        maps.S2tMax = Math.Max(a, c);
        maps.T2hkMax = b;
        return maps;
    }

    private static string Apply(Dictionary<string, string> map, int maxKeyLength, string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var matched = false;
            for (var length = Math.Min(maxKeyLength, text.Length - i); length >= 1; length--)
            {
                var segment = text.Substring(i, length);
                if (map.TryGetValue(segment, out var replacement))
                {
                    sb.Append(replacement);
                    i += length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
            {
                sb.Append(text[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    public static string Convert(string text)
    {
        if (text.Length == 0) return text;
        var traditional = Apply(M.S2T, M.S2tMax, text);
        return Apply(M.T2Hk, M.T2hkMax, traditional);
    }

    /// <summary>
    /// 单字转换且候选唯一时返回繁体字形；有歧义（如 干 => 幹 乾 干）或无映射返回 null。
    /// 查单字读音时只用无歧义结果：码表把简体字登记为其生僻本字
    /// （例：体 = bèn，ban6），此时该按唯一对应的繁体（體 tai2）查。
    /// </summary>
    public static string? ConvertUnique(string text)
    {
        if (text.Length == 0) return null;
        if (!M.S2T.TryGetValue(text, out var trad) || M.Ambiguous.Contains(text)) return null;
        // 与 Convert 的 S2T->T2Hk 路径保持一致，确保单字查音用的字形和显示字形完全相同
        return M.T2Hk.TryGetValue(trad, out var hk) ? hk : trad;
    }
}
