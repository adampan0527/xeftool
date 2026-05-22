using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using XefTool.Converters;
using XefTool.Models;

namespace XefTool.Services;

public class ImageExporterService
{
    private const int COLOR_WIDTH = 1920;
    private const int COLOR_HEIGHT = 1080;
    private const int DEPTH_WIDTH = 512;
    private const int DEPTH_HEIGHT = 424;
    private const int IR_WIDTH = 512;
    private const int IR_HEIGHT = 424;
    private const int YUYV_BPP = 2;

    public void ExportFrame(FrameInfo frame, string outputDir, string colorFormat)
    {
        switch (frame.StreamType)
        {
            case StreamType.Color:
                ExportColorFrame(frame, outputDir, colorFormat);
                break;
            case StreamType.Depth:
                ExportDepthFrame(frame, outputDir);
                break;
            case StreamType.Ir:
                ExportIrFrame(frame, outputDir);
                break;
        }
    }

    private void ExportColorFrame(FrameInfo frame, string outputDir, string format)
    {
        if (frame.Data.Length < COLOR_WIDTH * COLOR_HEIGHT * YUYV_BPP) return;
        byte[] rgb = YuyvToRgb(frame.Data, COLOR_WIDTH, COLOR_HEIGHT);
        string ext = format.ToLower() == "png" ? "png" : "jpg";
        Directory.CreateDirectory(outputDir);
        SaveRgbAsImage(rgb, COLOR_WIDTH, COLOR_HEIGHT,
            Path.Combine(outputDir, $"color_{frame.FrameIndex:D6}.{ext}"), ext);
    }

    private void ExportDepthFrame(FrameInfo frame, string outputDir)
    {
        if (frame.Data.Length < DEPTH_WIDTH * DEPTH_HEIGHT * 2) return;
        Directory.CreateDirectory(outputDir);
        byte[] rgb = DepthColorMap.DepthToRgb(frame.Data, DEPTH_WIDTH, DEPTH_HEIGHT);
        SaveRgbAsImage(rgb, DEPTH_WIDTH, DEPTH_HEIGHT,
            Path.Combine(outputDir, $"depth_{frame.FrameIndex:D6}.png"), "png");
    }

    private void ExportIrFrame(FrameInfo frame, string outputDir)
    {
        if (frame.Data.Length < IR_WIDTH * IR_HEIGHT * 2) return;
        Directory.CreateDirectory(outputDir);
        byte[] gray = DepthColorMap.ToGrayscale(frame.Data, IR_WIDTH, IR_HEIGHT);
        SaveGrayscaleAsPng(gray, IR_WIDTH, IR_HEIGHT,
            Path.Combine(outputDir, $"ir_{frame.FrameIndex:D6}.png"));
    }

    private static byte[] YuyvToRgb(byte[] yuyv, int width, int height)
    {
        byte[] rgb = new byte[width * height * 3];
        int numPixels = width * height;
        unsafe
        {
            fixed (byte* srcPtr = yuyv)
            fixed (byte* dstPtr = rgb)
            {
                byte* src = srcPtr;
                byte* dst = dstPtr;
                for (int i = 0; i < numPixels; i += 2)
                {
                    int y0 = src[0], u = src[1], y1 = src[2], v = src[3];
                    src += 4;
                    int c0 = y0 - 16, c1 = y1 - 16, d = u - 128, e = v - 128;
                    dst[0] = (byte)Math.Clamp((298 * c0 + 409 * e + 128) >> 8, 0, 255);
                    dst[1] = (byte)Math.Clamp((298 * c0 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                    dst[2] = (byte)Math.Clamp((298 * c0 + 516 * d + 128) >> 8, 0, 255);
                    dst += 3;
                    dst[0] = (byte)Math.Clamp((298 * c1 + 409 * e + 128) >> 8, 0, 255);
                    dst[1] = (byte)Math.Clamp((298 * c1 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                    dst[2] = (byte)Math.Clamp((298 * c1 + 516 * d + 128) >> 8, 0, 255);
                    dst += 3;
                }
            }
        }
        return rgb;
    }

    private static void SaveRgbAsImage(byte[] rgb, int width, int height, string path, string format)
    {
        int expectedSize = width * height * 3;
        if (rgb.Length < expectedSize)
            throw new ArgumentException($"RGB data size mismatch: expected {expectedSize}, got {rgb.Length}");

        using Bitmap bmp = new(width, height, PixelFormat.Format24bppRgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            int copyLength = Math.Min(rgb.Length, data.Stride * height);
            Marshal.Copy(rgb, 0, data.Scan0, copyLength);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        bmp.Save(path, format == "png" ? ImageFormat.Png : ImageFormat.Jpeg);
    }

    private static void SaveGrayscaleAsPng(byte[] gray, int width, int height, string path)
    {
        int expectedSize = width * height;
        if (gray.Length < expectedSize)
            throw new ArgumentException($"Grayscale data size mismatch: expected {expectedSize}, got {gray.Length}");

        using Bitmap bmp = new(width, height, PixelFormat.Format8bppIndexed);
        var palette = bmp.Palette;
        for (int i = 0; i < 256; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
        bmp.Palette = palette;
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        try
        {
            int copyLength = Math.Min(gray.Length, data.Stride * height);
            Marshal.Copy(gray, 0, data.Scan0, copyLength);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
