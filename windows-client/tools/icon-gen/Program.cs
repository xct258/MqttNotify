using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IconGen;

/// <summary>
/// 生成 <c>MqttNotify.ico</c>（托盘图标 + 可执行文件图标）。
///
/// 为什么要手写字节：本仓库在 Linux 上交叉编译，环境里没有任何图形库
/// （System.Drawing.Common 从 .NET 6 起只在 Windows 上可用），
/// 所以直接按 ICO 规范拼字节，并用 4x4 超采样自绘抗锯齿。
///
/// 图案：圆角蓝色方块 + 白色铃铛（钟体 / 底沿 / 顶帽 / 钟锤四个图元求并集）。
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = { 16, 32, 48, 64, 128 };

    // BGRA 顺序，与位图载荷一致
    private static readonly (byte B, byte G, byte R) Background = (0xD2, 0x76, 0x19); // #1976D2
    private static readonly (byte B, byte G, byte R) Foreground = (0xFF, 0xFF, 0xFF);

    private static int Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "MqttNotify.ico";
        var images = Sizes.Select(RenderBgra).ToList();

        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        Directory.CreateDirectory(directory);

        WriteIco(output, images);
        Verify(output, images);

        Console.WriteLine(
            $"已生成 {output}：" +
            $"{new FileInfo(output).Length} 字节，" +
            $"{Sizes.Length} 个尺寸 [{string.Join(", ", Sizes)}]");

        return 0;
    }

    // ---------------------------------------------------------------------
    // 绘制
    // ---------------------------------------------------------------------

    private static byte[] RenderBgra(int size)
    {
        const int Samples = 4;
        var sampleStep = 1.0 / (size * Samples);
        var pixels = new byte[size * size * 4];
        var totalSamples = Samples * Samples;

        for (var py = 0; py < size; py++)
        {
            for (var px = 0; px < size; px++)
            {
                var backgroundCoverage = 0;
                var foregroundCoverage = 0;

                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var x = (px * Samples + sx + 0.5) * sampleStep;
                        var y = (py * Samples + sy + 0.5) * sampleStep;

                        if (!InRoundedSquare(x, y))
                        {
                            continue;
                        }

                        backgroundCoverage++;

                        if (InBell(x, y))
                        {
                            foregroundCoverage++;
                        }
                    }
                }

                // alpha = 圆角方块覆盖率；颜色 = 在方块内的子采样里，铃铛占比
                var alpha = (double)backgroundCoverage / totalSamples;
                var ink = backgroundCoverage > 0 ? (double)foregroundCoverage / backgroundCoverage : 0;

                var offset = (py * size + px) * 4;
                pixels[offset] = Mix(Background.B, Foreground.B, ink);
                pixels[offset + 1] = Mix(Background.G, Foreground.G, ink);
                pixels[offset + 2] = Mix(Background.R, Foreground.R, ink);
                pixels[offset + 3] = (byte)Math.Round(alpha * 255);
            }
        }

        return pixels;
    }

    private static bool InRoundedSquare(double x, double y)
    {
        const double Inset = 0.035;
        const double Radius = 0.22;
        return InRoundedRect(x, y, Inset, Inset, 1 - Inset, 1 - Inset, Radius);
    }

    private static bool InRoundedRect(double x, double y, double left, double top, double right, double bottom, double radius)
    {
        if (x < left || x > right || y < top || y > bottom)
        {
            return false;
        }

        // 把点夹到「内缩 radius 后的矩形」上，再比距离 —— 等价于到圆角的距离判定
        var cx = Math.Clamp(x, left + radius, right - radius);
        var cy = Math.Clamp(y, top + radius, bottom - radius);
        var dx = x - cx;
        var dy = y - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static bool InBell(double x, double y)
    {
        // 钟体：上窄下宽
        const double BodyTop = 0.255;
        const double BodyBottom = 0.700;
        if (y >= BodyTop && y <= BodyBottom)
        {
            var t = (y - BodyTop) / (BodyBottom - BodyTop);
            var halfWidth = 0.085 + (0.255 * Math.Pow(t, 0.65));
            if (Math.Abs(x - 0.5) <= halfWidth)
            {
                return true;
            }
        }

        // 底沿：一圈外扩的横条
        if (y > BodyBottom && y <= 0.765 && Math.Abs(x - 0.5) <= 0.375)
        {
            return true;
        }

        // 顶帽
        if (InCircle(x, y, 0.5, 0.205, 0.062))
        {
            return true;
        }

        // 钟锤：与底沿留出一点缝隙，否则小尺寸下会糊成一坨
        return InCircle(x, y, 0.5, 0.875, 0.070);
    }

    private static bool InCircle(double x, double y, double cx, double cy, double radius)
    {
        var dx = x - cx;
        var dy = y - cy;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }

    private static byte Mix(byte from, byte to, double t) =>
        (byte)Math.Round(from + ((to - from) * t));

    // ---------------------------------------------------------------------
    // ICO 封装
    // ---------------------------------------------------------------------

    private static void WriteIco(string path, IReadOnlyList<byte[]> images)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        // ICONDIR：reserved / type=1(icon) / 图像数
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        // ICONDIRENTRY[images.Count]，每个 16 字节
        var offset = 6 + (images.Count * 16);
        foreach (var image in images)
        {
            var size = SizeOf(image);
            writer.Write((byte)(size >= 256 ? 0 : size)); // 宽（256 记为 0）
            writer.Write((byte)(size >= 256 ? 0 : size)); // 高（256 记为 0）
            writer.Write((byte)0);                        // 调色板颜色数
            writer.Write((byte)0);                        // reserved
            writer.Write((ushort)1);                      // planes
            writer.Write((ushort)32);                     // 位深
            writer.Write((uint)BmpPayloadSize(size));     // 图像数据长度
            writer.Write((uint)offset);                   // 图像数据偏移
            offset += BmpPayloadSize(size);
        }

        foreach (var image in images)
        {
            WriteBmpPayload(writer, image);
        }
    }

    private static void WriteBmpPayload(BinaryWriter writer, byte[] bgra)
    {
        var size = SizeOf(bgra);
        var maskRowBytes = ((size + 31) / 32) * 4;
        var maskSize = maskRowBytes * size;

        // BITMAPINFOHEADER
        writer.Write(40u);                            // biSize
        writer.Write(size);                           // biWidth
        writer.Write(size * 2);                       // biHeight：XOR 图 + AND 掩码，故为 2 倍
        writer.Write((ushort)1);                      // biPlanes
        writer.Write((ushort)32);                     // biBitCount
        writer.Write(0u);                             // biCompression = BI_RGB
        writer.Write((uint)(bgra.Length + maskSize)); // biSizeImage
        writer.Write(0);                              // biXPelsPerMeter
        writer.Write(0);                              // biYPelsPerMeter
        writer.Write(0u);                             // biClrUsed
        writer.Write(0u);                             // biClrImportant

        // XOR 图：BGRA，自下而上
        for (var y = size - 1; y >= 0; y--)
        {
            writer.Write(bgra, y * size * 4, size * 4);
        }

        // AND 掩码：32 位图标里全 0 即可（透明度看 alpha 通道）
        writer.Write(new byte[maskSize]);
    }

    private static int BmpPayloadSize(int size)
    {
        var maskRowBytes = ((size + 31) / 32) * 4;
        return 40 + (size * size * 4) + (maskRowBytes * size);
    }

    private static int SizeOf(byte[] bgra) => (int)Math.Sqrt(bgra.Length / 4);

    /// <summary>回读校验：尺寸、条目数、总长度都必须自洽。</summary>
    private static void Verify(string path, IReadOnlyList<byte[]> images)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length < 6 || bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 1 || bytes[3] != 0)
        {
            throw new InvalidOperationException("ICO 头非法");
        }

        if (BitConverter.ToUInt16(bytes, 4) != images.Count)
        {
            throw new InvalidOperationException("ICO 尺寸条目数与图像数不一致");
        }

        var expected = 6 + (images.Count * 16) + images.Sum(i => BmpPayloadSize(SizeOf(i)));
        if (bytes.Length != expected)
        {
            throw new InvalidOperationException($"ICO 长度不符：实际 {bytes.Length}，预期 {expected}");
        }
    }
}
