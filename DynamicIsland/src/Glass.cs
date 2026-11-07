using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Island
{
    /// <summary>
    /// 毛玻璃背景：把自己（WDA_EXCLUDEFROMCAPTURE）排除后，截取窗口背后的桌面，
    /// 缩小 → 盒式模糊 → 提饱和 → 放大，喂给一个 ImageBrush。
    /// 截取失败（远程桌面、DRM 等）时自动退回静态玻璃。
    /// </summary>
    internal class GlassBackdrop : IDisposable
    {
        const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr hdcDest, int xd, int yd, int wd, int hd,
            IntPtr hdcSrc, int xs, int ys, int ws, int hs, int rop);
        [DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);

        const int SRCCOPY = 0x00CC0020;
        const int HALFTONE = 4;
        const int DOWNSCALE = 12;          // 缩到 1/9 再放大 = 基础模糊
        const double SATURATION = 1.10;   // 苹果的玻璃比背景更"艳"

        readonly FrameworkElement _anchor;
        readonly ImageBrush _brush;
        DispatcherTimer _timer;
        bool _disposed;
        bool _excluded;
        bool _fake;
        int _failures;
        int _skip;

        public bool Enabled { get; set; }
        public bool Available { get { return _excluded && _failures < 6; } }
        public double LastLuminance { get; private set; }

        /// <summary>抓不到背景时的兜底（静态玻璃）。</summary>
        public event Action<bool> AvailabilityChanged;

        /// <summary>每次抓到背景后报一次平均亮度（0..1），用来调暗色蒙版。</summary>
        public event Action<double> Captured;

        public GlassBackdrop(FrameworkElement anchor, ImageBrush brush)
        {
            _anchor = anchor;
            _brush = brush;
            Enabled = true;
        }

        public void Attach(IntPtr hwnd)
        {
            if (Environment.GetEnvironmentVariable("ISLAND_NOGLASS") == "1")
            {
                _excluded = false;
                AppEnv.Log("毛玻璃: 已禁用（调试）");
                return;
            }

            bool fake = Environment.GetEnvironmentVariable("ISLAND_FAKE_BACKDROP") == "1";
            _fake = fake;
            bool noExclude = fake || Environment.GetEnvironmentVariable("ISLAND_GLASS_NOEXCLUDE") == "1";
            if (!noExclude)
            {
                try { _excluded = SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); }
                catch { _excluded = false; }
            }
            else
            {
                _excluded = true;   // 调试：不排除自己，会拍到自反馈
            }

            AppEnv.Log("毛玻璃: 排除窗口截图=" + _excluded);

            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Normal,
                delegate { Tick(); }, _anchor.Dispatcher);
            _timer.Start();
            _anchor.Dispatcher.BeginInvoke(new Action(Tick), DispatcherPriority.Loaded);
        }

        public void Dispose()
        {
            _disposed = true;
            if (_timer != null) { _timer.Stop(); _timer = null; }
        }

        void Tick()
        {
            if (_disposed || !Enabled || !_excluded || _failures >= 6) return;
            if (!_anchor.IsVisible || _anchor.ActualWidth < 8 || _anchor.ActualHeight < 8) return;
            if (_skip > 0) { _skip--; return; }


            try
            {
                System.Windows.Point topLeft = _anchor.PointToScreen(new System.Windows.Point(0, 0));
                double scale = DpiScale(_anchor);
                int srcW = (int)Math.Round(_anchor.ActualWidth * scale);
                int srcH = (int)Math.Round(_anchor.ActualHeight * scale);
                if (srcW < 8 || srcH < 8) return;

                int dstW = Math.Max(10, srcW / DOWNSCALE);
                int dstH = Math.Max(6, srcH / DOWNSCALE);

                double luma;
                BitmapSource shot = _fake
                    ? FakeCapture(dstW, dstH, out luma)
                    : Capture((int)topLeft.X, (int)topLeft.Y, srcW, srcH, dstW, dstH, out luma);
                if (shot == null) { _failures++; return; }

                _brush.ImageSource = shot;
                LastLuminance = luma;

                if (Environment.GetEnvironmentVariable("ISLAND_GLASS_DUMP") == "1")
                {
                    try
                    {
                        SavePng(shot, AppEnv.State("glass.png"));
                        AppEnv.Log("glass rect=" + (int)topLeft.X + "," + (int)topLeft.Y +
                                   " size=" + srcW + "x" + srcH + " scale=" + scale.ToString("0.00") +
                                   " luma=" + luma.ToString("0.00"));
                    }
                    catch { }
                }

                var cap = Captured;
                if (cap != null) cap(luma);

                if (_failures > 0)
                {
                    _failures = 0;
                    var h = AvailabilityChanged;
                    if (h != null) h(true);
                }
            }
            catch
            {
                _failures++;
                if (_failures >= 6)
                {
                    var h = AvailabilityChanged;
                    if (h != null) h(false);
                }
            }
        }

        /// <summary>动画期间降低抓取频率。</summary>
        public void HoldFor(int ticks) { _skip = Math.Max(_skip, ticks); }

        /// <summary>调试用：造一张彩色背景，走同样的模糊/提饱和流程，用来肉眼检查玻璃质感。</summary>
        static BitmapSource FakeCapture(int dw, int dh, out double luma)
        {
            luma = 0.5;
            using (var bmp = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, dw, dh),
                        System.Drawing.Color.FromArgb(20, 33, 60), System.Drawing.Color.FromArgb(8, 9, 14), 70f))
                        g.FillRectangle(bg, 0, 0, dw, dh);

                    using (var b1 = new SolidBrush(System.Drawing.Color.FromArgb(190, 60, 90, 220)))
                        g.FillEllipse(b1, -dw / 4, -dh / 3, dw, dh);
                    using (var b2 = new SolidBrush(System.Drawing.Color.FromArgb(170, 220, 80, 120)))
                        g.FillEllipse(b2, dw / 2, -dh / 2, (int)(dw * 0.8), (int)(dh * 1.1));
                    using (var b3 = new SolidBrush(System.Drawing.Color.FromArgb(150, 60, 200, 170)))
                        g.FillEllipse(b3, dw / 4, dh / 2, (int)(dw * 0.7), (int)(dh * 0.9));
                }

                var rect = new Rectangle(0, 0, dw, dh);
                BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] buffer;
                int stride;
                try
                {
                    stride = data.Stride;
                    buffer = new byte[stride * dh];
                    Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                    Blur(buffer, stride, dw, dh, 2);
                    Blur(buffer, stride, dw, dh, 2);
                    Saturate(buffer, stride, dw, dh, SATURATION);
                    luma = AverageLuma(buffer, stride, dw, dh);
                    Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                }
                finally { bmp.UnlockBits(data); }

                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    var frame = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frozen = BitmapFrame.Create(frame);
                    frozen.Freeze();
                    return frozen;
                }
            }
        }

        static BitmapSource Capture(int x, int y, int w, int h, int dw, int dh, out double luma)
        {
            luma = 0.5;
            using (var small = new Bitmap(dw, dh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(small))
                {
                    IntPtr screen = GetDC(IntPtr.Zero);
                    if (screen == IntPtr.Zero) return null;
                    IntPtr dst = g.GetHdc();
                    try
                    {
                        SetStretchBltMode(dst, HALFTONE);
                        SetBrushOrgEx(dst, 0, 0, IntPtr.Zero);
                        if (!StretchBlt(dst, 0, 0, dw, dh, screen, x, y, w, h, SRCCOPY)) return null;
                    }
                    finally
                    {
                        g.ReleaseHdc(dst);
                        ReleaseDC(IntPtr.Zero, screen);
                    }
                }

                var rect = new Rectangle(0, 0, dw, dh);
                BitmapData data = small.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                byte[] buffer;
                int stride;
                try
                {
                    stride = data.Stride;
                    buffer = new byte[stride * dh];
                    Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                }
                finally { small.UnlockBits(data); }

                if (AverageLuma(buffer, stride, dw, dh) < 0.02) return null;   // 全黑 = 没抓到

                Blur(buffer, stride, dw, dh, 2);
                Blur(buffer, stride, dw, dh, 2);
                Blur(buffer, stride, dw, dh, 1);
                Saturate(buffer, stride, dw, dh, SATURATION);

                luma = AverageLuma(buffer, stride, dw, dh);

                var src = BitmapSource.Create(dw, dh, 96, 96, PixelFormats.Pbgra32, null, buffer, stride);
                src.Freeze();
                return src;
            }
        }

        static double AverageLuma(byte[] b, int stride, int w, int h)
        {
            double sum = 0;
            int n = 0;
            for (int y = 0; y < h; y += 2)
            {
                int row = y * stride;
                for (int x = 0; x < w; x += 2)
                {
                    int i = row + x * 4;
                    sum += 0.0722 * b[i] + 0.7152 * b[i + 1] + 0.2126 * b[i + 2];
                    n++;
                }
            }
            return n == 0 ? 0 : sum / n / 255.0;
        }

        /// <summary>可分离盒式模糊（BGRA，忽略 alpha）。</summary>
        static void Blur(byte[] b, int stride, int w, int h, int r)
        {
            var tmp = new byte[b.Length];
            // 横向
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int ch = 0; ch < 3; ch++)
                {
                    int sum = 0, count = 0;
                    for (int x = -r; x <= r; x++)
                    {
                        int xx = x < 0 ? 0 : (x >= w ? w - 1 : x);
                        sum += b[row + xx * 4 + ch];
                        count++;
                    }
                    for (int x = 0; x < w; x++)
                    {
                        tmp[row + x * 4 + ch] = (byte)(sum / count);
                        int addX = x + r + 1; if (addX >= w) addX = w - 1;
                        int subX = x - r; if (subX < 0) subX = 0;
                        sum += b[row + addX * 4 + ch] - b[row + subX * 4 + ch];
                    }
                }
                for (int x = 0; x < w; x++) tmp[row + x * 4 + 3] = 255;
            }
            // 纵向
            for (int x = 0; x < w; x++)
            {
                for (int ch = 0; ch < 3; ch++)
                {
                    int sum = 0, count = 0;
                    for (int y = -r; y <= r; y++)
                    {
                        int yy = y < 0 ? 0 : (y >= h ? h - 1 : y);
                        sum += tmp[yy * stride + x * 4 + ch];
                        count++;
                    }
                    for (int y = 0; y < h; y++)
                    {
                        b[y * stride + x * 4 + ch] = (byte)(sum / count);
                        int addY = y + r + 1; if (addY >= h) addY = h - 1;
                        int subY = y - r; if (subY < 0) subY = 0;
                        sum += tmp[addY * stride + x * 4 + ch] - tmp[subY * stride + x * 4 + ch];
                    }
                }
            }
        }

        static void Saturate(byte[] b, int stride, int w, int h, double sat)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    double bb = b[i], g = b[i + 1], r = b[i + 2];
                    double gray = 0.114 * bb + 0.587 * g + 0.299 * r;
                    bb = gray + (bb - gray) * sat;
                    g = gray + (g - gray) * sat;
                    r = gray + (r - gray) * sat;
                    b[i] = Clamp(bb + 10);
                    b[i + 1] = Clamp(g + 10);
                    b[i + 2] = Clamp(r + 10);
                    b[i + 3] = 255;
                }
            }
        }

        static byte Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (byte)v;
        }

        static void SavePng(BitmapSource src, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
        }

        static double DpiScale(Visual v)
        {
            PresentationSource src = PresentationSource.FromVisual(v);
            if (src == null || src.CompositionTarget == null) return 1.0;
            double s = src.CompositionTarget.TransformToDevice.M11;
            return s <= 0 ? 1.0 : s;
        }
    }
}









