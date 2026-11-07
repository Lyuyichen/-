using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Island
{
    internal static class Palette
    {
        public static readonly Brush TextPrimary = Solid(0xF2FFFFFF);
        public static readonly Brush TextSecondary = Solid(0xFFB4B4BF);
        public static readonly Brush TextTertiary = Solid(0xFF8E8E98);
        public static readonly Brush Track = Solid(0x24FFFFFF);
        public static readonly Brush Arc = Solid(0xF0FFFFFF);

        public static readonly Brush FallbackBg = Gradient(0xF5121216, 0xFA0A0A0D);
        public static readonly Brush GlassTint = Gradient(0x44000000, 0x5A000000);

        public static SolidColorBrush Solid(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static Brush Gradient(uint top, uint bottom)
        {
            var b = new LinearGradientBrush(
                Color.FromArgb((byte)(top >> 24), (byte)(top >> 16), (byte)(top >> 8), (byte)top),
                Color.FromArgb((byte)(bottom >> 24), (byte)(bottom >> 16), (byte)(bottom >> 8), (byte)bottom),
                new Point(0.5, 0), new Point(0.5, 1));
            b.Freeze();
            return b;
        }

        /// <summary>玻璃边缘的高光描边：左上亮、右下暗。</summary>
        public static Brush Rim()
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x4C, 0xFF, 0xFF, 0xFF), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF), 0.45));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 1.0));
            b.Freeze();
            return b;
        }

        /// <summary>玻璃内部自上而下的柔光。</summary>
        public static Brush Sheen()
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1)
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x07, 0xFF, 0xFF, 0xFF), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x07, 0xFF, 0xFF, 0xFF), 1.0));
            b.Freeze();
            return b;
        }

        /// <summary>让白字在亮背景上也读得清。</summary>
        public static void Shadow(TextBlock t, double blur, double opacity)
        {
            t.Effect = new DropShadowEffect
            {
                BlurRadius = blur,
                ShadowDepth = 0,
                Opacity = opacity,
                Color = Colors.Black,
                RenderingBias = RenderingBias.Performance
            };
        }
    }

    internal static class Ui
    {
        public static readonly FontFamily Sf = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");

        public static TextBlock Text(string s, double size, Brush fg)
        {
            var t = new TextBlock
            {
                Text = s,
                FontSize = size,
                Foreground = fg,
                FontFamily = Sf,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(t, TextRenderingMode.Grayscale);
            return t;
        }
    }

    /// <summary>一个指标格子：圆环 + 中间数值 + 底部标签。</summary>
    internal class MetricTile
    {
        public Grid Root;
        public ArcGauge Gauge;
        public TextBlock Value;
        public TextBlock Unit;
        public TextBlock Sub;
        public TextBlock Label;

        public static readonly double RingSize = 72;

        public static MetricTile Build(string label)
        {
            var t = new MetricTile();
            t.Root = new Grid { Width = RingSize };
            t.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            t.Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var ringHost = new Grid { Width = RingSize, Height = RingSize };

            t.Gauge = new ArcGauge
            {
                Width = RingSize,
                Height = RingSize,
                StrokeThickness = 6.5,
                StartAngle = 135,
                SweepAngle = 270,
                TrackBrush = Palette.Track,
                ArcBrush = Palette.Arc
            };
            ringHost.Children.Add(t.Gauge);

            var center = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var valueRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            t.Value = Ui.Text("0", 17, Palette.TextPrimary);
            t.Value.FontWeight = FontWeights.SemiBold;
            Palette.Shadow(t.Value, 6, 0.45);
            t.Unit = Ui.Text("%", 9, Palette.TextSecondary);
            t.Unit.Margin = new Thickness(1, 0, 0, 3);
            t.Unit.VerticalAlignment = VerticalAlignment.Bottom;
            valueRow.Children.Add(t.Value);
            valueRow.Children.Add(t.Unit);

            t.Sub = Ui.Text("", 7.5, Palette.TextTertiary);
            t.Sub.HorizontalAlignment = HorizontalAlignment.Center;
            t.Sub.TextAlignment = TextAlignment.Center;
            t.Sub.TextWrapping = TextWrapping.Wrap;
            t.Sub.Width = RingSize - 18;
            t.Sub.Margin = new Thickness(0, 1, 0, 0);

            center.Children.Add(valueRow);
            center.Children.Add(t.Sub);
            ringHost.Children.Add(center);
            Grid.SetRow(ringHost, 0);
            t.Root.Children.Add(ringHost);

            t.Label = Ui.Text(label, 9.5, Palette.TextSecondary);
            t.Label.HorizontalAlignment = HorizontalAlignment.Center;
            t.Label.Margin = new Thickness(0, 5, 0, 0);
            Grid.SetRow(t.Label, 1);
            t.Root.Children.Add(t.Label);

            return t;
        }

        public void Set(string value, string unit, string sub, double ratio, bool animate)
        {
            Value.Text = value;
            Unit.Text = unit;
            Sub.Text = sub;

            double target = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
            if (target > 0.0001 && target < 0.022) target = 0.022;

            if (animate)
            {
                Gauge.BeginAnimation(ArcGauge.ValueProperty,
                    new DoubleAnimation(target, TimeSpan.FromMilliseconds(420))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    });
            }
            else
            {
                Gauge.Value = target;
            }
        }

        public void SetLabel(string label) { Label.Text = label; }
    }

    /// <summary>灵动岛的视觉结构：液态玻璃底 + 紧凑态 + 五个圆弧。</summary>
    internal class DashboardView
    {
        public Border Shell;
        public Grid Host;

        public Border BackdropHost;
        public ImageBrush BackdropBrush;
        public Border TintLayer;
        public Border SheenLayer;
        public Ellipse PointerGlow;

        public Grid CompactLayer;
        public Grid ExpandedLayer;

        public ArcGauge MiniGauge;
        public TextBlock MiniLabel;
        public TextBlock MiniValue;
        public TextBlock Clock;

        public MetricTile Cpu, Gpu, Ram, Net;

        public static readonly double ExpandedWidth = 16 + 16 + 4 * MetricTile.RingSize + 3 * 12;
        public static readonly double CompactHeight = 36;

        public static double CornerCollapsed { get { return 18; } }
        public static double CornerExpanded { get { return 34; } }

        public static DashboardView Build()
        {
            var v = new DashboardView();

            v.Shell = new Border
            {
                Background = Palette.FallbackBg,
                BorderBrush = Palette.Rim(),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(CornerCollapsed),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                SnapsToDevicePixels = true
            };
            v.Host = new Grid();
            v.Host.ClipToBounds = true;
            v.Shell.Child = v.Host;

            // ---- 玻璃层 ----
            v.BackdropBrush = new ImageBrush { Stretch = Stretch.Fill };
            v.BackdropHost = new Border
            {
                Background = v.BackdropBrush,
                Opacity = 0.96,
                IsHitTestVisible = false
            };
            v.BackdropHost.SetBinding(Border.CornerRadiusProperty,
                new System.Windows.Data.Binding("CornerRadius")
                {
                    Source = v.Shell,
                    Mode = System.Windows.Data.BindingMode.OneWay
                });
            RenderOptions.SetBitmapScalingMode(v.BackdropHost, BitmapScalingMode.HighQuality);

            v.TintLayer = new Border
            {
                Background = Palette.GlassTint,
                IsHitTestVisible = false
            };
            v.TintLayer.SetBinding(Border.CornerRadiusProperty,
                new System.Windows.Data.Binding("CornerRadius") { Source = v.Shell });

            v.SheenLayer = new Border { Background = Palette.Sheen(), IsHitTestVisible = false };
            v.SheenLayer.SetBinding(Border.CornerRadiusProperty,
                new System.Windows.Data.Binding("CornerRadius") { Source = v.Shell });

            v.PointerGlow = new Ellipse
            {
                Width = 260,
                Height = 260,
                IsHitTestVisible = false,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.7));
            v.PointerGlow.Fill = glow;

            var vignette = new Border { IsHitTestVisible = false };
            var vg = new RadialGradientBrush { Center = new Point(0.5, 0.45), GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.75, RadiusY = 0.95 };
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.45));
            vg.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1.0));
            vignette.Background = vg;
            vignette.SetBinding(Border.CornerRadiusProperty, new System.Windows.Data.Binding("CornerRadius") { Source = v.Shell });

            v.Host.Children.Add(v.BackdropHost);
            v.Host.Children.Add(v.TintLayer);
            v.Host.Children.Add(vignette);
            v.Host.Children.Add(v.SheenLayer);

            // ---- 紧凑态 ----
            v.CompactLayer = new Grid();
            var compact = new Grid { Margin = new Thickness(11, 0, 11, 0) };
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            v.MiniGauge = new ArcGauge
            {
                Width = 24, Height = 24, StrokeThickness = 3.4,
                StartAngle = 135, SweepAngle = 270,
                TrackBrush = Palette.Track,
                ArcBrush = Palette.Arc,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(v.MiniGauge, 0);
            compact.Children.Add(v.MiniGauge);

            var miniText = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            v.MiniLabel = Ui.Text("GPU", 9, Palette.TextTertiary);
            v.MiniLabel.Margin = new Thickness(0, 1, 5, 0);
            v.MiniValue = Ui.Text("--", 12.5, Palette.TextPrimary);
            v.MiniValue.FontWeight = FontWeights.SemiBold;
            Palette.Shadow(v.MiniValue, 5, 0.4);
            miniText.Children.Add(v.MiniLabel);
            miniText.Children.Add(v.MiniValue);
            Grid.SetColumn(miniText, 1);
            compact.Children.Add(miniText);

            v.Clock = Ui.Text("--:--", 12.5, Palette.Solid(0xFFEDEDF3));
            v.Clock.FontWeight = FontWeights.SemiBold;
            v.Clock.Margin = new Thickness(10, 0, 0, 0);
            Palette.Shadow(v.Clock, 6, 0.5);
            Grid.SetColumn(v.Clock, 2);
            compact.Children.Add(v.Clock);

            v.CompactLayer.Children.Add(compact);

            // ---- 展开态 ----
            v.ExpandedLayer = new Grid { Opacity = 0 };
            var panel = new Border { Padding = new Thickness(16, 14, 16, 14) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            v.Cpu = MetricTile.Build("CPU");
            v.Gpu = MetricTile.Build("GPU");
            v.Ram = MetricTile.Build("内存");
            v.Net = MetricTile.Build("以太网");


            var tiles = new MetricTile[] { v.Cpu, v.Gpu, v.Ram, v.Net };
            for (int i = 0; i < tiles.Length; i++)
            {
                if (i > 0) tiles[i].Root.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(tiles[i].Root);
            }

            panel.Child = row;
            v.ExpandedLayer.Children.Add(panel);

            v.Host.Children.Add(v.CompactLayer);
            v.Host.Children.Add(v.ExpandedLayer);

            return v;
        }

        public static double ExpandedHeight()
        {
            return 14 + MetricTile.RingSize + 5 + 14 + 14;
        }

        int _tintAlpha = -1;
        double _lastLuma = 0.3;

        /// <summary>用户可调的玻璃蒙版强度（百分比，0=几乎全透）。</summary>
        public int TintPercent = 0;

        public void ApplyTint()
        {
            _tintAlpha = -1;
            SetBackdropLuminance(_lastLuma);
        }

        /// <summary>按背景亮度自适应蒙版：背景越亮，黑色蒙版越重，保证白字可读。</summary>
        public void SetBackdropLuminance(double luma)
        {
            if (luma < 0) luma = 0; else if (luma > 1) luma = 1;
            _lastLuma = luma;
            double adapt = 0.70 + 0.80 * luma;
            int a = (int)(255.0 * TintPercent / 100.0 * adapt);
            if (a < 0) a = 0; else if (a > 200) a = 200;
            if (Math.Abs(a - _tintAlpha) < 6) return;
            _tintAlpha = a;

            int top = Math.Max(0, a - 0x12);
            int bottom = Math.Min(255, a + 0x12);
            var b = new LinearGradientBrush(
                Color.FromArgb((byte)top, 0x03, 0x03, 0x06),
                Color.FromArgb((byte)bottom, 0x03, 0x03, 0x06),
                new Point(0.5, 0), new Point(0.5, 1));
            b.Freeze();
            TintLayer.Background = b;
        }
        public void SetClock(DateTime now) { Clock.Text = now.ToString("HH:mm"); }

        /// <summary>内容层按当前尺寸和圆角做圆角裁剪，动画中跟着变。</summary>
        RectangleGeometry _clip;

        public void UpdateClip()
        {
            double w = Shell.ActualWidth, h = Shell.ActualHeight;
            if (w <= 0 || h <= 0) return;
            double r = Shell.CornerRadius.TopLeft;
            if (r < 0) r = 0;
            if (r > Math.Min(w, h) / 2) r = Math.Min(w, h) / 2;

            if (_clip == null)
            {
                _clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
                Host.Clip = _clip;
                return;
            }
            _clip.Rect = new Rect(0, 0, w, h);
            _clip.RadiusX = r;
            _clip.RadiusY = r;
        }

        /// <summary>指针高光跟随鼠标。</summary>
        public void SetPointer(Point p)
        {
            if (PointerGlow == null) return;
            PointerGlow.Margin = new Thickness(p.X - PointerGlow.Width / 2, p.Y - PointerGlow.Height / 2, 0, 0);
        }

        public void Update(Snapshot s, bool animate = true)
        {
            double cpu = Clean(s.Cpu);
            Cpu.Set(cpu.ToString("0"), "%", TempText(s.CpuTemp), cpu / 100.0, animate);

            if (s.GpuOk)
                Gpu.Set(Clean(s.Gpu).ToString("0"), "%", TempText(s.GpuTemp), Clean(s.Gpu) / 100.0, animate);
            else
                Gpu.Set("--", "%", "不可用", 0, animate);

            Ram.Set(Clean(s.RamPct).ToString("0"), "%",
                s.RamTotalGb > 0 ? (s.RamUsedGb.ToString("0.0") + "/" + s.RamTotalGb.ToString("0") + "G") : "",
                Clean(s.RamPct) / 100.0, animate);

            Net.Set(Clean(s.NetPct).ToString("0"), "%",
                "↓" + Rate(s.NetDown) + " ↑" + Rate(s.NetUp),
                Clean(s.NetPct) / 100.0, animate);

            bool gpuOk = s.GpuOk;
            double gpu = Clean(s.Gpu);
            MiniValue.Text = gpuOk ? gpu.ToString("0") + "%" : "--";
            MiniGauge.BeginAnimation(ArcGauge.ValueProperty,
                new DoubleAnimation(gpuOk ? Math.Max(0, Math.Min(1, gpu / 100.0)) : 0, TimeSpan.FromMilliseconds(420))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        static double Clean(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0) return 0;
            return v > 100 ? 100 : v;
        }

        static string TempText(double t)
        {
            if (double.IsNaN(t)) return "温度 —";
            return t.ToString("0") + "°C";
        }

        static string Rate(double bytesPerSec)
        {
            double kb = bytesPerSec / 1024.0;
            if (kb < 1000) return kb.ToString("0") + "K";
            double mb = kb / 1024.0;
            return (mb >= 10 ? mb.ToString("0") : mb.ToString("0.0")) + "M";
        }
    }
}
























