using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace XdcConverter;

public static class FrameImage
{
    public static Bitmap FromRgb(ReadOnlySpan<byte> rgb, int width, int height)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("Invalid RGB frame size.");
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var pixels = new int[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = unchecked((int)0xff000000) | (rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2];
        var data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); } finally { image.UnlockBits(data); }
        return image;
    }
    public static Bitmap Create(byte[] vram, int mode, MonitorOptions? monitor = null)
    {
        if (vram.Length != 16384 || mode is < 1 or > 3) throw new ArgumentException("Invalid frame");
        int[] palette = mode == 1 ? FrameWriter.Composite : FrameWriter.Tandy;
        var options = monitor ?? MonitorSettings.Current;
        bool composite = mode == 1 && options.Enabled;
        var colors = composite ? CgaNtsc.Render(vram, options) : new int[640 * 200];
        if (!composite) for (int y = 0; y < 200; y++)
        {
            int row = (y & 1) * 8192 + y / 2 * 80;
            for (int x = 0; x < 640; x++)
            {
                int color;
                if (mode == 2) color = (vram[row + x / 8] & (128 >> (x & 7))) != 0 ? 0xffffff : 0;
                else
                {
                    int pixel = x / 4;
                    color = palette[(vram[row + pixel / 2] >> ((pixel & 1) == 0 ? 4 : 0)) & 15];
                }
                colors[y * 640 + x] = unchecked((int)0xff000000) | color;
            }
        }
        using var native = new Bitmap(640, 200, PixelFormat.Format32bppArgb);
        var data = native.LockBits(new Rectangle(0, 0, 640, 200), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { Marshal.Copy(colors, 0, data.Scan0, colors.Length); } finally { native.UnlockBits(data); }
        var image = new Bitmap(640, 480);
        using var graphics = Graphics.FromImage(image);
        graphics.InterpolationMode = composite ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear : System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        graphics.DrawImage(native, new Rectangle(0, 0, 640, 480));
        return image;
    }
}
