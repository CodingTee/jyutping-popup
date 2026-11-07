using System.IO;
using System.Text;

namespace JyutPingPopup;

/// <summary>一个字的注音结果：原字、主读音、全部读音。</summary>
public sealed class JyutItem
{
    public string Char { get; init; } = "";
    public string Reading { get; init; } = "";
    public IReadOnlyList<string> All { get; init; } = Array.Empty<string>();
}

/// <summary>Rime 粤拼码表的加载与注音。逻辑与 Python 版 dict_jyut.py 一致。</summary>
public static class Jyutping
{
    public static string DataDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "data");

    // 用 Lazy 而不用 ??=：??= 不是原子操作，两个线程同时首次访问会各加载一遍
    // （3.4 万 + 10 万行，约 110ms），更糟的是一个线程边写 Dictionary 另一个边读。
    // ExecutionAndPublication 保证只跑一次初始化，其余线程等结果。
    private static readonly Lazy<Dictionary<string, string[]>> _chars =
        new(() => Load("chars.dict.yaml"), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Dictionary<string, string[]>> _words =
        new(() => Load("words.dict.yaml"), LazyThreadSafetyMode.ExecutionAndPublication);

    public static Dictionary<string, string[]> Chars => _chars.Value;
    public static Dictionary<string, string[]> Words => _words.Value;

    /// <summary>汉字范围，与 Python 版 CJK 正则一致（含扩展 A 与兼容表意文字）。</summary>
    public static bool IsCjk(char c) =>
        (c >= '\u3400' && c <= '\u4dbf') ||
        (c >= '\u4e00' && c <= '\u9fff') ||
        (c >= '\uf900' && c <= '\ufaff');

    /// <summary>只保留汉字。</summary>
    public static string KeepCjk(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
            if (IsCjk(c)) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// 码表格式：词&lt;TAB&gt;粤拼&lt;TAB&gt;权重%。跳过 ... 之前的 YAML 头。
    /// 码表按读音字母序排，不是按常用度：实测「没写权重列」的那条才是主读音，
    /// 所以主读音一律排到最前（Python 版曾在这里把「嘅」选成 ge2）。
    /// </summary>
    private static Dictionary<string, string[]> Load(string filename)
    {
        var raw = new Dictionary<string, List<(string Reading, bool HasWeight)>>();
        var started = false;
        foreach (var rawLine in File.ReadLines(Path.Combine(DataDir, filename), Encoding.UTF8))
        {
            var line = rawLine.TrimEnd('\n').TrimEnd('\r');
            if (!started)
            {
                started = line.Trim() == "...";
                continue;
            }
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length < 2) continue;
            var word = parts[0].Trim();
            var reading = parts[1].Trim();
            if (word.Length == 0 || reading.Length == 0) continue;
            if (!raw.TryGetValue(word, out var bucket))
                raw[word] = bucket = new List<(string, bool)>();
            if (bucket.All(b => b.Reading != reading))
                bucket.Add((reading, parts.Length >= 3));
        }

        var table = new Dictionary<string, string[]>(raw.Count);
        foreach (var (word, bucket) in raw)
        {
            var ordered = new List<string>(bucket.Count);
            foreach (var (reading, hasWeight) in bucket)
                if (!hasWeight) ordered.Add(reading);
            foreach (var (reading, hasWeight) in bucket)
                if (hasWeight) ordered.Add(reading);
            table[word] = ordered.ToArray();
        }
        return table;
    }

    private static string? LookupWord(string segment)
    {
        if (Words.TryGetValue(segment, out var readings)) return readings[0];
        var trad = S2Hk.Convert(segment);
        if (trad != segment && Words.TryGetValue(trad, out readings)) return readings[0];
        return null;
    }

    private static string[] LookupChar(string ch)
    {
        // 简体字在码表里登记的可能是其生僻本字读音（例：体 = bèn，主读音 ban6），
        // 单字转繁无歧义时优先按繁体查（体 -> 體 tai2）；有歧义的（干/面/了）
        // 仍用字自己的条目，避免被 OpenCC 的默认选择带偏（干 -> 幹 gon3 是错的）。
        var unique = S2Hk.ConvertUnique(ch);
        if (unique != null && unique.Length == 1 && Chars.TryGetValue(unique, out var readings))
            return readings;
        if (Chars.TryGetValue(ch, out readings)) return readings;
        var trad = S2Hk.Convert(ch);
        if (trad != ch && trad.Length == 1 && Chars.TryGetValue(trad, out readings)) return readings;
        return Array.Empty<string>();
    }

    /// <summary>
    /// 逐字注音。先按词表做最长匹配（词表能消歧：「重量」zung6 vs「重新」cung4），
    /// 词表没命中才退回单字；单字多音时全部带回，由界面决定怎么显示。
    /// </summary>
    public static List<JyutItem> Annotate(string text, int maxWord = 5)
    {
        var result = new List<JyutItem>();
        int i = 0;
        while (i < text.Length)
        {
            string? hit = null;
            var hitLength = 0;
            var hitSegment = "";
            for (var length = Math.Min(maxWord, text.Length - i); length > 1; length--)
            {
                var segment = text.Substring(i, length);
                var reading = LookupWord(segment);
                if (reading != null)
                {
                    hit = reading;
                    hitLength = length;
                    hitSegment = segment;
                    break;
                }
            }

            if (hit != null)
            {
                var syllables = hit.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (var k = 0; k < hitSegment.Length; k++)
                {
                    var one = k < syllables.Length ? syllables[k] : "";
                    var raw = hitSegment[k].ToString();
                    // 显示用的是繁体字形，All 也按繁体查，界面和读音才一致
                    // （否则简体「么」会带出 jiu1/maa1 这类自己的变体读音）。
                    var tradChar = S2Hk.Convert(raw);
                    var full = tradChar.Length == 1 ? LookupChar(tradChar) : LookupChar(raw);
                    result.Add(new JyutItem
                    {
                        Char = raw,
                        Reading = one,
                        // Reading 用词的消歧结果（重量 -> cung5）；All 回填该字字典里的
                        // 完整读音，否则「什」之类在词里会丢掉多音信息、不标红。
                        All = full.Length > 0 ? full : (one.Length > 0 ? new[] { one } : Array.Empty<string>()),
                    });
                }
                i += hitLength;
                continue;
            }

            var ch = text[i].ToString();
            if (IsCjk(text[i]))
            {
                var readings = LookupChar(ch);
                result.Add(new JyutItem
                {
                    Char = ch,
                    Reading = readings.Length > 0 ? readings[0] : "",
                    All = readings,
                });
            }
            i++;
        }
        return result;
    }
}
