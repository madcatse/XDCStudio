// Port of XDC's delta compiler, Copyright (c) 2014 Jim Leonard, MIT.
// Upstream: MobyGamer/XDC, revision 56a5dc1951e1f0b8747fe8fa4614c607e2f91c8b.
// Host pointers/assembler are replaced by bounded arrays. Output is still 8088 code.
namespace XdcConverter;

public sealed record EncoderStatistics(long Frames, long SourceFramesAccepted, long Deltas,
    long VideoBytes, double MaximumCycles, double CycleBudget, int VideoByteBudget);

public sealed class NativeEncoder
{
    public const int ScreenBytes = 16384;
    public const double DefaultFps = (60000.0 / 1001) * (227.5 / 228) * (262.5 / 262);
    private const int FrameOverhead = 16, MinimumRun = 9;
    private const double FrameCycles = 118, MoveByteCycles = 38.4 / 2, StoreByteCycles = 27.4 / 2;
    private readonly int mode, height, integrity, maxVideoBytes;
    private readonly double cycleBudget;
    private readonly byte[] target = new byte[ScreenBytes], reconstructed = new byte[ScreenBytes];
    private readonly byte[] intended = new byte[ScreenBytes];
    private List<Delta> pending = [];
    private int previousPendingBytes;
    private double[]? priorityPrefix;
    private long frameCount, sourceCount, deltaCount, videoBytes;
    private double maximumCycles;

    private sealed record Delta(int Start, int Length, bool Run, byte Fill, bool Frozen = false)
    {
        public int End => Start + Length;
        public double ReplayCycles => Length * (Run ? StoreByteCycles : MoveByteCycles);
    }
    private sealed record Encoding(byte[] Code, int DataLength, double Cycles, byte? Al)
    {
        public int Size => Code.Length + DataLength;
    }

