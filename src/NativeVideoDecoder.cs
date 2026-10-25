using System.Buffers.Binary;

namespace XdcConverter;

// Bounded interpreter for ONLY the video-store instructions emitted by XDC.
// It cannot access host memory or execute arbitrary native machine code.
public static class NativeVideoDecoder
{
    public static int Apply(ReadOnlySpan<byte> video, Span<byte> vram, bool allowPadding = false)
    {
        ReadOnlySpan<byte> start = [0x1e, 0x0e, 0x1f, 0xbe];
        ReadOnlySpan<byte> setup = [0xb8, 0, 0xb8, 0x8e, 0xc0, 0xb5, 0, 0xfc];
        if (vram.Length != 16384 || video.Length < 16 || !video[..4].SequenceEqual(start) || !video.Slice(6, 8).SequenceEqual(setup))
            throw new InvalidDataException("Invalid 8088 frame prologue.");
        int dataStart = BinaryPrimitives.ReadUInt16LittleEndian(video[4..]);
        if (dataStart < 16 || dataStart > video.Length || video[dataStart - 2] != 0x1f || video[dataStart - 1] != 0xcb)
            throw new InvalidDataException("Invalid 8088 frame epilogue.");
        int ip = 14, si = dataStart, di = 0, cx = 0, ax = 0xb800;
        while (ip < dataStart - 2)
        {
            byte op = video[ip++];
            int repeat = 1;
            if (op == 0xf3)
            {
                if (ip >= dataStart - 2) throw new InvalidDataException("Truncated REP.");
                op = video[ip++]; repeat = cx; cx = 0;
                if (op is not (0xa4 or 0xa5 or 0xaa or 0xab)) throw new InvalidDataException("Unsupported REP opcode.");
            }
            if (op is 0xbf or 0xb9 or 0xb8)
            {
                if (ip + 2 > dataStart - 2) throw new InvalidDataException("Truncated word operand.");
                int value = BinaryPrimitives.ReadUInt16LittleEndian(video[ip..]); ip += 2;
                if (op == 0xbf) di = value; else if (op == 0xb9) cx = value; else ax = value;
            }
            else if (op is 0xb0 or 0xb1 or 0xb5)
            {
                if (ip >= dataStart - 2) throw new InvalidDataException("Truncated byte operand.");
                int value = video[ip++];
                if (op == 0xb0) ax = (ax & 0xff00) | value;
                else if (op == 0xb1) cx = (cx & 0xff00) | value;
                else cx = (cx & 255) | value << 8;
            }
            else if (op is 0xa4 or 0xa5 or 0xaa or 0xab)
            {
                int width = op is 0xa5 or 0xab ? 2 : 1, count = repeat * width;
                if (di + count > vram.Length) throw new InvalidDataException("Video write outside CGA RAM.");
                if (op is 0xa4 or 0xa5)
                {
                    if (si + count > video.Length) throw new InvalidDataException("Literal data out of bounds.");
                    video.Slice(si, count).CopyTo(vram[di..]); si += count;
                }
                else if (width == 1) vram.Slice(di, count).Fill((byte)ax);
                else for (int i = 0; i < count; i += 2) BinaryPrimitives.WriteUInt16LittleEndian(vram[(di + i)..], (ushort)ax);
                di += count;
            }
            else if (op == 0x26)
            {
                if (ip + 5 > dataStart - 2 || video[ip] is not (0xc6 or 0xc7) || video[ip + 1] != 6)
                    throw new InvalidDataException("Invalid direct video store.");
                int width = video[ip] == 0xc7 ? 2 : 1;
                int address = BinaryPrimitives.ReadUInt16LittleEndian(video[(ip + 2)..]);
                ip += 4;
                if (ip + width > dataStart - 2 || address + width > vram.Length) throw new InvalidDataException("Direct store out of bounds.");
                video.Slice(ip, width).CopyTo(vram[address..]); ip += width;
            }
            else throw new InvalidDataException($"Unsupported opcode {op:X2}.");
        }
        if (ip != dataStart - 2 || (!allowPadding && si != video.Length)) throw new InvalidDataException("Code or literal size mismatch.");
        return si;
    }
}
