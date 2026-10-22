using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RegHookTarget
{
    /// <summary>
    ///     注册表钩子测试靶。
    ///     在 HKCU 下建一个测试键，写入三个值，然后用原生 RegQueryValueExW / RegGetValueW 读取：
    ///       DeviceRegion (12 字符，等于 PCL 关注的设备区域值名) —— 被钩子拦截时应返回「不存在」
    ///       DeviceRegio  (11 字符，前缀相同)                      —— 必须照常读得到（验证跳板透传）
    ///       Other                                                  —— 必须照常读得到
    ///     退出码：0 = 未挂钩（正常读到）、3 = 钩子工作正常、1 = 透传被打断（异常）
    /// </summary>
    internal static class RegHookTarget
    {
        private const int ErrorFileNotFound = 2;
        private const int KeyRead = 0x20019;
        private const int RrfRtAny = 0x0000FFFF;
        private const string TestSubKey = @"Software\PCL2CE-Bypass-Test";

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyExW(IntPtr key, string subKey, int options, int desired, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueExW(IntPtr key, string valueName, IntPtr reserved, out int type,
            byte[] data, ref int size);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegGetValueW(IntPtr key, string subKey, string valueName, int flags, out int type,
            byte[] data, ref int size);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr key);

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
            }

            // 给外部补丁器留出挂载钩子的时间（真实启动器启动本来就要好几秒）
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "--delay", StringComparison.OrdinalIgnoreCase)) continue;
                int delay;
                if (int.TryParse(args[i + 1], out delay)) System.Threading.Thread.Sleep(delay);
            }

            Prepare();

            int deviceRegionError = QueryValue("DeviceRegion");
            int deviceRegioError = QueryValue("DeviceRegio");
            int otherError = QueryValue("Other");
            int getValueError = QueryViaGetValue();

            Cleanup();

            bool hookActive = deviceRegionError == ErrorFileNotFound && getValueError == ErrorFileNotFound;
            bool passthroughOk = deviceRegioError == 0 && otherError == 0;

            Console.WriteLine("DEVICE_REGION_QUERY   = " + deviceRegionError);
            Console.WriteLine("DEVICE_REGION_GETVALUE= " + getValueError);
            Console.WriteLine("PREFIX_DEVICEREGIO    = " + deviceRegioError);
            Console.WriteLine("OTHER_VALUE           = " + otherError);
            Console.WriteLine("HOOK_ACTIVE           = " + (hookActive ? "YES" : "NO"));
            Console.WriteLine("PASSTHROUGH_OK        = " + (passthroughOk ? "YES" : "NO"));

            if (!passthroughOk) return 1;
            return hookActive ? 3 : 0;
        }

        private static void Prepare()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(TestSubKey))
            {
                key.SetValue("DeviceRegion", 45, RegistryValueKind.DWord);
                key.SetValue("DeviceRegio", 9, RegistryValueKind.DWord);
                key.SetValue("Other", 7, RegistryValueKind.DWord);
            }
        }

        private static void Cleanup()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(TestSubKey, false);
            }
            catch
            {
            }
        }

        private static int QueryValue(string valueName)
        {
            IntPtr key;
            int result = RegOpenKeyExW(new IntPtr(unchecked((int)0x80000001)), TestSubKey, 0, KeyRead, out key);
            if (result != 0) return -1000 - result;
            try
            {
                var data = new byte[16];
                int size = data.Length;
                int type;
                result = RegQueryValueExW(key, valueName, IntPtr.Zero, out type, data, ref size);
                return result;
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private static int QueryViaGetValue()
        {
            var data = new byte[16];
            int size = data.Length;
            int type;
            return RegGetValueW(new IntPtr(unchecked((int)0x80000001)), TestSubKey, "DeviceRegion", RrfRtAny, out type,
                data, ref size);
        }
    }
}
