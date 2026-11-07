using System.Windows;
using System.Windows.Controls;

namespace JyutPingPopup;

/// <summary>截图识字结果面板：上半部显示原始识别文本，下半部把中文逐字注上粤拼。</summary>
public partial class OcrPanel : Window
{
    public OcrPanel()
    {
        InitializeComponent();
    }

    public void ShowResults(string rawText)
    {
        var chinese = Jyutping.KeepCjk(rawText);
        RawText.Text = "识别原文：" + (rawText.Length == 0 ? "（空）" : rawText);

        if (chinese.Length == 0)
        {
            Cells.Children.Add(new TextBlock
            {
                Text = "未识别到中文。",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)),
                FontSize = 13,
                Margin = new Thickness(2)
            });
            return;
        }

        foreach (var item in Jyutping.Annotate(chinese))
        {
            var trad = S2Hk.Convert(item.Char);
            var cell = new StackPanel
            {
                Margin = new Thickness(5, 2, 5, 6),
                MinWidth = 38
            };
            cell.Children.Add(new TextBlock
            {
                Text = trad,
                FontSize = 17,
                FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });
            var tone = item.All.Count > 1
                ? System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B)
                : System.Windows.Media.Color.FromRgb(0x5A, 0x5A, 0x5A);
            cell.Children.Add(new TextBlock
            {
                Text = item.Reading.Length > 0 ? item.Reading : "—",
                FontSize = 10,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new System.Windows.Media.SolidColorBrush(tone),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            });
            Cells.Children.Add(cell);
        }
    }
}
