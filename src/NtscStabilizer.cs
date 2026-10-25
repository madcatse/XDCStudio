namespace XdcConverter;

public sealed record StabilizationStatistics(long Frames, long ProposedByteChanges, long HeldByteChanges, long SceneCuts);

// Sequential conversion only. History is the actual reconstructed VRAM supplied
// by the encoder, never an assumed previous ideal frame. All buffers stay in RAM.
internal sealed class NtscStabilizer
{
    private readonly int height;
    private readonly double strength, saturation;
    private readonly MonitorOptions monitor;
    private readonly byte[] gamma = new byte[256];
    private readonly double[] linear = Enumerable.Range(0, 256).Select(v => ColorMixer.Linear(v / 255.0)).ToArray();
    private ColorMixer.Rgb[]? previousSource;
    private readonly byte[] desired = new byte[16384];
    private readonly float[] benefit = new float[16384];
    private readonly float[] cost = new float[16384];
    private long frames, proposed, held, cuts;
    internal bool SceneCut { get; private set; }
    internal ReadOnlySpan<float> Benefits => benefit;
    internal StabilizationStatistics Statistics => new(frames, proposed, held, cuts);

    internal NtscStabilizer(Job job)
    {
        job.ValidateColor(); height = job.Height; strength = job.TemporalStability / 50.0; saturation = job.Saturation;
        monitor = new(NewCga: job.CompositeNewCga);
        for (int i = 0; i < 256; i++) gamma[i] = (byte)Math.Round(Math.Pow(i / 255.0, 1 / job.Gamma) * 255);

    }

