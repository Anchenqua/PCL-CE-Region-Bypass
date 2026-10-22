using System;
using System.IO;
using PclRegionBypass;

namespace PathUtilTest
{
    /// <summary>
    ///     回归测试：路径框为空 / 含非法字符时，绝不能抛异常（曾导致「浏览」按钮直接崩溃）。
    ///     退出码 0 = 全部通过。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            }
            catch
            {
                // 忽略
            }

            int failures = 0;

            Expect(ref failures, "空串", null, PathUtil.ExistingDirectoryOf(""));
            Expect(ref failures, "全空白", null, PathUtil.ExistingDirectoryOf("   "));
            Expect(ref failures, "只有引号", null, PathUtil.ExistingDirectoryOf("\"\""));
            Expect(ref failures, "null", null, PathUtil.ExistingDirectoryOf(null));
            Expect(ref failures, "非法字符", null, PathUtil.ExistingDirectoryOf("a|b\\c.exe"));
            Expect(ref failures, "只有文件名", null, PathUtil.ExistingDirectoryOf("PCL.exe"));
            Expect(ref failures, "不存在的目录", null, PathUtil.ExistingDirectoryOf(@"Z:\nope\PCL.exe"));

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string expected = windows.TrimEnd('\\');
            Expect(ref failures, "正常路径", expected, PathUtil.ExistingDirectoryOf(Path.Combine(windows, "notepad.exe")));
            Expect(ref failures, "带引号的正常路径", expected,
                PathUtil.ExistingDirectoryOf("\"" + Path.Combine(windows, "notepad.exe") + "\""));

            Expect(ref failures, "Normalize 空串", null, PathUtil.NormalizeOrNull(""));
            Expect(ref failures, "Normalize 非法字符", null, PathUtil.NormalizeOrNull("a|b"));

            if (failures == 0)
            {
                Console.WriteLine("PATHUTIL: ALL PASSED");
                return 0;
            }

            Console.WriteLine("PATHUTIL: " + failures + " FAILED");
            return 1;
        }

        private static void Expect(ref int failures, string name, string expected, string actual)
        {
            bool ok = expected == null
                ? actual == null
                : actual != null && string.Equals(
                      actual.TrimEnd('\\'), expected.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name +
                              (ok ? string.Empty : "  expected=" + (expected ?? "null") + " actual=" + (actual ?? "null")));
            if (!ok) failures++;
        }
    }
}
