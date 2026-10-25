namespace XdcConverter;

// Palette order is a hardware bit pattern, never an adaptive/reordered palette.
internal static class ColorMixer
{
    internal readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
        public static Rgb operator -(Rgb a, Rgb b) => new(a.R - b.R, a.G - b.G, a.B - b.B);
        public static Rgb operator *(Rgb a, double t) => new(a.R * t, a.G * t, a.B * t);
        public double Dot(Rgb b) => R * b.R + G * b.G + B * b.B;
    }
    internal readonly record struct Plan(byte Nearest, byte A, byte B, byte CountB);
    private static readonly Lazy<Plan[]> CompositePlans = new(() => Build(FrameWriter.Composite));
    private static readonly Lazy<Plan[]> TandyPlans = new(() => Build(FrameWriter.Tandy));
    internal static Plan[] Plans(int mode) => mode == 1 ? CompositePlans.Value : TandyPlans.Value;
    internal static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    internal static double Srgb(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    internal static Rgb FromColor(int color) => new(Linear((color >> 16) / 255.0), Linear(((color >> 8) & 255) / 255.0), Linear((color & 255) / 255.0));

    // Björn Ottosson's public-domain linear-sRGB -> Oklab transform:
    // https://bottosson.github.io/posts/oklab/
    internal static Rgb Lab(Rgb c)
    {
        double l = Math.Cbrt(0.4122214708 * c.R + 0.5363325363 * c.G + 0.0514459929 * c.B);
        double m = Math.Cbrt(0.2119034982 * c.R + 0.6806995451 * c.G + 0.1073969566 * c.B);
        double s = Math.Cbrt(0.0883024619 * c.R + 0.2817188376 * c.G + 0.6299787005 * c.B);
        return new(0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }
    internal static double Distance(Rgb a, Rgb b) { var d = a - b; return d.Dot(d); }

    private static Plan[] Build(int[] palette)
    {
        var colors = palette.Select(FromColor).ToArray();
        var labs = colors.Select(Lab).ToArray();
        var plans = new Plan[32768];
        for (int r = 0; r < 32; r++) for (int g = 0; g < 32; g++) for (int b = 0; b < 32; b++)
        {
            var target = new Rgb(Linear(r / 31.0), Linear(g / 31.0), Linear(b / 31.0));
            var targetLab = Lab(target);
            double best = double.MaxValue; byte nearest = 0;
            for (byte i = 0; i < 16; i++)
            {
                double error = Distance(labs[i], targetLab);
                if (error < best) { best = error; nearest = i; }
            }
            var plan = new Plan(nearest, nearest, nearest, 0);
            for (byte a = 0; a < 15; a++) for (byte c = (byte)(a + 1); c < 16; c++)
            {
                var delta = colors[c] - colors[a];
                // Project in linear light; evaluate the actual 8x8 mixture perceptually.
                int count = (int)Math.Round(Math.Clamp((target - colors[a]).Dot(delta) / delta.Dot(delta), 0, 1) * 64);
                if (count is 0 or 64) continue;
                double t = count / 64.0;
                var mixed = colors[a] + delta * t;
                // Penalize high-contrast speckling, especially black/white mixtures.
                double score = Distance(Lab(mixed), targetLab) + 0.025 * t * (1 - t) * Distance(labs[a], labs[c]);
                if (score < best) { best = score; plan = new(nearest, a, c, (byte)count); }
            }
            plans[(r << 10) | (g << 5) | b] = plan;
        }
        return plans;
    }
}
