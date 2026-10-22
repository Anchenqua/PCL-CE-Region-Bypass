using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PclRegionBypass;

namespace PolicyTarget
{
    /// <summary>
    ///     测试靶：把 PCL-CE 的 RegionPolicy 判定逻辑跑在独立进程里。
    ///     直接运行时应当判定为「受限」；经由 PCLRegionBypass 启动（注入钩子后）应当判定为「不受限」。
    ///     退出码：0 = 不受限，1 = 受限（便于脚本断言）。
    /// </summary>
    internal static class PolicyTarget
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
                // 忽略
            }

            int delayMs = ParseIntOption(args, "--delay", 0);
            int formMs = ParseIntOption(args, "--form", 0);

            if (formMs > 0)
            {
                // 模拟启动器：先起一个窗口（此时区域性已经定型），再在窗口存在期间做判定。
                int exitCode = 1;
                var form = new Form
                {
                    Text = "PolicyTarget",
                    ClientSize = new System.Drawing.Size(360, 120)
                };
                form.Shown += delegate
                {
                    var timer = new System.Windows.Forms.Timer();
                    timer.Interval = formMs;
                    timer.Tick += delegate
                    {
                        timer.Stop();
                        exitCode = Evaluate();
                        form.Close();
                    };
                    timer.Start();
                };
                Application.Run(form);
                return exitCode;
            }

            if (delayMs > 0) Thread.Sleep(delayMs);
            return Evaluate();
        }

        private static int Evaluate()
        {
            RegionSignals signals = RegionPolicyPort.ReadWindowsSignals();
            RegionAssessment assessment = RegionPolicyPort.Evaluate(signals);

            Console.WriteLine("GeoName              = " + Show(signals.GeoName));
            Console.WriteLine("LocaleName           = " + Show(signals.UserLocaleName));
            Console.WriteLine("PreferredUILanguages = " +
                              (signals.PreferredUiLanguages.Count == 0
                                  ? "[空]"
                                  : "[" + string.Join(", ", new List<string>(signals.PreferredUiLanguages).ToArray()) + "]"));
            Console.WriteLine("TimeZone             = " + Show(signals.TimeZoneId));
            Console.WriteLine("DeviceRegion         = " +
                              (signals.DeviceRegionAvailable ? signals.DeviceRegionGeoId.ToString() : "(不存在)"));
            Console.WriteLine("CurrentCulture       = " + CultureInfo.CurrentCulture.Name);
            Console.WriteLine("CurrentUICulture     = " + CultureInfo.CurrentUICulture.Name);
            Console.WriteLine("Classification       = " + assessment.ClassificationText);
            Console.WriteLine("Reason               = " + assessment.Reason);
            Console.WriteLine("RESTRICTED           = " + (assessment.IsRestricted ? "YES" : "NO"));
            return assessment.IsRestricted ? 1 : 0;
        }

        private static string Show(string value)
        {
            return value == null ? "(null)" : "\"" + value + "\"";
        }

        private static int ParseIntOption(string[] args, string name, int fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                int value;
                if (int.TryParse(args[i + 1], out value)) return value;
            }

            return fallback;
        }
    }
}
