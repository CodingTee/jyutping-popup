using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Forms; // Cursor, Screen

namespace JyutPingPopup;

/// <summary>
/// 全屏截图遮罩，拖框选区域。全程用物理像素（Cursor.Position / CopyFromScreen 都是物理像素），
/// 画到界面上时再用窗口的 TransformFromDevice 把物理像素转成 WPF 逻辑像素（DIP）。
/// </summary>
public partial class RegionSelector : Window
{
    // 选完一框后回调：传入截图 Bitmap（调用方负责 Dispose）；null 表示取消（框太小 / 按 Esc）
    public event Action<Bitmap?>? CaptureCompleted;

    private Matrix _toDip = Matrix.Identity; // 物理像素 -> WPF 逻辑像素
    private bool _dragging;
    private System.Drawing.Point _startPhys;  // 物理像素
    private System.Drawing.Point _originPhys; // 虚拟屏幕原点（副屏在主屏左侧/上方时为负）
    private System.Drawing.Size _sizePhys;
    private Bitmap? _screen;       // 按下热键那一刻的全屏快照
    private BitmapSource? _src;    // 同一张快照的 WPF 版本，用作遮罩背景

    public RegionSelector()
    {
        InitializeComponent();
        // 覆盖整块虚拟屏幕（多显示器合一）。Bounds 是物理像素，而 WPF 的 Left/Width
        // 是逻辑像素（DIP），所以这里先存着，等拿到 DPI 变换再换算（见 OnSourceInitialized）。
        var minX = int.MaxValue; var minY = int.MaxValue;
        var maxX = int.MinValue; var maxY = int.MinValue;
        foreach (var s in Screen.AllScreens)
        {
            var b = s.Bounds; // 物理像素
            minX = System.Math.Min(minX, b.X); minY = System.Math.Min(minY, b.Y);
            maxX = System.Math.Max(maxX, b.Right); maxY = System.Math.Max(maxY, b.Bottom);
        }
        _originPhys = new System.Drawing.Point(minX, minY);
        _sizePhys = new System.Drawing.Size(maxX - minX, maxY - minY);

        // 必须在窗口显示之前拍快照：窗口一旦显示就会被 CopyFromScreen 拍进去。
        try { _screen = GrabVirtualScreen(); } catch { _screen = null; }
    }

    private Bitmap GrabVirtualScreen()
    {
        var bmp = new Bitmap(_sizePhys.Width, _sizePhys.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(_originPhys.X, _originPhys.Y, 0, 0, _sizePhys);
        }
        return bmp;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is PresentationSource ps && ps.CompositionTarget != null)
        {
            _toDip = ps.CompositionTarget.TransformFromDevice;
            var o = _toDip.Transform(new System.Windows.Point(_originPhys.X, _originPhys.Y));
            var s = _toDip.Transform(new System.Windows.Point(_sizePhys.Width, _sizePhys.Height));
            Left = o.X; Top = o.Y;
            Width = s.X; Height = s.Y;
        }
        else
        {
            // 拿不到 DPI 变换就退回直接用物理像素（单屏 100% 缩放下与上面等价）
            Left = _originPhys.X; Top = _originPhys.Y;
            Width = _sizePhys.Width; Height = _sizePhys.Height;
        }

