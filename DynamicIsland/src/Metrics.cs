using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Island
{
    /// <summary>一次采样得到的系统指标。</summary>
    internal class Snapshot
    {
        public double Cpu;          // 0..100
        public double Gpu;          // 0..100
        public bool GpuOk;

        public double RamPct;       // 0..100
        public double RamUsedGb;
        public double RamTotalGb;

        public double NetDown;      // bytes / s
        public double NetUp;        // bytes / s
        public double NetPct;       // 0..100（相对链路带宽）
        public double NetLinkMbps;

        public double CpuTemp = double.NaN;
        public double GpuTemp = double.NaN;

        public double Hz = 60;      // 显示器刷新率
        public double Fps = double.NaN;

        public DateTime Stamp = DateTime.Now;

        public Snapshot Clone() { return (Snapshot)MemberwiseClone(); }
    }

    /// <summary>
    /// 一组 PDH 计数器（支持通配实例），一次读取拿到所有实例的值。
    /// </summary>
    internal class PdhSeries : IDisposable
    {
        const uint PDH_FMT_DOUBLE = 0x00000200;
        const uint PDH_MORE_DATA = 0x800007D2;

        [StructLayout(LayoutKind.Sequential)]
        struct ITEM { public IntPtr szName; public uint CStatus; public double doubleValue; }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);
        [DllImport("pdh.dll")]
        static extern uint PdhCloseQuery(IntPtr query);

        IntPtr _query, _counter;
        public bool Ok { get; private set; }

        public PdhSeries(string path)
        {
            try
            {
                Ok = PdhOpenQuery(null, IntPtr.Zero, out _query) == 0 &&
                     PdhAddEnglishCounter(_query, path, IntPtr.Zero, out _counter) == 0 &&
                     PdhCollectQueryData(_query) == 0;
            }
            catch { Ok = false; }
        }

        public bool Read(out double[] values)
        {
            values = null;
            if (!Ok) return false;
            try
            {
                if (PdhCollectQueryData(_query) != 0) return false;

                uint size = 0, count = 0;
                uint rc = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
                if (rc != PDH_MORE_DATA && rc != 0) return false;
                if (size == 0 || count == 0) { values = new double[0]; return true; }

                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    rc = PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE, ref size, ref count, buf);
                    if (rc != 0) return false;

                    int itemSize = Marshal.SizeOf(typeof(ITEM));
                    var list = new List<double>((int)count);
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = new IntPtr(buf.ToInt64() + i * itemSize);
                        var item = (ITEM)Marshal.PtrToStructure(p, typeof(ITEM));
                        if (item.CStatus == 0 && !double.IsNaN(item.doubleValue) && !double.IsInfinity(item.doubleValue))
                            list.Add(item.doubleValue);
                    }
                    values = list.ToArray();
                    return true;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return false; }
        }

        public void Dispose()
        {
            try { if (_query != IntPtr.Zero) PdhCloseQuery(_query); }
            catch { }
            _query = IntPtr.Zero;
            Ok = false;
        }
    }

    /// <summary>
    /// 系统指标采集，全部走 Windows 自带接口，普通权限即可：
    ///  CPU%    GetSystemTimes
    ///  GPU%    PDH "GPU Engine"
    ///  内存%   GlobalMemoryStatusEx
    ///  以太网   NetworkInterface 字节差分
    ///  温度     PDH 热区 / WMI ACPI（CPU），NVML（NVIDIA GPU）
    /// </summary>
    internal class Sampler : IDisposable
    {
        readonly object _gate = new object();
        Snapshot _current = new Snapshot();
        Timer _timer;
        volatile bool _disposed;

        // ---- CPU ----
        [StructLayout(LayoutKind.Sequential)]
        struct FILETIME { public uint Low; public uint High; }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        ulong _lastIdle, _lastKernel, _lastUser;
        bool _cpuInit;

        // ---- 内存 ----
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ---- 刷新率 ----
        const int VREFRESH = 116;
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);

        // ---- GPU 温度 NVML ----
        [DllImport("nvml.dll")] static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
        [DllImport("nvml.dll")] static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensorType, out uint temp);
        IntPtr _nvmlDev;
        bool _nvmlTried, _nvmlOk;

        // ---- PDH ----
        PdhSeries _gpuSeries, _tzSeries;
        bool _pdhTried;

        // ---- 网络 ----
        System.Net.NetworkInformation.NetworkInterface _nic;
        double _nicSpeedMbps = 1000;
        ulong _lastRx, _lastTx;
        DateTime _lastNetTime = DateTime.MinValue;

        double _hz = 60;
        double _cpuTemp = double.NaN;
        double _gpuTemp = double.NaN;
        DateTime _lastTempScan = DateTime.MinValue;

        public Snapshot Current { get { lock (_gate) return _current.Clone(); } }
        public event Action Updated;

        public void Start()
        {
            ProbeRefreshRate();
            _timer = new Timer(Tick, null, 250, 600);
        }

        public void Dispose()
        {
            _disposed = true;
            if (_timer != null) { _timer.Dispose(); _timer = null; }
            if (_gpuSeries != null) _gpuSeries.Dispose();
            if (_tzSeries != null) _tzSeries.Dispose();
        }

        void Tick(object state)
        {
            if (_disposed) return;
            try
            {
                EnsurePdh();

                var s = new Snapshot();
                s.Cpu = ReadCpu();
                s.GpuOk = ReadGpuGpu(out s.Gpu);
                ReadMemory(s);
                ReadNetwork(s);
                s.Hz = _hz;
                s.CpuTemp = _cpuTemp;
                s.GpuTemp = _gpuTemp;
                s.Stamp = DateTime.Now;

                lock (_gate) _current = s;

                var h = Updated;
                if (h != null) h();
            }
            catch { }

            if ((DateTime.Now - _lastTempScan).TotalSeconds > 5)
            {
                _lastTempScan = DateTime.Now;
                ThreadPool.QueueUserWorkItem(delegate { ScanTemps(); });
            }
        }

        void EnsurePdh()
        {
            if (_pdhTried) return;
            _pdhTried = true;
            _gpuSeries = new PdhSeries("\\GPU Engine(*)\\Utilization Percentage");
            _tzSeries = new PdhSeries("\\Thermal Zone Information(*)\\Temperature");
        }

        void ScanTemps()
        {
            double cpu = ReadCpuTempPdh();
            if (double.IsNaN(cpu)) cpu = ReadCpuTempWmi();
            double gpu = ReadGpuTempNvml();
            _cpuTemp = cpu;
            _gpuTemp = gpu;
        }

        // ---------------- CPU ----------------
        double ReadCpu()
        {
            FILETIME idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return 0;
            ulong i = ((ulong)idle.High << 32) | idle.Low;
            ulong k = ((ulong)kernel.High << 32) | kernel.Low;
            ulong u = ((ulong)user.High << 32) | user.Low;

            if (!_cpuInit) { _cpuInit = true; _lastIdle = i; _lastKernel = k; _lastUser = u; return 0; }

            ulong di = i - _lastIdle, dk = k - _lastKernel, du = u - _lastUser;
            _lastIdle = i; _lastKernel = k; _lastUser = u;

            ulong total = dk + du;
            if (total == 0) return 0;
            return Clamp((double)(total - di) / total * 100.0, 0, 100);
        }

        // ---------------- GPU ----------------
        bool ReadGpuGpu(out double value)
        {
            value = 0;
            if (_gpuSeries == null || !_gpuSeries.Ok) return false;
            double[] vals;
            if (!_gpuSeries.Read(out vals)) return false;

            double total = 0;
            foreach (double v in vals) total += v;
            value = Clamp(total, 0, 100);
            return true;
        }

        // ---------------- 内存 ----------------
        static void ReadMemory(Snapshot s)
        {
            var m = new MEMORYSTATUSEX();
            m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (!GlobalMemoryStatusEx(ref m)) return;
            s.RamPct = m.dwMemoryLoad;
            s.RamTotalGb = m.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
            s.RamUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
        }

        // ---------------- 网络 ----------------
        void ReadNetwork(Snapshot s)
        {
            try
            {
                if (_nic == null) PickNic();
                if (_nic == null) return;

                var st = _nic.GetIPv4Statistics();
                ulong rx = (ulong)st.BytesReceived;
                ulong tx = (ulong)st.BytesSent;
                DateTime now = DateTime.Now;

                if (_lastNetTime != DateTime.MinValue)
                {
                    double sec = (now - _lastNetTime).TotalSeconds;
                    if (sec > 0.05)
                    {
                        s.NetDown = Math.Max(0, (double)(rx - _lastRx) / sec);
                        s.NetUp = Math.Max(0, (double)(tx - _lastTx) / sec);
                    }
                }
                _lastNetTime = now;
                _lastRx = rx;
                _lastTx = tx;

                s.NetLinkMbps = _nicSpeedMbps;
                double bits = (s.NetDown + s.NetUp) * 8.0;
                double cap = _nicSpeedMbps * 1000.0 * 1000.0;
                s.NetPct = cap > 0 ? Clamp(bits / cap * 100.0, 0, 100) : 0;
            }
            catch { }
        }

        void PickNic()
        {
            var t = System.Net.NetworkInformation.NetworkInterfaceType.Ethernet;
            System.Net.NetworkInformation.NetworkInterface best = null;

            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType != t &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.GigabitEthernet &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetT &&
                    ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.FastEthernetFx)
                    continue;
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                best = ni;
                break;
            }

            if (best == null)
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    best = ni;
                    break;
                }
            }

            _nic = best;
            if (_nic != null)
            {
                try
                {
                    long speed = _nic.Speed;
                    if (speed > 0) _nicSpeedMbps = speed / 1000.0 / 1000.0;
                }
                catch { }
            }
        }

        // ---------------- 温度 ----------------
        double ReadCpuTempPdh()
        {
            if (_tzSeries == null || !_tzSeries.Ok) return double.NaN;
            double[] vals;
            if (!_tzSeries.Read(out vals)) return double.NaN;

            double best = double.NaN;
            foreach (double raw in vals)
            {
                // 多数机器上报开尔文（如 301K），少数是十倍开尔文
                double c = raw - 273.15;
                if (c < 5 || c > 110)
                {
                    double alt = raw / 10.0 - 273.15;
                    if (alt >= 5 && alt <= 110) c = alt; else continue;
                }
                if (double.IsNaN(best) || c > best) best = c;
            }
            return double.IsNaN(best) ? best : Math.Round(best, 0);
        }

        bool _wmiTried, _wmiOk;

        double ReadCpuTempWmi()
        {
            if (_wmiTried && !_wmiOk) return double.NaN;
            _wmiTried = true;
            try
            {
                double best = double.NaN;
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            double kelvin10 = Convert.ToDouble(obj["CurrentTemperature"]);
                            double c = kelvin10 / 10.0 - 273.15;
                            if (c > 0 && c < 120 && (double.IsNaN(best) || c > best)) best = c;
                        }
                        catch { }
                    }
                }
                if (!double.IsNaN(best)) { _wmiOk = true; return Math.Round(best, 0); }
            }
            catch { }
            _wmiOk = false;
            return double.NaN;
        }

        double ReadGpuTempNvml()
        {
            if (!_nvmlTried)
            {
                _nvmlTried = true;
                try { _nvmlOk = nvmlInit_v2() == 0 && nvmlDeviceGetHandleByIndex_v2(0, out _nvmlDev) == 0; }
                catch { _nvmlOk = false; }
            }
            if (!_nvmlOk) return double.NaN;
            try
            {
                uint t;
                if (nvmlDeviceGetTemperature(_nvmlDev, 0, out t) == 0) return t;
            }
            catch { }
            return double.NaN;
        }

        // ---------------- 刷新率 ----------------
        void ProbeRefreshRate()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                int hz = GetDeviceCaps(dc, VREFRESH);
                ReleaseDC(IntPtr.Zero, dc);
                if (hz >= 24 && hz <= 500) _hz = hz;
            }
            catch { }
        }

        static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }

    internal class Config
    {
        public double X = double.NaN;
        public double Y = 6;
        public bool AutoStart;
        public bool AutoStartInit;
        public int GlassTint = 0;
    }

    internal static class Store
    {
        static readonly object Gate = new object();
        public static Config Cfg = new Config();
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static string ConfigPath { get { return AppEnv.State("config.json"); } }

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                var d = Ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                if (d == null) return;
                Cfg.X = Num(d, "x", double.NaN);
                Cfg.Y = Num(d, "y", 6);
                Cfg.AutoStart = Bool(d, "autoStart", false);
                Cfg.AutoStartInit = Bool(d, "autoStartInit", false);
                Cfg.GlassTint = (int)Num(d, "glassTint", 0);
            }
            catch { }
        }

        public static void Save()
        {
            lock (Gate)
            {
                try
                {
                    var d = new Dictionary<string, object>
                    {
                        { "x", double.IsNaN(Cfg.X) ? (object)null : Cfg.X },
                        { "y", Cfg.Y },
                        { "autoStart", Cfg.AutoStart },
                        { "autoStartInit", Cfg.AutoStartInit },
                        { "glassTint", Cfg.GlassTint }
                    };
                    string tmp = ConfigPath + ".tmp";
                    File.WriteAllText(tmp, Ser.Serialize(d), new UTF8Encoding(false));
                    if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                    File.Move(tmp, ConfigPath);
                }
                catch { }
            }
        }

        static double Num(Dictionary<string, object> d, string k, double def)
        {
            if (d == null || !d.ContainsKey(k) || d[k] == null) return def;
            try { return Convert.ToDouble(d[k]); } catch { return def; }
        }

        static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            if (d == null || !d.ContainsKey(k) || d[k] == null) return def;
            try { return Convert.ToBoolean(d[k]); } catch { return def; }
        }
    }
}