    public NativeEncoder(int mode, int height, double fps, int diskKiB, int audioChunk, int integrity)
    {
        if (mode is < 1 or > 3 || (height != 200 && !(mode == 1 && height == 100))) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(fps) || fps < 10 || fps > 60 || diskKiB < 1 || audioChunk < 1 || integrity is < 0 or > 100) throw new ArgumentException("Invalid encoder parameters.");
        this.mode = mode; this.height = height; this.integrity = integrity;
        // The audio and sector padding count toward the disk bandwidth, too.
        int packetBudget = (int)Math.Floor(diskKiB * 1024.0 / fps / 512) * 512;
        maxVideoBytes = Math.Min(8000, packetBudget - audioChunk);
        if (maxVideoBytes < 64) throw new ArgumentException(L.T("Недостаточный поток для выбранных FPS и звука.", "Data rate is too low for the selected FPS and audio."));
        cycleBudget = (76 * 262 * 4 / 2.0) * DefaultFps / fps;
    }

    public EncoderStatistics Statistics => new(frameCount, sourceCount, deltaCount, videoBytes, maximumCycles, cycleBudget, maxVideoBytes);
    public ReadOnlySpan<byte> Reconstructed => reconstructed;

    public static void PackPixels(ReadOnlySpan<byte> pixels, int mode, int height, Span<byte> destination)
    {
        int width = mode == 2 ? 640 : 160;
        if (destination.Length != ScreenBytes || pixels.Length != width * height || mode is < 1 or > 3 ||
            (height != 200 && !(mode == 1 && height == 100))) throw new ArgumentException("Invalid frame dimensions.");
        destination.Clear();
        for (int y = 0; y < height; y++)
        {
            // Original 160x100 BMP loader writes only the odd scanline bank.
            int screenY = height == 100 ? y * 2 + 1 : y;
            int offset = (screenY & 1) * 8192 + (screenY / 2) * 80;
            var row = pixels.Slice(y * width, width);
            if (mode == 2)
            {
                for (int x = 0; x < 640; x++)
                {
                    if (row[x] > 1) throw new ArgumentException("Invalid monochrome palette index.");
                    destination[offset + x / 8] |= (byte)(row[x] << (7 - x % 8));
                }
            }
            else for (int x = 0; x < 160; x += 2)
            {
                if ((row[x] | row[x + 1]) > 15) throw new ArgumentException("Invalid color palette index.");
                destination[offset + x / 2] = (byte)(row[x] << 4 | row[x + 1]);
            }
        }
    }

    public byte[] Encode(ReadOnlySpan<byte> pixels, ReadOnlySpan<float> benefits = default, bool sceneCut = false)
    {
        if (!benefits.IsEmpty && benefits.Length != ScreenBytes) throw new ArgumentException("Invalid update benefits.");
        int remaining = pending.Sum(d => d.Length);
        double completed = previousPendingBytes == 0 ? 100 : 100.0 - remaining * 100.0 / previousPendingBytes;
        if (sceneCut || pending.Count == 0 || completed >= integrity)
        {
            PackPixels(pixels, mode, height, target);
            if (benefits.IsEmpty) priorityPrefix = null;
            else
            {
                priorityPrefix ??= new double[ScreenBytes + 1]; priorityPrefix[0] = 0;
                for (int i = 0; i < ScreenBytes; i++) priorityPrefix[i + 1] = priorityPrefix[i] + Math.Max(.000001, benefits[i]);
            }
            pending = FindDeltas();
            Optimize();
            sourceCount++;
        }
        previousPendingBytes = pending.Sum(d => d.Length);
        var opcodes = new List<byte>(maxVideoBytes) { 0x1e, 0x0e, 0x1f, 0xbe, 0, 0, 0xb8, 0, 0xb8, 0x8e, 0xc0, 0xb5, 0, 0xfc };
        var literals = new List<byte>(maxVideoBytes);
        int bytePool = maxVideoBytes - FrameOverhead;
        double cycles = FrameCycles;
        byte? cachedAl = null;
        Array.Copy(reconstructed, intended, ScreenBytes);
        var uncommitted = new List<Delta>(pending.Count);
        int committed = 0;
        foreach (var delta in pending)
        {
            var encoding = Generate(delta, cachedAl);
            if (encoding.Size > bytePool || cycles + encoding.Cycles > cycleBudget)
            {
                uncommitted.Add(delta);
                continue;
            }
            opcodes.AddRange(encoding.Code);
            if (encoding.DataLength > 0) literals.AddRange(target.AsSpan(delta.Start, encoding.DataLength).ToArray());
            if (encoding.Al.HasValue) cachedAl = encoding.Al.Value;
            target.AsSpan(delta.Start, delta.Length).CopyTo(intended.AsSpan(delta.Start));
            bytePool -= encoding.Size; cycles += encoding.Cycles; committed++;
        }
        pending = uncommitted;
        // Recovery for a residual delta that cannot fit in either budget. Unlike
        // the Pascal SplitAll, splitting a two-byte delta never makes an empty range.
        if (committed == 0 && pending.Count > 0)
        {
            pending = pending.SelectMany(d => d.Length > 1
                ? new[] { Describe(d.Start, d.Length / 2), Describe(d.Start + d.Length / 2, d.Length - d.Length / 2) }
                : new[] { d }).ToList();
            Sort(pending);
        }
        opcodes.Add(0x1f); opcodes.Add(0xcb);
        int dataOffset = opcodes.Count;
        opcodes[4] = (byte)dataOffset; opcodes[5] = (byte)(dataOffset >> 8);
        byte[] video = new byte[dataOffset + literals.Count];
        opcodes.CopyTo(video, 0); literals.CopyTo(video, dataOffset);
        if (video.Length > maxVideoBytes) throw new InvalidOperationException("Video budget exceeded.");
        // Decode emitted code, rather than assuming the intended writes happened.
        // This is the portable replacement for the original CALL combinedBuf.
        NativeVideoDecoder.Apply(video, reconstructed);
        if (!reconstructed.AsSpan().SequenceEqual(intended)) throw new InvalidOperationException("Generated 8088 code does not reconstruct the committed deltas.");
        frameCount++; deltaCount += committed; videoBytes += video.Length; maximumCycles = Math.Max(maximumCycles, cycles);
        return video;
    }

    private Delta Describe(int start, int length, bool frozen = false)
    {
        if (start < 0 || length < 1 || start + length > ScreenBytes) throw new InvalidOperationException("Invalid delta range.");
        bool run = length >= MinimumRun && target.AsSpan(start, length).IndexOfAnyExcept(target[start]) < 0;
        return new(start, length, run, run ? target[start] : (byte)0, frozen);
    }

    private List<Delta> FindDeltas()
    {
        var result = new List<Delta>();
        int offset = 0;
        while (offset < ScreenBytes)
        {
            if (target[offset] == reconstructed[offset]) { offset++; continue; }
            int start = offset++;
            while (offset < ScreenBytes && target[offset] != reconstructed[offset]) offset++;
            result.Add(Describe(start, offset - start));
        }
        return result;
    }

    private void Optimize()
    {
        pending = ExtractRuns(pending);
        pending = Subdivide(pending);
        Concatenate();
        pending = ExtractRuns(pending);
        pending = Subdivide(pending);
        Sort(pending);
    }

    private List<Delta> ExtractRuns(List<Delta> input)
    {
        var output = new List<Delta>(input.Count);
        foreach (var d in input)
        {
            if (d.Run || d.Length < MinimumRun) { output.Add(d); continue; }
            int start = d.Start, cursor = start;
            while (cursor < d.End)
            {
                int runStart = cursor++;
                while (cursor < d.End && target[cursor] == target[runStart]) cursor++;
                if (cursor - runStart < MinimumRun) continue;
                if (runStart > start) output.Add(Describe(start, runStart - start));
                output.Add(Describe(runStart, cursor - runStart));
                start = cursor;
            }
            if (start < d.End) output.Add(Describe(start, d.End - start, d.Frozen));
        }
        return output;
    }

    private List<Delta> Subdivide(List<Delta> input)
    {
        var output = new List<Delta>(input.Count);
        double available = (cycleBudget - FrameCycles) * 0.99;
        foreach (var d in input)
        {
            int limit = Math.Max(1, (int)((available - 64) / (d.Run ? StoreByteCycles : MoveByteCycles)));
            if (!d.Run) limit = Math.Min(limit, maxVideoBytes - FrameOverhead - 9);
            if (d.Length <= limit) { output.Add(d); continue; }
            for (int start = d.Start; start < d.End; start += limit)
                output.Add(Describe(start, Math.Min(limit, d.End - start), frozen: true));
        }
        return output;
    }

    private static int SliceSize(int length) => Math.Min(length + 7, length <= 6 ? 3 + (length + 1) / 2 + length : int.MaxValue);

    private void Concatenate()
    {
        pending.Sort((a, b) => a.Start.CompareTo(b.Start));
        int maxLength = Math.Min(maxVideoBytes - FrameOverhead - 9, (int)(((cycleBudget - FrameCycles) * 0.99 - 64) / MoveByteCycles));
        var result = new List<Delta>(pending.Count);
        foreach (var d in pending)
        {
            if (result.Count > 0)
            {
                var previous = result[^1];
                int length = d.End - previous.Start;
                if (d.Start < previous.End) throw new InvalidOperationException("Overlapping deltas.");
                if (!previous.Run && !d.Run && !previous.Frozen && !d.Frozen && d.Start - (previous.End - 1) <= 7 &&
                    length <= maxLength && SliceSize(length) < SliceSize(previous.Length) + SliceSize(d.Length))
                {
                    result[^1] = Describe(previous.Start, length);
                    continue;
                }
            }
            result.Add(d);
        }
        pending = result;
    }

    private void Sort(List<Delta> deltas) => deltas.Sort((a, b) =>
    {
        if (priorityPrefix != null)
        {
            // Larger visible NTSC improvement per estimated replay cycle first.
            // Prefix sums avoid rescanning long spans during sorting. Values are
            // captured with the accepted target, including its unfinished deltas.
            double Priority(Delta d) => (priorityPrefix[d.End] - priorityPrefix[d.Start]) / (d.ReplayCycles + 48);
            int priority = Priority(b).CompareTo(Priority(a));
            if (priority != 0) return priority;
        }
        int result = b.Length.CompareTo(a.Length);
        if (result == 0) result = a.ReplayCycles.CompareTo(b.ReplayCycles);
        if (result == 0) result = b.Fill.CompareTo(a.Fill);
        return result == 0 ? a.Start.CompareTo(b.Start) : result;
    });

    private Encoding Generate(Delta delta, byte? cachedAl)
    {
        var options = new List<Encoding>(3);
        List<byte> Start() => [0xbf, (byte)delta.Start, (byte)(delta.Start >> 8)];
        void Count(List<byte> code)
        {
            if (delta.Length <= 255) code.AddRange([0xb1, (byte)delta.Length]);
            else code.AddRange([0xb9, (byte)delta.Length, (byte)(delta.Length >> 8)]);
        }
        if (delta.Run)
        {
            var code = Start(); Count(code);
            bool setAl = cachedAl != delta.Fill;
            if (setAl) code.AddRange([0xb0, delta.Fill]);
            code.AddRange([0xf3, 0xaa]);
            options.Add(new(code.ToArray(), 0, 12 + (delta.Length <= 255 ? 8 : 12) + (setAl ? 8 : 0) + 4 + (4 + StoreByteCycles) + delta.Length * StoreByteCycles,
                setAl ? delta.Fill : null));
        }
        if (delta.Length <= 6)
        {
            var code = Start();
            for (int i = 0; i < delta.Length / 2; i++) code.Add(0xa5);
            if ((delta.Length & 1) != 0) code.Add(0xa4);
            options.Add(new(code.ToArray(), delta.Length, 12 + (delta.Length / 2) * (4 + 38.4) + (delta.Length % 2) * (4 + MoveByteCycles), null));
        }
        {
            var code = Start(); Count(code); code.AddRange([0xf3, 0xa4]);
            options.Add(new(code.ToArray(), delta.Length, 12 + (delta.Length <= 255 ? 8 : 12) + 4 + (4 + MoveByteCycles) + delta.Length * MoveByteCycles, null));
        }
        // Keep the first candidate on ties, as XDC's size-first code generator does.
        return options.MinBy(e => e.Size)!;
    }
}