        if (_screen != null)
        {
            try { _src = ToBitmapSource(_screen); } catch { _src = null; }
            Bg.Source = _src;
            SelImg.Source = _src;
        }
    }

    // 布局完成后才拿得到 ActualWidth，此时再把提示条摆到主屏顶部居中
    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        var primary = Screen.PrimaryScreen?.Bounds
                      ?? new System.Drawing.Rectangle(_originPhys, _sizePhys);
        var p = _toDip.Transform(new System.Windows.Point(
            primary.X + primary.Width / 2.0 - _originPhys.X,
            primary.Y + 48 - _originPhys.Y));
        Canvas.SetLeft(HintBox, p.X - HintBox.ActualWidth / 2);
        Canvas.SetTop(HintBox, p.Y);

        // ✕ 贴在提示条右侧：水平方向紧接提示条右边缘再留 8px，垂直方向与提示条居中对齐。
        // 必须按提示条的实际尺寸定位，不能钉屏幕右上角：提示条是居中的，
        // 钉角落会让两者隔得很远，看上去不像一组。
        Canvas.SetLeft(CloseButton, p.X + HintBox.ActualWidth / 2 + 8);
        Canvas.SetTop(CloseButton, p.Y + (HintBox.ActualHeight - CloseButton.ActualHeight) / 2);
    }

    // 点右上角 ✕：取消截图。Preview 在窗口的 OnMouseLeftButtonDown（冒泡）之前拦截，
    // 设 Handled=true 阻止窗口把它当成划选起点，于是不会误触发拖框。
    private void CloseButton_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        CaptureCompleted?.Invoke(null); // null 表示取消（与 Esc 同语义）
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // 快照可能几 MB，窗口一关就还回去（裁剪出来的 clone 由调用方负责释放）
        _screen?.Dispose();
        _screen = null;
        _src = null;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CaptureCompleted?.Invoke(null);
            Close();
        }
    }

    protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragging = true;
        _startPhys = System.Windows.Forms.Cursor.Position;
        SelRect.Visibility = Visibility.Visible;
        SelImg.Visibility = Visibility.Visible;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragging) return;
        DrawSelection(_startPhys, System.Windows.Forms.Cursor.Position);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var end = System.Windows.Forms.Cursor.Position;
        var rect = ToRect(_startPhys, end);
        SelRect.Visibility = Visibility.Collapsed;
        SelImg.Visibility = Visibility.Collapsed;

        if (rect.Width < 4 || rect.Height < 4)
        {
            CaptureCompleted?.Invoke(null); // 太小视为取消
            Close();
            return;
        }

        // 从快照里裁，而不是重新 CopyFromScreen：否则会把遮罩自己也截进去
        var crop = new System.Drawing.Rectangle(
            rect.X - _originPhys.X, rect.Y - _originPhys.Y, rect.Width, rect.Height);
        if (_screen != null && crop.X >= 0 && crop.Y >= 0 &&
            crop.Right <= _screen.Width && crop.Bottom <= _screen.Height)
        {
            CaptureCompleted?.Invoke(_screen.Clone(crop, _screen.PixelFormat));
        }
        else
        {
            // 拿不到快照才退回实时抓屏（极少数情况）
            using var fallback = new Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(fallback))
            {
                g.CopyFromScreen(rect.X, rect.Y, 0, 0, new System.Drawing.Size(rect.Width, rect.Height));
            }
            CaptureCompleted?.Invoke((Bitmap)fallback.Clone());
        }
        Close();
    }

    private void DrawSelection(System.Drawing.Point a, System.Drawing.Point b)
    {
        var r = ToRect(a, b);
        // 画布坐标是相对窗口原点的：必须先减掉虚拟屏幕原点再换算成逻辑像素。
        // 直接用屏幕坐标会导致副屏在主屏左侧/上方时（原点为负）选框整体偏移。
        var tl = _toDip.Transform(new System.Windows.Point(r.X - _originPhys.X, r.Y - _originPhys.Y));
        var br = _toDip.Transform(new System.Windows.Point(r.Right - _originPhys.X, r.Bottom - _originPhys.Y));
        Canvas.SetLeft(SelRect, tl.X);
        Canvas.SetTop(SelRect, tl.Y);
        SelRect.Width = br.X - tl.X;
        SelRect.Height = br.Y - tl.Y;
        // 把快照按选框裁剪后盖在变暗层之上，选区就恢复原亮度
        SelImg.Clip = new RectangleGeometry(new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y));
    }

    private static System.Drawing.Rectangle ToRect(System.Drawing.Point a, System.Drawing.Point b) =>
        new(
            System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y),
            System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>GDI 位图 -> WPF BitmapSource。拷一份像素字节，不留下需要手工释放的 GDI 句柄。</summary>
    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var r = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(r, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * bmp.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var src = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96,
                PixelFormats.Pbgra32, null, bytes, stride);
            src.Freeze();
            return src;
        }
        finally { bmp.UnlockBits(data); }
    }
}
