namespace XdcConverter;

public static class XdvValidator
{
    public static XdvInfo Read(string path, int? expectedMode = null, int? expectedPackets = null)
    {
        using var file = File.OpenRead(path);
        using var r = new BinaryReader(file);
        if (file.Length < 513 || new string(r.ReadChars(4)) != "XDCV") throw new IOException(L.T("Некорректная сигнатура XDV.", "Invalid XDV signature."));
        int packets = r.ReadUInt16(), largest = r.ReadUInt16(), audio = r.ReadUInt16(), rate = r.ReadUInt16();
        int mode = r.ReadByte(), columns = r.ReadByte(), rows = r.ReadByte(), features = r.ReadByte();
        // The requested rate may round slightly below 8000 when aligned to FPS.
        if (packets == 0 || audio == 0 || rate == 0 || mode is < 1 or > 3 || columns != 80 || rows != 200 || features != 0)
            throw new IOException(L.T("Некорректные параметры заголовка XDV.", "Invalid XDV header."));
        if (expectedMode.HasValue && mode != expectedMode) throw new IOException(L.T("XDC записал другой видеорежим.", "XDC wrote a different video mode."));
        if (expectedPackets.HasValue && packets != expectedPackets) throw new IOException($"XDC записал {packets} из {expectedPackets} кадров. Файл не будет выдан как готовый.");
        file.Position = file.Length - packets;
        byte[] index = r.ReadBytes(packets);
        long size = 512;
        int maximum = 0;
        foreach (byte sectors in index)
        {
            int bytes = sectors * 512;
            if (bytes < audio + 16 || bytes > 20 * 1024) throw new IOException(L.T("Некорректный размер пакета XDV.", "Invalid XDV packet size."));
            maximum = Math.Max(maximum, bytes); size += bytes;
        }
        if (size + packets != file.Length || maximum != largest) throw new IOException(L.T("Размер XDV не согласуется с индексом пакетов.", "XDV size does not match the packet index."));
        // Every video chunk starts with the compiler's 8088 prologue and has a RETF before its data.
        long position = 512;
        foreach (byte sectors in index)
        {
            file.Position = position;
            byte[] prologue = r.ReadBytes(14);
            int dataOffset = prologue[4] | prologue[5] << 8;
            if (prologue[0] != 0x1e || prologue[1] != 0x0e || prologue[2] != 0x1f || prologue[3] != 0xbe ||
                dataOffset < 16 || dataOffset > sectors * 512 - audio) throw new IOException(L.T("Повреждён видеопакет XDV.", "Corrupt XDV video packet."));
            file.Position = position + dataOffset - 2;
            if (r.ReadByte() != 0x1f || r.ReadByte() != 0xcb) throw new IOException(L.T("У видеопакета нет корректного завершения.", "Invalid video packet ending."));
            position += sectors * 512;
        }
        double fps = (double)rate / audio;
        return new(Path.GetFileName(path), mode, packets, rate, audio, fps, packets / fps, file.Length, largest, maximum * fps / 1024);
    }
}
