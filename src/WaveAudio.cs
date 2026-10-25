using System.Runtime.InteropServices;

namespace XdcConverter;

// PCM U8 from XDV, directly to Windows; no WAV file or full-movie audio buffer.
// https://learn.microsoft.com/windows/win32/api/mmeapi/ns-mmeapi-wavehdr
public sealed class WaveAudio : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Format
    {
        public ushort Tag, Channels;
        public uint Rate, BytesPerSecond;
        public ushort Alignment, Bits, Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header
    {
        public IntPtr Data;
        public uint Length, Recorded;
        public UIntPtr User;
        public uint Flags, Loops;
        public IntPtr Next;
        public UIntPtr Reserved;
    }
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    private struct Position { [FieldOffset(0)] public uint Type; [FieldOffset(4)] public uint Value; }
    private sealed record Buffer(IntPtr Header, IntPtr Data);
    private readonly List<Buffer> buffers = [];
    private readonly uint rate;
    private IntPtr device;
    private int volume = 100;
    private bool softwareVolume;
    public int Volume
    {
        get => volume;
        set
        {
            int level = Math.Clamp(value, 0, 100);
            uint channel = (uint)Math.Round(level * 65535.0 / 100);
            // An open handle targets this playback instance, never the system device ID.
            uint result = waveOutSetVolume(device, channel | channel << 16);
            softwareVolume = result == 8; // Device does not support volume; scale queued PCM instead.
            if (!softwareVolume) Check(result);
            volume = level;
        }
    }
    public int EffectiveVolume
    {
        get
        {
            if (softwareVolume) return volume;
            Check(waveOutGetVolume(device, out uint value));
            return (int)Math.Round((value & 65535) * 100.0 / 65535);
        }
    }
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<Header>();
    public int Pending { get { Reap(); return buffers.Count; } }
    public long SamplesPlayed
    {
        get
        {
            var position = new Position { Type = 2 };
            Check(waveOutGetPosition(device, ref position, 12));
            return position.Type switch { 2 or 4 => position.Value, 1 => (long)position.Value * rate / 1000, _ => throw new IOException("Unsupported audio clock.") };
        }
    }
    public WaveAudio(int sampleRate)
    {
        rate = (uint)sampleRate;
        var format = new Format { Tag = 1, Channels = 1, Rate = rate, BytesPerSecond = rate, Alignment = 1, Bits = 8 };
        Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, UIntPtr.Zero, 0));
    }
    public void Enqueue(byte[] pcm)
    {
        ObjectDisposedException.ThrowIf(device == IntPtr.Zero, this);
        Reap();
        var data = Marshal.AllocHGlobal(pcm.Length);
        var header = Marshal.AllocHGlobal((int)HeaderSize);
        bool prepared = false;
        try
        {
            if (softwareVolume && volume != 100)
            {
                var scaled = new byte[pcm.Length];
                for (int i = 0; i < pcm.Length; i++) scaled[i] = (byte)(128 + (pcm[i] - 128) * volume / 100);
                Marshal.Copy(scaled, 0, data, scaled.Length);
            }
            else Marshal.Copy(pcm, 0, data, pcm.Length);
            Marshal.StructureToPtr(new Header { Data = data, Length = (uint)pcm.Length }, header, false);
            Check(waveOutPrepareHeader(device, header, HeaderSize)); prepared = true;
            Check(waveOutWrite(device, header, HeaderSize));
            buffers.Add(new(header, data));
        }
        catch
        {
            if (prepared) waveOutUnprepareHeader(device, header, HeaderSize);
            Marshal.FreeHGlobal(header); Marshal.FreeHGlobal(data); throw;
        }
    }
    private void Reap()
    {
        for (int i = buffers.Count - 1; i >= 0; i--)
        {
            var buffer = buffers[i];
            if ((Marshal.PtrToStructure<Header>(buffer.Header).Flags & 1) == 0) continue;
            Check(waveOutUnprepareHeader(device, buffer.Header, HeaderSize));
            Marshal.FreeHGlobal(buffer.Header); Marshal.FreeHGlobal(buffer.Data); buffers.RemoveAt(i);
        }
    }
    public void Dispose()
    {
        if (device == IntPtr.Zero) return;
        // Reset returns all queued headers before memory is released.
        Check(waveOutReset(device)); Reap();
        Check(waveOutClose(device)); device = IntPtr.Zero;
    }
    private static void Check(uint result) { if (result != 0) throw new IOException(L.T("Не удалось открыть или воспроизвести звук", "Could not open or play audio") + $" (Windows audio: {result})."); }
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr device, uint id, ref Format format, IntPtr callback, UIntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutGetPosition(IntPtr device, ref Position position, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr device);
    [DllImport("winmm.dll")] private static extern uint waveOutSetVolume(IntPtr device, uint value);
    [DllImport("winmm.dll")] private static extern uint waveOutGetVolume(IntPtr device, out uint value);
}
