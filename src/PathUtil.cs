using System;
using System.IO;

namespace PclRegionBypass
{
    /// <summary>
    ///     路径相关的小工具。.NET Framework 的 Path.GetDirectoryName 对空串、非法字符、超长路径都会抛异常，
    ///     UI 里任何一处直接调用它都可能让整个窗口崩掉，所以统一走这里，永不抛异常。
    /// </summary>
    internal static class PathUtil
    {
        /// <summary>返回路径所在的、确实存在的目录；无法判断时返回 null。</summary>
        public static string ExistingDirectoryOf(string path)
        {
            try
            {
                if (path == null) return null;

                // 用户可能粘贴了带引号的路径，或者只有空白
                path = path.Trim().Trim('"').Trim();
                if (path.Length == 0) return null;

                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory)) return null;

                return Directory.Exists(directory) ? directory : null;
            }
            catch
            {
                // 含非法字符、路径过长、UNC 格式异常等，一律当作「没有可用目录」
                return null;
            }
        }

        /// <summary>把用户输入的路径规整成绝对路径；失败时返回 null。</summary>
        public static string NormalizeOrNull(string path)
        {
            try
            {
                if (path == null) return null;
                path = path.Trim().Trim('"').Trim();
                if (path.Length == 0) return null;
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }
    }
}
