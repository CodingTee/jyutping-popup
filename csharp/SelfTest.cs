using System.Diagnostics;
using System.IO;
using System.Text;

namespace JyutPingPopup;

/// <summary>自检：把注音结果按固定格式写盘，用来和 Python 版逐条对照。</summary>
public static class SelfTest
{
    public static readonly string[] Samples =
    {
        "你好",
        "粤语",
        "学习粤拼",
        "今天天气很好",
        "我食饭",
        "你好吗",
        "重量",
        "重新",
        "嘅",
        "佢",
        "凹",
        "好",
        "这是一个测试",
        "我哋去边度食饭",
        "佢哋喺度做紧乜",
        "广东话好难学",
        "香港人讲粤语",
        "呢个系咩嚟㗎",
        "食咗饭未",
        "唔该晒你",
    };

    public static void Run(string outputPath)
    {
        var sw = Stopwatch.StartNew();
        var charsCount = Jyutping.Chars.Count;
        var wordsCount = Jyutping.Words.Count;
        var loadMs = sw.Elapsed.TotalMilliseconds;

        var sb = new StringBuilder();
        sb.AppendLine($"# chars={charsCount} words={wordsCount} load_ms={loadMs:F1}");
        sw.Restart();
        foreach (var sample in Samples)
        {
            var items = Jyutping.Annotate(sample);
            var body = string.Join(";", items.Select(i =>
                $"{i.Char}:{i.Reading}:{string.Join("/", i.All)}"));
            sb.AppendLine($"{sample}\t{body}");
        }
        var annotateMs = sw.Elapsed.TotalMilliseconds;
        sb.AppendLine($"# annotate_ms={annotateMs:F2} samples={Samples.Length}");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }
}
