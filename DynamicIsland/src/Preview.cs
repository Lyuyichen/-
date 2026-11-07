using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Island
{
    /// <summary>离屏渲染（--preview=目录），用来检查界面。</summary>
    internal static class Preview
    {
        public static void Render(string dir)
        {
            Directory.CreateDirectory(dir);

            BitmapSource sharp = Wallpaper(false);
            BitmapSource blurred = Wallpaper(true);

            var snap = new Snapshot
            {
                Cpu = 42, Gpu = 67, GpuOk = true,
                RamPct = 61, RamUsedGb = 9.8, RamTotalGb = 16,
                NetDown = 4.6 * 1024 * 1024, NetUp = 0.42 * 1024 * 1024, NetPct = 4.2, NetLinkMbps = 1000,
                CpuTemp = 58, GpuTemp = 64, Hz = 144, Fps = double.NaN
            };

            var busy = new Snapshot
            {
                Cpu = 93, Gpu = 88, GpuOk = true,
                RamPct = 84, RamUsedGb = 13.4, RamTotalGb = 16,
                NetDown = 82 * 1024 * 1024, NetUp = 12 * 1024 * 1024, NetPct = 92, NetLinkMbps = 1000,
                CpuTemp = 86, GpuTemp = 81, Hz = 144, Fps = double.NaN
            };

            Shot(Path.Combine(dir, "01-compact.png"), false, snap, sharp, blurred);
            Shot(Path.Combine(dir, "02-expanded.png"), true, snap, sharp, blurred);
            Shot(Path.Combine(dir, "03-expanded-busy.png"), true, busy, sharp, blurred);
        }

        static void Shot(string path, bool expanded, Snapshot s, BitmapSource sharp, BitmapSource blurred)
        {
            var v = DashboardView.Build();
            v.Update(s, false);
            v.SetClock(DateTime.Now);

            double w = expanded ? DashboardView.ExpandedWidth : 138;
            double h = expanded ? DashboardView.ExpandedHeight() : DashboardView.CompactHeight;

            v.Shell.Width = w;
            v.Shell.Height = h;
            v.Shell.CornerRadius = new CornerRadius(expanded ? DashboardView.CornerExpanded : DashboardView.CornerCollapsed);
            v.CompactLayer.Opacity = expanded ? 0 : 1;
            v.ExpandedLayer.Opacity = expanded ? 1 : 0;
            v.ExpandedLayer.IsHitTestVisible = expanded;

            var root = new Grid { Width = 640, Height = 360 };
            var wallBrush = new ImageBrush(sharp) { Stretch = Stretch.Fill };
            root.Background = wallBrush;
            v.Shell.Margin = new Thickness(0, 26, 0, 0);
            root.Children.Add(v.Shell);

            root.Measure(new Size(640, 360));
            root.Arrange(new Rect(0, 0, 640, 360));
            root.UpdateLayout();

            v.UpdateClip();
            v.SetPointer(new Point(w * 0.36, h * 0.5));

            // 岛屿位置（2x 设备像素）→ 从模糊壁纸上裁出那一块当作"背后的桌面"
            double x = (640 - w) / 2.0;
            const double y = 26;
            var crop = new Int32Rect((int)Math.Round(x * 2), (int)Math.Round(y * 2),
                Math.Min(1280, (int)Math.Round(w * 2)), Math.Min(720, (int)Math.Round(h * 2)));
            v.BackdropBrush.ImageSource = new CroppedBitmap(blurred, crop);
            v.BackdropHost.Opacity = 1.0;
            v.SetBackdropLuminance(0.28);

            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(1280, 720, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        /// <summary>造一张"桌面壁纸"：彩色光斑，用来展示毛玻璃。</summary>
        static BitmapSource Wallpaper(bool blur)
        {
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                var full = new Rect(0, 0, 640, 360);
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x1A, 0x22, 0x33), Color.FromRgb(0x08, 0x0A, 0x11), 70), null, full);

                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x86, 0x44, 0x5C, 0xC8)), null, new Point(120, 90), 150, 150);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x7A, 0xC0, 0x52, 0x74)), null, new Point(540, 120), 130, 130);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x6E, 0x33, 0xA8, 0x8C)), null, new Point(360, 320), 190, 120);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x5E, 0xD8, 0x94, 0x46)), null, new Point(560, 330), 120, 120);
            }

            var host = new Grid { Width = 640, Height = 360 };
            host.Children.Add(new DrawingVisualElement(dv));
            if (blur)
            {
                host.Effect = new BlurEffect
                {
                    Radius = 42,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
            }

            host.Measure(new Size(640, 360));
            host.Arrange(new Rect(0, 0, 640, 360));
            host.UpdateLayout();

            var rtb = new RenderTargetBitmap(1280, 720, 192, 192, PixelFormats.Pbgra32);
            rtb.Render(host);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>把已经画好的 DrawingVisual 塞进可视化树。</summary>
        class DrawingVisualElement : FrameworkElement
        {
            readonly Visual _visual;
            public DrawingVisualElement(Visual v) { _visual = v; }
            protected override int VisualChildrenCount { get { return 1; } }
            protected override Visual GetVisualChild(int index) { return _visual; }
        }
    }
}


