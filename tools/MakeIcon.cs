using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
///     生成 assets/app.ico（32x32、32bpp BGRA + AND 掩码的传统 DIB 格式，兼容性最好）。
///     只在图标需要改动时手动运行：
///         csc /nologo /target:exe /r:System.Drawing.dll /out:tools\MakeIcon.exe tools\MakeIcon.cs
///         tools\MakeIcon.exe assets\app.ico
/// </summary>
internal static class MakeIcon
{
    private static int Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "app.ico";
        const int size = 32;

        byte[] pixels;
        int stride;

        using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(255, 46, 125, 70)))
                    graphics.FillEllipse(brush, 1f, 1f, 30f, 30f);

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

            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            stride = data.Stride;
            pixels = new byte[stride * size];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bitmap.UnlockBits(data);
        }

        int xorSize = size * size * 4;
        int andStride = (size + 31) / 32 * 4;
        int andSize = andStride * size;
        int imageSize = 40 + xorSize + andSize;

        string directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            // ICONDIR
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            // ICONDIRENTRY
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)imageSize);
            writer.Write((uint)22);
            // BITMAPINFOHEADER，高度写两倍（XOR + AND）
            writer.Write((uint)40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)0);
            writer.Write((uint)xorSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)0);
            writer.Write((uint)0);

            // XOR 位图，自下而上
            for (int y = size - 1; y >= 0; y--)
                writer.Write(pixels, y * stride, size * 4);

            // AND 掩码全 0：不透明，透明度交给 alpha 通道
            writer.Write(new byte[andSize]);
        }

        Console.WriteLine("icon written: " + Path.GetFullPath(output) + " (" + new FileInfo(output).Length + " bytes)");
        return 0;
    }
}
