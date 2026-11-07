using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace JyutPingPopup;

/// <summary>
/// Windows 自带离线 OCR（Windows.Media.Ocr）。不联网、不泄密，中文随系统语言包提供。
/// 把一张截图 Bitmap 识别成文本；识别不到中文时返回空串，由上层决定要不要弹面板。
/// </summary>
public static class Ocr
{
    public static bool IsChineseSupported()
    {
        try
        {
            return OcrEngine.IsLanguageSupported(new Language("zh-Hans"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>把截图识别成文本。引擎不可用或非中文语境时返回空串。</summary>
    public static async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        OcrEngine? engine = OcrEngine.TryCreateFromLanguage(new Language("zh-Hans"));
        if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return "";

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        // PNG -> IRandomAccessStream -> SoftwareBitmap -> OCR 要求的 Bgra8/Premultiply
        using var ras = ms.AsRandomAccessStream();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ras);
        using SoftwareBitmap src = await decoder.GetSoftwareBitmapAsync();
        using SoftwareBitmap converted =
            SoftwareBitmap.Convert(src, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        OcrResult? result = await engine.RecognizeAsync(converted);
        return result?.Text ?? "";
    }
}
