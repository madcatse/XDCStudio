namespace XdcConverter;

// Fixed screen-anchored Bayer tiles keep still images stable. Candidates are
// measured AFTER NTSC decoding: transitions, bandwidth and display clipping.
internal static class NtscQuantizer
{
    private readonly record struct Candidate(ColorMixer.Plan Plan, ColorMixer.Rgb Lab, double Texture);
    private static readonly Dictionary<(bool, string), Lazy<Candidate[]>> Models = [];
    private static readonly Dictionary<(bool Late, int Count, string Mask), ColorMixer.Plan[]> Cache = [];
    private static readonly Queue<(bool Late, int Count, string Mask)> Order = [];

    internal static ColorMixer.Plan[] Plans(bool newer, int strength, CancellationToken ct, string maskName = "bayer")
    {
        // Limit minority coverage. Zero is solid colours; 100 permits all pairs.
        var mask = DitherMask.Get(maskName);
        int count = (int)Math.Round(32 * strength / 100.0 / mask.Step) * mask.Step;
        if (count == 0) { maskName = "bayer"; mask = DitherMask.Bayer8; }
        var key = (newer, count, maskName);
        lock (Cache) if (Cache.TryGetValue(key, out var cached)) return cached;
        ct.ThrowIfCancellationRequested();
        Lazy<Candidate[]> model;
        lock (Cache)
        {
            if (!Models.TryGetValue((newer, maskName), out model!))
                Models.Add((newer, maskName), model = new(() => Measure(newer, mask)));
        }
        var candidates = model.Value.Where(c => c.Plan.CountB <= count).ToArray();
        var plans = new ColorMixer.Plan[32768];
        var linear = Enumerable.Range(0, 32).Select(v => ColorMixer.Linear(v / 31.0)).ToArray();
        for (int r = 0; r < 32; r++) for (int g = 0; g < 32; g++) for (int b = 0; b < 32; b++)
        {
            ct.ThrowIfCancellationRequested();
            var target = ColorMixer.Lab(new(linear[r], linear[g], linear[b]));
            double best = double.MaxValue; ColorMixer.Plan plan = default;
            foreach (ref readonly var candidate in candidates.AsSpan())
            {
                double score = ColorMixer.Distance(target, candidate.Lab) + .08 * candidate.Texture;
                if (score < best) { best = score; plan = candidate.Plan; }
            }
            plans[(r << 10) | (g << 5) | b] = plan;
        }
        ct.ThrowIfCancellationRequested();
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            while (Order.Count >= 8) Cache.Remove(Order.Dequeue());
            Cache.Add(key, plans); Order.Enqueue(key);
        }
        return plans;
    }

    private static Candidate[] Measure(bool newer, DitherMask mask)
    {
        var result = new List<Candidate>();
        var rows = new byte[80 * 8]; var cells = new ColorMixer.Rgb[64];
        var options = new MonitorOptions(NewCga: newer);
        void Add(byte a, byte b, byte count)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 160; x += 2)
            {
                int left = mask.At(x, y) < count ? b : a;
                int right = mask.At(x + 1, y) < count ? b : a;
                rows[y * 80 + x / 2] = (byte)((left << 4) | right);
            }
            var decoded = CgaNtsc.RenderRows(rows, options, 8);
            var mean = new ColorMixer.Rgb();
            // Average a central period with full neighbour context in linear light.
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                var cell = new ColorMixer.Rgb();
                for (int dot = 0; dot < 4; dot++) cell += ColorMixer.FromColor(decoded[y * 640 + 288 + x * 4 + dot] & 0xffffff);
                cell *= .25; cells[y * 8 + x] = ColorMixer.Lab(cell); mean += cell;
            }
            var lab = ColorMixer.Lab(mean * (1 / 64.0));
            double texture = cells.Average(c => ColorMixer.Distance(c, lab));
            result.Add(new(new(a, a, b, count), lab, texture));
        }
        for (byte a = 0; a < 16; a++) Add(a, a, 0);
        // Both Bayer orientations matter to the horizontal analogue signal.
        for (byte a = 0; a < 16; a++) for (byte b = 0; b < 16; b++) if (a != b)
            for (int count = mask.Step; count <= 32; count += mask.Step) Add(a, b, (byte)count);
        return result.ToArray();
    }
}
