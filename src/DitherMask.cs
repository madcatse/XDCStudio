namespace XdcConverter;

internal sealed class DitherMask(int size, int[] ranks)
{
    internal int Size { get; } = size;
    internal int Step => 64 / (Size * Size);
    // Normalize thresholds to the existing 64-entry colour lookup.
    internal int At(int x, int y) => ranks[(y % Size) * Size + x % Size] * Step + (Step - 1) / 2;
    internal static readonly DitherMask Bayer8 = new(8, FrameWriter.Bayer);
    internal static readonly DitherMask Bayer4 = new(4, [0,12,3,15,8,4,11,7,2,14,1,13,10,6,9,5]);
    internal static readonly DitherMask Blue = new(8, MakeBlueNoise());
    internal static DitherMask Get(string name) => name switch
    {
        "bayer" or "none" => Bayer8, "bayer4" => Bayer4, "blue" => Blue,
        _ => throw new ArgumentException("Unknown dither mask.")
    };

    // Deterministic, periodic void-and-cluster construction (Ulichney).
    // A fixed seed constructs ONE mask; it never changes with time or video.
    private static int[] MakeBlueNoise()
    {
        const int n = 64, side = 8;
        var kernel = new double[n, n];
        for (int a = 0; a < n; a++) for (int b = 0; b < n; b++)
        {
            int dx = Math.Abs(a % side - b % side), dy = Math.Abs(a / side - b / side);
            dx = Math.Min(dx, side - dx); dy = Math.Min(dy, side - dy);
            kernel[a, b] = Math.Exp(-(dx * dx + dy * dy) / (2 * 1.5 * 1.5));
        }
        int Pick(bool[] pattern, bool occupied, bool densest)
        {
            int best = -1; double score = densest ? double.NegativeInfinity : double.PositiveInfinity;
            for (int i = 0; i < n; i++) if (pattern[i] == occupied)
            {
                double sum = 0; for (int j = 0; j < n; j++) if (pattern[j]) sum += kernel[i, j];
                if (densest ? sum > score + 1e-12 : sum < score - 1e-12) { best = i; score = sum; }
            }
            return best;
        }
        var order = Enumerable.Range(0, n).ToArray(); uint seed = 0x58444337;
        for (int i = n - 1; i > 0; i--)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            int j = (int)(seed % (uint)(i + 1)); (order[i], order[j]) = (order[j], order[i]);
        }
        var initial = new bool[n]; for (int i = 0; i < 13; i++) initial[order[i]] = true;
        for (int iteration = 0; iteration < 4096; iteration++)
        {
            int cluster = Pick(initial, true, true); initial[cluster] = false;
            int empty = Pick(initial, false, false); initial[empty] = true;
            if (empty == cluster) break;
        }
        var ranks = new int[n]; var current = (bool[])initial.Clone();
        for (int rank = 12; rank >= 0; rank--)
        { int i = Pick(current, true, true); ranks[i] = rank; current[i] = false; }
        current = (bool[])initial.Clone();
        for (int rank = 13; rank < n / 2; rank++)
        { int i = Pick(current, false, false); ranks[i] = rank; current[i] = true; }
        for (int i = 0; i < n; i++) current[i] = !current[i];
        for (int rank = n / 2; rank < n; rank++)
        { int i = Pick(current, true, true); ranks[i] = rank; current[i] = false; }
        return ranks;
    }
}
