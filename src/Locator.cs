using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PclRegionBypass
{
    internal static class Locator
    {
        /// <summary>在常见位置寻找 PCL-CE 的主程序。</summary>
        public static string FindLauncher(string hintDirectory)
        {
            string selfDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var candidates = new List<string>();

            if (!string.IsNullOrEmpty(hintDirectory))
                candidates.Add(hintDirectory);
            if (!string.IsNullOrEmpty(selfDirectory))
            {
                candidates.Add(selfDirectory);
                string parent = Path.GetDirectoryName(selfDirectory);
                if (!string.IsNullOrEmpty(parent)) candidates.Add(parent);
            }

            foreach (string directory in candidates)
            {
                string found = ScanDirectory(directory);
                if (!string.IsNullOrEmpty(found)) return found;
            }

            return null;
        }

        private static string ScanDirectory(string directory)
        {
            try
            {
                if (!Directory.Exists(directory)) return null;

                var best = new List<string>();
                foreach (string file in Directory.GetFiles(directory, "*.exe"))
                {
                    string name = Path.GetFileName(file);
                    if (string.Equals(name, Path.GetFileName(Assembly.GetExecutingAssembly().Location),
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.StartsWith("PCL", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Plain Craft Launcher", StringComparison.OrdinalIgnoreCase))
                        best.Add(file);
                }

                if (best.Count == 1) return best[0];

                // 优先带 CE / x64 字样的发布版
                foreach (string file in best)
                    if (Path.GetFileName(file).IndexOf("CE", StringComparison.OrdinalIgnoreCase) >= 0)
                        return file;
                if (best.Count > 0) return best[0];
            }
            catch
            {
                // 目录没有权限之类，忽略
            }

            return null;
        }
    }
}
