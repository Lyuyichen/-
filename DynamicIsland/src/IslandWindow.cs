using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Island
{
    internal class IslandWindow : Window
    {
        const double StageWidth = 620;
        const double StageHeight = 320;

        readonly DashboardView _v;
        readonly Sampler _sampler;
        readonly GlassBackdrop _glass;
        DispatcherTimer _uiTimer, _clockTimer, _collapseTimer, _hoverTimer;

        bool _expanded, _pinned, _dragging, _moved, _hidden;
        DispatcherTimer _hideTimer;
        Point _dragStartScreen, _dragOrigin;
        DateTime _lastPollLog = DateTime.MinValue;

        readonly bool _demo;
        DateTime _demoStart = DateTime.Now;

        public IslandWindow(bool demo)
        {
            _demo = demo;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Title = "灵动岛";
            Width = StageWidth;
            Height = StageHeight;

            _v = DashboardView.Build();
            _v.Shell.Margin = new Thickness(0, 14, 0, 0);
            _v.Shell.Width = CompactWidth;
            _v.Shell.Height = DashboardView.CompactHeight;

            var root = new Grid();
            root.Children.Add(_v.Shell);
            Content = root;

            _sampler = new Sampler();
            _glass = new GlassBackdrop(_v.Shell, _v.BackdropBrush);
            _glass.AvailabilityChanged += delegate(bool ok) { ApplyGlassState(ok); };
            _glass.Captured += delegate(double luma) { _v.SetBackdropLuminance(luma); };

            SourceInitialized += delegate
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                Native.MakeHud(hwnd);
                _glass.Attach(hwnd);
            };
            Loaded += OnLoaded;
            Closed += delegate
            {
                try { _sampler.Dispose(); } catch { }
                try { _glass.Dispose(); } catch { }
            };
        }

        static double CompactWidth { get { return 156; } }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            PlaceWindow();
            Store.Load();
            _v.TintPercent = Store.Cfg.GlassTint;
            EnsureAutoStart();

            _v.SetClock(DateTime.Now);
            if (!_demo) _sampler.Start();

            _uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(220), DispatcherPriority.Normal,
                delegate { Refresh(); }, Dispatcher);
            _uiTimer.Start();

            _clockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
                delegate { _v.SetClock(DateTime.Now); }, Dispatcher);
            _clockTimer.Start();

            _collapseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal,
                delegate
                {
                    _collapseTimer.Stop();
                    if (!_pinned && _expanded && !_v.Shell.IsMouseOver) Collapse();
                }, Dispatcher);
            _collapseTimer.Stop();

            _hoverTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input,
                delegate { PollHover(); }, Dispatcher);
            _hoverTimer.Start();

            Hook();
            ApplyGlassState(_glass.Available);
            if (Environment.GetEnvironmentVariable("ISLAND_START_EXPANDED") == "1") { _pinned = true; Expand(); }
            Refresh();
            AppEnv.Log("灵动岛已启动" + (_demo ? "（演示模式）" : ""));
        }

        /// <summary>首次运行时自动注册开机自启（右键菜单可以关掉）。</summary>
        void EnsureAutoStart()
        {
            if (Store.Cfg.AutoStartInit) return;
            try
            {
                if (!AutoStart.IsEnabled()) AutoStart.Set(true);
                Store.Cfg.AutoStartInit = true;
                Store.Save();
                AppEnv.Log("已注册开机自启");
            }
            catch (Exception ex) { AppEnv.Log("autostart: " + ex.Message); }
        }

        void PlaceWindow()
        {
            Rect wa = SystemParameters.WorkArea;
            double x = wa.Left + (wa.Width - StageWidth) / 2;
            double y = wa.Top;
            Left = Math.Round(Math.Max(wa.Left - 40, Math.Min(x, wa.Right - 120)));
            Top = Math.Round(Math.Max(wa.Top - 10, Math.Min(y, wa.Bottom - 60)));
        }

        // ---------------- 数据 ----------------

        /// <summary>玻璃开/关：开的时候露出背后的模糊桌面，关的时候用实心深色。</summary>
        void ApplyGlassState(bool available)
        {
            bool on = available && _glass.Enabled;
            _v.BackdropHost.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(on ? 1.0 : 0.0, TimeSpan.FromMilliseconds(150)));
            _v.TintLayer.Background = on
                ? Palette.GlassTint
                : (Brush)new SolidColorBrush(Color.FromArgb(0x00, 0x00, 0x00, 0x00));
            AppEnv.Log("毛玻璃: " + (on ? "开" : "关"));
        }
        void Refresh()
        {
            Snapshot s = _demo ? DemoSnapshot() : _sampler.Current;
            _v.Update(s);
        }

        /// <summary>演示模式：造一组随时间起伏的假数据。</summary>
        Snapshot DemoSnapshot()
        {
            double t = (DateTime.Now - _demoStart).TotalSeconds;
            double cpu = 34 + 22 * Math.Sin(t / 3.1) + 6 * Math.Sin(t / 0.9);
            double gpu = 46 + 26 * Math.Sin(t / 4.3 + 1.1);
            double ram = 58 + 6 * Math.Sin(t / 9.0);
            double net = 12 + 10 * Math.Abs(Math.Sin(t / 2.2));

            return new Snapshot
            {
                Cpu = Clamp(cpu, 3, 99),
                Gpu = Clamp(gpu, 2, 99),
                GpuOk = true,
                RamPct = Clamp(ram, 20, 95),
                RamUsedGb = 16 * Clamp(ram, 20, 95) / 100.0,
                RamTotalGb = 16,
                NetDown = net * 1024 * 1024 / 8,
                NetUp = net * 1024 * 1024 / 40,
                NetPct = net / 10.0,
                NetLinkMbps = 1000,
                CpuTemp = 52 + 8 * Math.Sin(t / 5.0),
                GpuTemp = 58 + 7 * Math.Sin(t / 4.0 + 0.5),
                Hz = 144,
                Fps = double.NaN,
                Stamp = DateTime.Now
            };
        }

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        // ---------------- 动画 ----------------

        void AnimateSize(double w, double h, double corner, bool expand)
        {
            TimeSpan dur = TimeSpan.FromMilliseconds(expand ? 300 : 200);
            EasingFunctionBase ease = expand
                ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                : (EasingFunctionBase)new QuinticEase { EasingMode = EasingMode.EaseInOut };

            _v.Shell.BeginAnimation(WidthProperty, new DoubleAnimation(w, dur) { EasingFunction = ease });
            _v.Shell.BeginAnimation(HeightProperty, new DoubleAnimation(h, dur) { EasingFunction = ease });
            _v.Shell.BeginAnimation(Border.CornerRadiusProperty, new CornerRadiusAnimation
            {
                From = _v.Shell.CornerRadius,
                To = new CornerRadius(corner),
                Duration = new Duration(dur),
                Easing = ease
            });
        }

        void FadeContent(bool expand)
        {
            _v.CompactLayer.BeginAnimation(OpacityProperty,
                new DoubleAnimation(expand ? 0 : 1, TimeSpan.FromMilliseconds(expand ? 60 : 90)));

            var fa = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(expand ? 170 : 90));
            fa.BeginTime = TimeSpan.FromMilliseconds(expand ? 90 : 0);
            _v.ExpandedLayer.BeginAnimation(OpacityProperty, fa);
            _v.ExpandedLayer.IsHitTestVisible = expand;
        }

        void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            _glass.HoldFor(4);
            Refresh();
            AnimateSize(DashboardView.ExpandedWidth, DashboardView.ExpandedHeight(),
                DashboardView.CornerExpanded, true);
            FadeContent(true);
        }

        void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            _glass.HoldFor(3);
            _pinned = false;
            AnimateSize(CompactWidth, DashboardView.CompactHeight, DashboardView.CornerCollapsed, false);
            FadeContent(false);
        }

        // ---------------- 鼠标 ----------------

        void PollHover()
        {
            if (_hidden || _dragging || !IsVisible) return;
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;

            double w = _v.Shell.ActualWidth, h = _v.Shell.ActualHeight;
            if (w <= 0 || h <= 0) return;

            bool over;
            try
            {
                Point screen = PointFromScreen(new Point(p.X, p.Y));
                Point rel = TranslatePoint(screen, _v.Shell);
                over = rel.X >= 0 && rel.X <= w && rel.Y >= 0 && rel.Y <= h;
            }
            catch { return; }

            if (Environment.GetEnvironmentVariable("ISLAND_DEBUG") == "1" && (DateTime.Now - _lastPollLog).TotalSeconds > 1)
            {
                _lastPollLog = DateTime.Now;
                AppEnv.Log("poll over=" + over + " cursor=" + p.X + "," + p.Y + " shellW=" + w.ToString("0") + " win=" + Left + "," + Top);
            }

            if (over)
            {
                if (!_expanded) Expand();
                _collapseTimer.Stop();
            }
            else if (_expanded && !_pinned && !_collapseTimer.IsEnabled)
            {
                _collapseTimer.Start();
            }
        }

        void Hook()
        {
            _v.Shell.MouseEnter += delegate
            {
                if (_hidden) return;
                _collapseTimer.Stop();
                if (!_expanded) Expand();
            };

            _v.Shell.MouseLeave += delegate
            {
                if (_pinned) return;
                _collapseTimer.Stop();
                _collapseTimer.Start();
            };

            _v.Shell.MouseMove += delegate(object s, MouseEventArgs e)
            {
                _v.SetPointer(e.GetPosition(_v.Shell));
            };

            _v.Shell.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                _moved = false;
                e.Handled = true;
            };

            _v.Shell.MouseMove += delegate(object s, MouseEventArgs e)
            {
                return;
                Point p = PointToScreen(e.GetPosition(this));
                double scale = DpiScale();
                double dx = (p.X - _dragStartScreen.X) / scale;
                double dy = (p.Y - _dragStartScreen.Y) / scale;
                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3) _moved = true;
                if (_moved)
                {
                    Left = Math.Round(_dragOrigin.X + dx);
                    Top = Math.Round(_dragOrigin.Y + dy);
                }
            };

            _v.Shell.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (_v.Shell.IsMouseCaptured) _v.Shell.ReleaseMouseCapture();
                _dragging = false;
                e.Handled = true;
                if (!_expanded) { _pinned = true; Expand(); }
                else { _pinned = !_pinned; if (!_pinned) Collapse(); }
            };

            // 滚轮 = 调透明度，不用进菜单
            _v.Shell.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                int d = e.Delta > 0 ? 3 : -3;
                SetTint(_v.TintPercent + d);
                e.Handled = true;
            };

            _v.Shell.MouseRightButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                HideTemporarily();
                e.Handled = true;
            };
        }

        double DpiScale()
        {
            PresentationSource src = PresentationSource.FromVisual(this);
            if (src == null || src.CompositionTarget == null) return 1.0;
            double s = src.CompositionTarget.TransformToDevice.M11;
            return s <= 0 ? 1.0 : s;
        }

        // ---------------- 菜单 ----------------

        /// <summary>右键：隐藏 3 秒再自己回来。</summary>
        void HideTemporarily()
        {
            if (_hideTimer == null)
            {
                _hideTimer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Normal, delegate
                {
                    _hideTimer.Stop();
                    _hidden = false;
                    _v.Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(200)));
                }, Dispatcher);
            }

            _hidden = true;
            if (_expanded) Collapse();
            _v.Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130)));

            _hideTimer.Stop();
            _hideTimer.Start();
        }

        void SetTint(int pct)
        {
            if (pct < 0) pct = 0; else if (pct > 80) pct = 80;
            _v.TintPercent = pct;
            _v.ApplyTint();
            Store.Cfg.GlassTint = pct;
            Store.Save();
        }

        void ShowMenu()
        {
            try { ShowMenuCore(); } catch (Exception ex) { AppEnv.Log("menu: " + ex.Message); }
        }

        void ShowMenuCore()
        {
            var menu = new ContextMenu();
            menu.PlacementTarget = _v.Shell;

            var auto = new MenuItem { Header = "开机自动启动", IsCheckable = true, IsChecked = AutoStart.IsEnabled() };
            auto.Click += delegate
            {
                try { AutoStart.Set(!auto.IsChecked); auto.IsChecked = AutoStart.IsEnabled(); }
                catch (Exception ex) { AppEnv.Log("autostart: " + ex.Message); }
            };
            menu.Items.Add(auto);

            var more = new MenuItem { Header = "更透明（也可直接滚轮）" };
            more.Click += delegate { SetTint(_v.TintPercent - 5); };
            menu.Items.Add(more);

            var less = new MenuItem { Header = "更实一点" };
            less.Click += delegate { SetTint(_v.TintPercent + 5); };
            menu.Items.Add(less);
            menu.Items.Add(new Separator());

            var glass = new MenuItem { Header = "毛玻璃效果", IsCheckable = true, IsChecked = _glass.Enabled };
            glass.Click += delegate
            {
                _glass.Enabled = glass.IsChecked;
                ApplyGlassState(_glass.Available);
            };
            glass.IsEnabled = _glass.Available;
            menu.Items.Add(glass);

            var demo = new MenuItem { Header = "演示模式", IsCheckable = true, IsChecked = _demo };
            demo.IsEnabled = false;
            menu.Items.Add(demo);

            menu.Items.Add(new Separator());

            var quit = new MenuItem { Header = "退出灵动岛" };
            quit.Click += delegate { Close(); };
            menu.Items.Add(quit);

            menu.IsOpen = true;
        }
    }

    internal static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "DynamicIsland";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    return k.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k == null) return;
                if (on) k.SetValue(Name, "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }
}























