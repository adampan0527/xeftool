namespace XefTool.Converters;

/// <summary>
/// Jet colormap for depth visualization.
/// Maps 16-bit depth values to RGB using a standard jet color ramp.
/// </summary>
public static class DepthColorMap
{
    private static readonly (byte R, byte G, byte B)[] _jetTable = GenerateJetTable();

    private static (byte R, byte G, byte B)[] GenerateJetTable()
    {
        var table = new (byte R, byte G, byte B)[256];
        for (int i = 0; i < 256; i++)
        {
            double t = i / 255.0;
            double r, g, b;

            if (t < 0.25)
            {
                r = 0;
                g = t * 4;
                b = 1;
            }
            else if (t < 0.5)
            {
                r = 0;
                g = 1;
                b = 1 - (t - 0.25) * 4;
            }
            else if (t < 0.75)
            {
                r = (t - 0.5) * 4;
                g = 1;
                b = 0;
            }
            else
            {
                r = 1;
                g = 1 - (t - 0.75) * 4;
                b = 0;
            }

            table[i] = ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
        return table;
    }

    /// <summary>
    /// Convert a depth frame (16-bit unsigned, mm) to an RGB byte array.
    /// </summary>
    public static byte[] DepthToRgb(byte[] depthData, int width, int height, ushort minDepth = 500, ushort maxDepth = 4500)
    {
        int pixelCount = width * height;
        byte[] rgb = new byte[pixelCount * 3];

        for (int i = 0; i < pixelCount; i++)
        {
            ushort depth = BitConverter.ToUInt16(depthData, i * 2);

            if (depth == 0)
            {
                rgb[i * 3] = 0;
                rgb[i * 3 + 1] = 0;
                rgb[i * 3 + 2] = 0;
            }
            else
            {
                double normalized;
                if (depth < minDepth)
                    normalized = 0;
                else if (depth > maxDepth)
                    normalized = 1.0;
                else
                    normalized = (double)(depth - minDepth) / (maxDepth - minDepth);

                int idx = (int)(normalized * 255);
                idx = Math.Clamp(idx, 0, 255);

                rgb[i * 3] = _jetTable[idx].R;
                rgb[i * 3 + 1] = _jetTable[idx].G;
                rgb[i * 3 + 2] = _jetTable[idx].B;
            }
        }
        return rgb;
    }

    /// <summary>
    /// Convert 16-bit data to grayscale byte array for PNG output.
    /// </summary>
    public static byte[] ToGrayscale(byte[] data, int width, int height)
    {
        int pixelCount = width * height;
        byte[] gray = new byte[pixelCount];

        ushort min = ushort.MaxValue;
        ushort max = ushort.MinValue;
        for (int i = 0; i < pixelCount; i++)
        {
            ushort val = BitConverter.ToUInt16(data, i * 2);
            if (val > 0 && val < min) min = val;
            if (val > max) max = val;
        }

        if (max <= min)
        {
            for (int i = 0; i < pixelCount; i++)
            {
                ushort val = BitConverter.ToUInt16(data, i * 2);
                gray[i] = (byte)(val == 0 ? 0 : 255);
            }
        }
        else
        {
            double range = max - min;
            for (int i = 0; i < pixelCount; i++)
            {
                ushort val = BitConverter.ToUInt16(data, i * 2);
                if (val == 0)
                    gray[i] = 0;
                else
                    gray[i] = (byte)((val - min) / range * 255);
            }
        }
        return gray;
    }
}
