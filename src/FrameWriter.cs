namespace XdcConverter;

public sealed class FrameWriter
{
    public static readonly int[] Composite = [0x090909,0x009c3a,0x4018ff,0x00c5ff,0xe3054b,0x9a9d96,0xff1dff,0xddc5ff,0x4c7400,0x07ff00,0x9e9a9a,0x53ffd2,0xff7800,0xf5ff00,0xff98fa,0xffffff];
    public static readonly int[] Tandy = [0x000000,0x0000aa,0x00aa00,0x00aaaa,0xaa0000,0xaa00aa,0xaa5500,0xaaaaaa,0x555555,0x5555ff,0x55ff55,0x55ffff,0xff5555,0xff55ff,0xffff55,0xffffff];
    internal static readonly int[] Bayer = [0,48,12,60,3,51,15,63,32,16,44,28,35,19,47,31,8,56,4,52,11,59,7,55,40,24,36,20,43,27,39,23,2,50,14,62,1,49,13,61,34,18,46,30,33,17,45,29,10,58,6,54,9,57,5,53,42,26,38,22,41,25,37,21];
    private readonly int width, height, mode;
    private readonly DitherMask mask;
    private readonly byte[] lookup = new byte[32768 * 64];
    private readonly byte[] gamma = new byte[256];

    public FrameWriter(Job job, CancellationToken ct = default)
    {
        job.ValidateColor();
        width = job.Width; height = job.Height; mode = job.Mode; mask = DitherMask.Get(job.Dither);
        bool ntsc = mode == 1 && job.CompositeAlgorithm == "ntsc";
        var plans = mode == 2 ? null : ntsc
            ? NtscQuantizer.Plans(job.CompositeNewCga, job.Dither == "none" ? 0 : job.DitherStrength, ct, job.Dither)
            : ColorMixer.Plans(mode);
        double strength = ntsc ? 1 : job.Dither == "none" ? 0 : job.DitherStrength / 100.0;
        // Apply gamma before reducing RGB precision so dark input values survive
        // when shadows are lifted. Saturation/mixing use a bounded per-job lookup.
        for (int i = 0; i < 256; i++) gamma[i] = (byte)Math.Round(Math.Pow(i / 255.0, 1 / job.Gamma) * 255);
        var linear = Enumerable.Range(0, 32).Select(v => ColorMixer.Linear(v / 31.0)).ToArray();
        for (int r = 0; r < 32; r++) for (int g = 0; g < 32; g++) for (int b = 0; b < 32; b++)
        {
            ct.ThrowIfCancellationRequested();
            double rr = linear[r], gg = linear[g], bb = linear[b];
            double luma = 0.2126 * rr + 0.7152 * gg + 0.0722 * bb;
            int offset = ((r << 10) | (g << 5) | b) * 64;
            if (mode == 2)
            {
                // Saturation has no effect on monochrome; gamma and strength do.
                double light = ColorMixer.Srgb(luma);
                for (int d = 0; d < 64; d++) lookup[offset + d] = (byte)(light >= 0.5 + strength * ((d + 0.5) / 64 - 0.5) ? 1 : 0);
                continue;
            }
            int Adjust(double value) => (int)Math.Round(ColorMixer.Srgb(Math.Clamp(luma + (value - luma) * job.Saturation, 0, 1)) * 31);
            var plan = plans![(Adjust(rr) << 10) | (Adjust(gg) << 5) | Adjust(bb)];
            int countB = (int)Math.Round(plan.CountB * strength);
            int countA = (int)Math.Round((64 - plan.CountB) * strength);
            for (int d = 0; d < 64; d++) lookup[offset + d] = d < countB ? plan.B : d < countB + countA ? plan.A : plan.Nearest;
        }
    }

    public byte[] Quantize(byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("Invalid RGB frame size.");
        var result = new byte[width * height];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x, p = i * 3;
            int color = ((gamma[rgb[p]] >> 3) << 10) | ((gamma[rgb[p + 1]] >> 3) << 5) | (gamma[rgb[p + 2]] >> 3);
            result[i] = lookup[color * 64 + mask.At(x, y)];
        }
        return result;
    }

    public Bitmap Preview(byte[] pixels)
    {
        var vram = new byte[16384];
        NativeEncoder.PackPixels(pixels, mode, height, vram);
        return FrameImage.Create(vram, mode);
    }
}