    internal void Apply(byte[] rgb, byte[] pixels, ReadOnlySpan<byte> reconstructed, CancellationToken ct = default)
    {
        if (rgb.Length != 160 * height * 3 || pixels.Length != 160 * height || reconstructed.Length != 16384)
            throw new ArgumentException("Invalid stabilization frame.");
        ct.ThrowIfCancellationRequested();
        NativeEncoder.PackPixels(pixels, 1, height, desired);
        Array.Clear(benefit); Array.Clear(cost);
        var source = new ColorMixer.Rgb[pixels.Length]; var sourceLight = new ColorMixer.Rgb[pixels.Length]; double motion = 0;
        for (int i = 0; i < source.Length; i++)
        {
            int p = i * 3;
            // Retain full source precision here: crossing a 5-bit quantizer
            // boundary must not turn a 1/255 input change into false motion.
            double r = linear[gamma[rgb[p]]], g = linear[gamma[rgb[p + 1]]], b = linear[gamma[rgb[p + 2]]];
            double luma = .2126 * r + .7152 * g + .0722 * b;
            sourceLight[i] = new(Math.Clamp(luma + (r - luma) * saturation, 0, 1), Math.Clamp(luma + (g - luma) * saturation, 0, 1), Math.Clamp(luma + (b - luma) * saturation, 0, 1));
            source[i] = ColorMixer.Lab(sourceLight[i]);
            if (previousSource != null) motion += ColorMixer.Distance(source[i], previousSource[i]);
        }
        SceneCut = previousSource != null && motion / source.Length > .025;
        if (SceneCut) cuts++;
        if (desired.AsSpan().SequenceEqual(reconstructed)) { previousSource = source; frames++; return; }
        var oldScreen = CgaNtsc.Render(reconstructed, monitor);
        var newScreen = CgaNtsc.Render(desired, monitor);
        ct.ThrowIfCancellationRequested();
        ColorMixer.Rgb Cell(int[] screen, int x, int y)
        {
            var sum = new ColorMixer.Rgb(); int p = y * 640 + x * 4;
            for (int d = 0; d < 4; d++) { int c = screen[p + d]; sum += new ColorMixer.Rgb(linear[(c >> 16) & 255], linear[(c >> 8) & 255], linear[c & 255]); }
            return ColorMixer.Lab(sum * .25);
        }
        // Estimate per-byte replay work, including setup amortization. Isolated
        // writes are more expensive than runs. NativeEncoder still enforces the
        // exact generated-code and disk budgets after this approximation.
        for (int y = 0; y < height; y++)
        {
            int sy = height == 100 ? 2 * y + 1 : y, row = (sy & 1) * 8192 + sy / 2 * 80;
            for (int x = 0; x < 80;)
            {
                if (desired[row + x] == reconstructed[row + x]) { x++; continue; }
                int start = x++; bool fill = true;
                while (x < 80 && desired[row + x] != reconstructed[row + x]) { fill &= desired[row + x] == desired[row + start]; x++; }
                int length = x - start;
                float cycles = (float)((fill && length >= 9 ? 13.7 : 19.2) + 36.0 / length);
                for (int k = start; k < x; k++) cost[row + k] = cycles;
            }
            for (int x = 0; x < 160; x += 2)
            {
                int at = row + x / 2; if (desired[at] == reconstructed[at]) continue;
                proposed++; int p = y * 160 + x;
                var oa = Cell(oldScreen, x, sy); var ob = Cell(oldScreen, x + 1, sy);
                var na = Cell(newScreen, x, sy); var nb = Cell(newScreen, x + 1, sy);
                double oldError = (ColorMixer.Distance(source[p], oa) + ColorMixer.Distance(source[p + 1], ob)) * .5;
                double newError = (ColorMixer.Distance(source[p], na) + ColorMixer.Distance(source[p + 1], nb)) * .5;
                benefit[at] = (float)Math.Max(.000001, oldError - newError);

            }
        }
        // Hold a small coherent 4x4 region, rather than independently choosing
        // whichever individual dither dot looks closest. The latter destroys
        // the intended mean colour. Motion uses the maximum source-cell change.
        if (!SceneCut && previousSource != null)
        for (int by = 0; by < height; by += 4) for (int bx = 0; bx < 160; bx += 4)
        {
            var t = new ColorMixer.Rgb(); var o = new ColorMixer.Rgb(); var n = new ColorMixer.Rgb();
            double movement = 0, work = 0; int changes = 0;
            for (int y = by; y < by + 4; y++)
            {
                int sy = height == 100 ? 2 * y + 1 : y, row = (sy & 1) * 8192 + sy / 2 * 80;
                for (int x = bx; x < bx + 4; x++)
                {
                    int p = y * 160 + x; t += sourceLight[p];
                    movement = Math.Max(movement, ColorMixer.Distance(source[p], previousSource[p]));
                    for (int d = 0; d < 4; d++)
                    {
                        int oc = oldScreen[sy * 640 + x * 4 + d], nc = newScreen[sy * 640 + x * 4 + d];
                        o += new ColorMixer.Rgb(linear[(oc >> 16) & 255], linear[(oc >> 8) & 255], linear[oc & 255]);
                        n += new ColorMixer.Rgb(linear[(nc >> 16) & 255], linear[(nc >> 8) & 255], linear[nc & 255]);
                    }
                }
                for (int x = bx / 2; x < bx / 2 + 2; x++) if (desired[row + x] != reconstructed[row + x]) { work += cost[row + x]; changes++; }
            }
            if (changes == 0 || movement >= .0004) continue;
            t = ColorMixer.Lab(t * (1 / 16.0)); o = ColorMixer.Lab(o * (1 / 64.0)); n = ColorMixer.Lab(n * (1 / 64.0));
            double allowance = .00006 * strength * Math.Clamp(work / changes / 20, .7, 2.8);
            if (ColorMixer.Distance(o, n) > .0005 * strength || ColorMixer.Distance(t, o) > ColorMixer.Distance(t, n) + allowance) continue;
            for (int y = by; y < by + 4; y++)
            {
                int sy = height == 100 ? 2 * y + 1 : y, row = (sy & 1) * 8192 + sy / 2 * 80;
                for (int x = bx; x < bx + 4; x += 2)
                {
                    int at = row + x / 2, p = y * 160 + x;
                    if (desired[at] != reconstructed[at]) held++;
                    pixels[p] = (byte)(reconstructed[at] >> 4); pixels[p + 1] = (byte)(reconstructed[at] & 15); benefit[at] = 0;
                }
            }
        }
        previousSource = source; frames++;
    }
}
