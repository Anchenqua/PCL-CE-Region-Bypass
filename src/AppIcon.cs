using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PclRegionBypass
{
    /// <summary>运行时画一个托盘图标，省得外挂 .ico 文件。</summary>
    internal static class AppIcon
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon Create()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    using (var background = new SolidBrush(Color.FromArgb(46, 125, 70)))
                        graphics.FillEllipse(background, 1f, 1f, 30f, 30f);

                    using (var pen = new Pen(Color.White, 3.6f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        graphics.DrawLines(pen, new[]
                        {
                            new PointF(8.5f, 16.5f),
                            new PointF(13.5f, 21.5f),
                            new PointF(23.5f, 10.5f)
                        });
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
