using System.Buffers.Binary;

namespace XdcConverter;

public sealed class XdvMuxer : IDisposable
{
    private readonly FileStream file;
    private readonly int mode, rate, audioChunk, packetLimit;
    private readonly List<byte> index = [];
    private int largest;
    private bool finished;

    public XdvMuxer(string path, int mode, int rate, int audioChunk, int diskKiB)
    {
        this.mode = mode; this.rate = rate; this.audioChunk = audioChunk;
        packetLimit = (int)Math.Floor(diskKiB * 1024.0 / ((double)rate / audioChunk) / 512) * 512;
        file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536);
        file.Write(new byte[512]);
    }

    public void Write(ReadOnlySpan<byte> video, ReadOnlySpan<byte> audio)
    {
        if (finished || index.Count >= 60000 || audio.Length != audioChunk) throw new InvalidOperationException("Invalid packet state.");
        int size = (video.Length + audioChunk + 511) & ~511;
        if (size > packetLimit || size > 20 * 1024) throw new InvalidOperationException("Packet exceeds disk budget.");
        byte[] packet = new byte[size];
        video.CopyTo(packet); audio.CopyTo(packet.AsSpan(size - audioChunk));
        file.Write(packet);
        index.Add((byte)(size / 512)); largest = Math.Max(largest, size);
    }

    public void Finish()
    {
        if (finished || index.Count == 0) throw new InvalidOperationException("Empty/already finished XDV.");
        file.Write(index.ToArray());
        Span<byte> header = stackalloc byte[512]; header.Clear();
        "XDCV"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)index.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], (ushort)largest);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], (ushort)audioChunk);
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], (ushort)rate);
        header[12] = (byte)mode; header[13] = 80; header[14] = 200;
        file.Position = 0; file.Write(header); file.Flush(); finished = true;
    }

    public void Dispose() => file.Dispose();
}
