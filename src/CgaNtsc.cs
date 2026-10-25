namespace XdcConverter;

// Standard XDC mode 1: BIOS mode 6, white foreground, black border, colour burst on.
// The 1-bit CGA signal is decoded at 4 * 3.579545 MHz. This is deliberately a
// signal decoder, not a blur of the 16-colour preview palette.
public static class CgaNtsc
{
    private const int Width = 640, Height = 200, Pad = 48, Length = Width + 2 * Pad;
    public const double SampleRateMHz = 315.0 / 22;
    private static readonly float[] Chroma = LowPass(16, 1.0);
    private static readonly float[] Luma = LowPass(8, 3.0);
    private static readonly float[] Cos = [1, 0, -1, 0], Sin = [0, 1, 0, -1];

    // Measured 1-bit transition voltages from Andrew Jenner's CGA model.
    // Only the factual black/white samples are used, with an independently
    // implemented FIR decoder. See docs/CGA-NTSC.txt for sources and assumptions.
    private static readonly double[,] ChromaVoltage = {
        { 2, 2, 2, 2 }, { 151, 152, 151, 152 },
        { 61, 59, 60, 60 }, { 251, 252, 252, 252 }
    };
    private static readonly double[] Intensity = [77.175381, 166.564623, 88.654656, 174.228438];
    private static readonly float[,] OldSignal = BuildSignal(false), NewSignal = BuildSignal(true);

    private static float[] LowPass(int radius, double cutoffMHz)
    {
        var filter = new float[2 * radius + 1]; double sum = 0, frequency = cutoffMHz / SampleRateMHz;
        for (int x = -radius; x <= radius; x++)
        {
            double sinc = x == 0 ? 2 * frequency : Math.Sin(2 * Math.PI * frequency * x) / (Math.PI * x);
            // Blackman window: bounded support, no frame history or vertical phase mixing.
            double window = .42 + .5 * Math.Cos(Math.PI * x / radius) + .08 * Math.Cos(2 * Math.PI * x / radius);
            filter[x + radius] = (float)(sinc * window); sum += filter[x + radius];
        }
        for (int i = 0; i < filter.Length; i++) filter[i] /= (float)sum;
        return filter;
    }
    private static float[,] BuildSignal(bool newer)
    {
        double Voltage(int transition, int phase) => newer
            ? ChromaVoltage[transition, phase] * (.29 / .72) + Intensity[transition] * (.71 / .28)
            : ChromaVoltage[transition, phase] + Intensity[transition];
        double black = Voltage(0, 0), white = Enumerable.Range(0, 4).Average(p => Voltage(3, p));
        var table = new float[4, 4];
        for (int t = 0; t < 4; t++) for (int p = 0; p < 4; p++) table[t, p] = (float)((Voltage(t, p) - black) / (white - black));
        return table;
    }

    public static int[] Render(ReadOnlySpan<byte> vram, MonitorOptions options)
    {
        if (vram.Length != 16384) throw new ArgumentException("Invalid CGA memory size.");
        return RenderRows(vram, options, Height, true);
    }

    // The encoder evaluates repeating dither tiles through the player's signal path.
    internal static int[] RenderRows(ReadOnlySpan<byte> vram, MonitorOptions options, int rows, bool banked = false)
    {
        if (rows < 1 || rows > Height || vram.Length < (banked ? 16384 : rows * 80))
            throw new ArgumentException("Invalid CGA rows.");
        options.Validate();
        var pixels = new int[Width * rows];
        Span<float> signal = stackalloc float[Length];
        Span<float> uRaw = stackalloc float[Length], vRaw = stackalloc float[Length];
        Span<float> u = stackalloc float[Length], v = stackalloc float[Length], ySignal = stackalloc float[Length];
        var table = options.NewCga ? NewSignal : OldSignal;
        // Burst samples for CGA colour 6: [118,4,88,249]. Phase is aligned with
        // the mode-6 pixel boundary; +33 degrees defines the standard NTSC I axis.
        double burstPhase = Math.Atan2(4 - 249, 118 - 88);
        double angle = (33 - 90 + options.Hue) * Math.PI / 180 - burstPhase;
        float c = (float)(Math.Cos(angle) * options.Saturation), s = (float)(Math.Sin(angle) * options.Saturation);
        for (int row = 0; row < rows; row++)
        {
            int offset = banked ? (row & 1) * 8192 + (row / 2) * 80 : row * 80;
            var line = vram.Slice(offset, 80);
            for (int x = -Pad; x < Width + Pad; x++)
            {
                int left = x >= 0 && x < Width ? (line[x >> 3] >> (7 - (x & 7))) & 1 : 0;
                int right = x + 1 >= 0 && x + 1 < Width ? (line[(x + 1) >> 3] >> (7 - ((x + 1) & 7))) & 1 : 0;
                int index = x + Pad, phase = x & 3;
                float voltage = table[left * 2 + right, phase];
                signal[index] = voltage;
                uRaw[index] = 2 * voltage * Cos[phase]; vRaw[index] = 2 * voltage * Sin[phase];
            }
            // Synchronous chroma demodulation and 1 MHz bandwidth limiting.
            for (int x = 16; x < Length - 16; x++)
            {
                float uu = 0, vv = 0;
                for (int k = 0; k < Chroma.Length; k++) { uu += uRaw[x + k - 16] * Chroma[k]; vv += vRaw[x + k - 16] * Chroma[k]; }
                u[x] = uu; v[x] = vv;
                // Remove the remodulated colour carrier before filtering luminance.
                ySignal[x] = signal[x] - uu * Cos[x & 3] - vv * Sin[x & 3];
            }
            for (int x = 0; x < Width; x++)
            {
                int index = x + Pad; float y = 0;
                for (int k = 0; k < Luma.Length; k++) y += ySignal[index + k - 8] * Luma[k];
                float i = c * u[index] - s * v[index], q = s * u[index] + c * v[index];
                int r = Channel(y + .9563f * i + .6210f * q);
                int g = Channel(y - .2721f * i - .6474f * q);
                int b = Channel(y - 1.1069f * i + 1.7046f * q);
                pixels[row * Width + x] = unchecked((int)0xff000000) | (r << 16) | (g << 8) | b;
            }
        }
        return pixels;
    }
    private static int Channel(float value) => Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
