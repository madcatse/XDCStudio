namespace XdcConverter;

// Replays deltas when seeking; an XDV packet is not an independent keyframe.
// At most 65 RAM checkpoints (~1 MiB), regardless of movie duration.
public sealed class XdvReader : IDisposable
{
    private readonly object gate = new();
    private readonly FileStream stream;
    private readonly long[] positions;
    private readonly int[] sizes;
    private readonly SortedDictionary<int, byte[]> checkpoints = [];
    private readonly int interval;
    private byte[] ram = new byte[16384];
    private int current = -1;
    private bool disposed;
    public XdvInfo Info { get; }
    public int CheckpointCount { get { lock (gate) return checkpoints.Count; } }

    public XdvReader(string path)
    {
        Info = XdvValidator.Read(path);
        stream = File.OpenRead(path);
        try
        {
            positions = new long[Info.Packets]; sizes = new int[Info.Packets];
            stream.Position = stream.Length - Info.Packets;
            byte[] index = new byte[Info.Packets]; stream.ReadExactly(index);
            long offset = 512;
            for (int i = 0; i < index.Length; i++) { positions[i] = offset; sizes[i] = index[i] * 512; offset += sizes[i]; }
            interval = Math.Max(1, (Info.Packets + 63) / 64);
        }
        catch { stream.Dispose(); throw; }
    }

    public byte[] ReadFrame(int frame, CancellationToken ct = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (frame < 0 || frame >= Info.Packets) throw new ArgumentOutOfRangeException(nameof(frame));
            int checkpoint = -1;
            foreach (int key in checkpoints.Keys) { if (key > frame) break; checkpoint = key; }
            if (current > frame || checkpoint > current)
            {
                ram = checkpoint < 0 ? new byte[16384] : (byte[])checkpoints[checkpoint].Clone();
                current = checkpoint;
            }
            byte[] packet = new byte[Info.LargestPacket];
            while (current < frame)
            {
                ct.ThrowIfCancellationRequested();
                int next = current + 1;
                stream.Position = positions[next]; stream.ReadExactly(packet.AsSpan(0, sizes[next]));
                // Decode into a fresh buffer: malformed input must not poison the seek state.
                byte[] nextRam = (byte[])ram.Clone();
                NativeVideoDecoder.Apply(packet.AsSpan(0, sizes[next] - Info.AudioChunk), nextRam, allowPadding: true);
                ram = nextRam; current = next;
                if (current % interval == 0) checkpoints.TryAdd(current, (byte[])ram.Clone());
            }
            ct.ThrowIfCancellationRequested();
            return (byte[])ram.Clone();
        }
    }

    public byte[] ReadAudio(int frame)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (frame < 0 || frame >= Info.Packets) throw new ArgumentOutOfRangeException(nameof(frame));
            var audio = new byte[Info.AudioChunk];
            stream.Position = positions[frame] + sizes[frame] - audio.Length;
            stream.ReadExactly(audio); return audio;
        }
    }
    public void Dispose() { lock (gate) { disposed = true; stream.Dispose(); checkpoints.Clear(); } }
}
