using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace PclRegionBypass
{
    /// <summary>
    ///     开机启动：写在 HKCU\…\Run 下，不需要管理员权限，也不碰计划任务。
    ///     注册表是唯一事实来源，每次启动时按当前 exe 路径自愈（程序被移动过也能修正）。
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PCLRegionBypass";

        public static string ExecutablePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        private static string BuildCommand(string exePath)
        {
            return "\"" + exePath + "\" --autostart";
        }

        public static string CurrentCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) return null;
                    return key.GetValue(ValueName) as string;
                }
            }
            catch
            {
                return null;
            }
        }

        public static bool IsEnabled()
        {
            string command = CurrentCommand();
            return !string.IsNullOrEmpty(command);
        }

        public static void Enable()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) throw new InvalidOperationException("无法写入注册表启动项");
                key.SetValue(ValueName, BuildCommand(ExecutablePath), RegistryValueKind.String);
            }
        }

        public static void Disable()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
            {
                if (key == null) return;
                if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, false);
            }
        }

        public static void SetEnabled(bool enabled)
        {
            if (enabled) Enable();
            else Disable();
        }

        /// <summary>程序被移动/改名后，把启动项里的路径修正成当前路径。</summary>
        public static void Heal()
        {
            try
            {
                string command = CurrentCommand();
                if (string.IsNullOrEmpty(command)) return;

                string expected = BuildCommand(ExecutablePath);
                if (string.Equals(command.Trim(), expected, StringComparison.OrdinalIgnoreCase)) return;

                // 条目指向的 exe 已经不存在（例如换了目录），照样修正成本程序
                Enable();
            }
            catch
            {
                // 自愈失败不影响启动
            }
        }

        /// <summary>启动项里记录的 exe 路径（去掉引号与参数），解析不出来时返回 null。</summary>
        public static string ParsedExecutable()
        {
            string command = CurrentCommand();
            if (string.IsNullOrEmpty(command)) return null;

            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = command.IndexOf('"', 1);
                if (end > 1) return command.Substring(1, end - 1);
            }

            int space = command.IndexOf(' ');
            return space > 0 ? command.Substring(0, space) : command;
        }

        public static bool PointsToCurrentExecutable()
        {
            string parsed = ParsedExecutable();
            if (string.IsNullOrEmpty(parsed)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(parsed), Path.GetFullPath(ExecutablePath),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
