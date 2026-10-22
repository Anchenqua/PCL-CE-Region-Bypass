using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PclRegionBypass
{
    /// <summary>极简配置持久化（%APPDATA%\PCLRegionBypass\settings.ini），key=value 一行一项。</summary>
    internal static class Settings
    {
        public const string CloseAsk = "ask";
        public const string CloseTray = "tray";
        public const string CloseExit = "exit";

        private static readonly Dictionary<string, string> Values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PCLRegionBypass", "settings.ini");
            }
        }

        public static string LastLauncherPath
        {
            get { return Get("LauncherPath", string.Empty); }
            set { Set("LauncherPath", value); }
        }

        /// <summary>关闭窗口时的行为：ask / tray / exit。</summary>
        public static string CloseAction
        {
            get
            {
                string value = Get("CloseAction", CloseAsk);
                if (string.Equals(value, CloseTray, StringComparison.OrdinalIgnoreCase)) return CloseTray;
                if (string.Equals(value, CloseExit, StringComparison.OrdinalIgnoreCase)) return CloseExit;
                return CloseAsk;
            }
            set { Set("CloseAction", value); }
        }

        /// <summary>启动时不显示窗口，直接进托盘。</summary>
        public static bool SilentStart
        {
            get { return GetBool("SilentStart"); }
            set { Set("SilentStart", value ? "1" : "0"); }
        }

        private static string Get(string key, string fallback)
        {
            string value;
            return Values.TryGetValue(key, out value) ? value : fallback;
        }

        private static bool GetBool(string key)
        {
            string value = Get(key, "0");
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static void Set(string key, string value)
        {
            Values[key] = value ?? string.Empty;
        }

        public static void Load()
        {
            Values.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int index = line.IndexOf('=');
                    if (index <= 0) continue;
                    string key = line.Substring(0, index).Trim();
                    string value = line.Substring(index + 1).Trim();
                    if (key.Length > 0) Values[key] = value;
                }
            }
            catch
            {
                // 配置读不到不影响主流程
            }
        }

        public static void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                foreach (KeyValuePair<string, string> pair in Values)
                    builder.Append(pair.Key).Append('=').Append(pair.Value).Append("\r\n");
                File.WriteAllText(FilePath, builder.ToString(), Encoding.UTF8);
            }
            catch
            {
                // 写不进去也无所谓
            }
        }
    }
}
