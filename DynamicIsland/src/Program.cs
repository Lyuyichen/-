using System;
using System.Threading;
using System.Windows;

namespace Island
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool demo = false;
            string preview = null;
            bool probe = false;

            foreach (string a in args)
            {
                if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                else if (string.Equals(a, "--probe", StringComparison.OrdinalIgnoreCase)) probe = true;
                else if (a.StartsWith("--preview=", StringComparison.OrdinalIgnoreCase)) preview = a.Substring("--preview=".Length);
            }

            if (probe)
            {
                Probe();
                return;
            }

            if (!string.IsNullOrEmpty(preview))
            {
                var pa = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Preview.Render(preview);
                return;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, "DynamicIsland.SingleInstance.v1", out createdNew))
            {
                if (!createdNew) return;

                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        AppEnv.Log("UI 异常: " + e.Exception);
                        e.Handled = true;
                    };

                    var win = new IslandWindow(demo);
                    win.Show();
                    app.Run();
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(AppEnv.State("crash.log"), ex.ToString()); }
                    catch { }
                }

                GC.KeepAlive(mutex);
            }
        }

        /// <summary>--probe：采集 4 秒，把结果写到 state\probe.txt，用于排查读数问题。</summary>
        static void Probe()
        {
            var s = new Sampler();
            s.Start();
            System.Threading.Thread.Sleep(4200);
            var v = s.Current;
            s.Dispose();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("时间: " + v.Stamp.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("CPU:      " + v.Cpu.ToString("0.0") + " %");
            sb.AppendLine("CPU 温度: " + (double.IsNaN(v.CpuTemp) ? "不可用" : v.CpuTemp.ToString("0") + " °C"));
            sb.AppendLine("GPU:      " + (v.GpuOk ? v.Gpu.ToString("0.0") + " %" : "不可用（无 GPU Engine 计数器）"));
            sb.AppendLine("GPU 温度: " + (double.IsNaN(v.GpuTemp) ? "不可用（非 NVIDIA 或无 NVML）" : v.GpuTemp.ToString("0") + " °C"));
            sb.AppendLine("内存:     " + v.RamPct.ToString("0") + " %  " + v.RamUsedGb.ToString("0.0") + "/" + v.RamTotalGb.ToString("0.0") + " GB");
            sb.AppendLine("网络:     ↓" + (v.NetDown / 1048576.0).ToString("0.00") + " MB/s  ↑" + (v.NetUp / 1048576.0).ToString("0.00") + " MB/s  (链路 " + v.NetLinkMbps.ToString("0") + " Mbps)");
            sb.AppendLine("刷新率:   " + v.Hz.ToString("0") + " Hz");

            string text = sb.ToString();
            try { System.IO.File.WriteAllText(AppEnv.State("probe.txt"), text, new System.Text.UTF8Encoding(false)); }
            catch { }
        }
    }
}
