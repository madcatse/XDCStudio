using System.Globalization;
using System.Text.Json;

namespace XdcConverter;

public sealed record AudioTrack(int Index, string Description);
public sealed record MediaInfo(double Duration, int VideoIndex, int Width, int Height, double Fps, List<AudioTrack> Audio);
public sealed record ConversionProgress(int Percent, string Message);
public sealed record XdvInfo(string File, int Mode, int Packets, int SampleRate, int AudioChunk, double Fps,
    double Duration, long Bytes, int LargestPacket, double PeakKiBPerSecond);
public sealed record ConversionResult(List<XdvInfo> Files, string OutputDirectory, string LogFile);

public sealed class Job
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public int Mode { get; set; } = 1;
    public bool HalfHeight { get; set; }
    public double Fps { get; set; } = 30;
    public int SampleRate { get; set; } = 22050;
    public int DiskRate { get; set; } = 150;
    public int Integrity { get; set; } = 75;
    public string Dither { get; set; } = "bayer";
    public double Gamma { get; set; } = 1;
    public double Saturation { get; set; } = 1;
    public int DitherStrength { get; set; } = 100;
    public string CompositeAlgorithm { get; set; } = "ntsc";
    public bool CompositeNewCga { get; set; }
    public int TemporalStability { get; set; } = 50;
    public string Fit { get; set; } = "fit";
    public bool Deinterlace { get; set; }
    public bool Silent { get; set; }
    public int? AudioStream { get; set; }
    public double Start { get; set; }
    public double Duration { get; set; }
    // Command-line opt-in. The GUI confirms each collision through its callback.
    public bool Overwrite { get; set; }
    // Stay below the original DOS player's 16-bit packet index limit.
    public int SegmentFrames { get; set; } = 60000;

    public int Width => Mode == 2 ? 640 : 160;
    public int Height => Mode == 1 && HalfHeight ? 100 : 200;
    public void Validate()
    {
        Input = Path.GetFullPath(Input);
        Output = Path.GetFullPath(Output);
        if (!File.Exists(Input)) throw new IOException(L.T("Исходный файл не найден.", "Source file not found."));
        if (Mode is < 1 or > 3) throw new ArgumentException(L.T("Неизвестный профиль.", "Unknown profile."));
        if (!double.IsFinite(Fps) || Fps < 10 || Fps > 60) throw new ArgumentException(L.T("FPS должен быть от 10 до 60.", "FPS must be between 10 and 60."));
        if (SampleRate < 8000 || SampleRate > 32000) throw new ArgumentException(L.T("Частота звука: 8000–32000 Гц.", "Audio rate must be between 8000 and 32000 Hz."));
        if (DiskRate < 30 || DiskRate > 500) throw new ArgumentException(L.T("Поток: 30–500 КиБ/с.", "Data rate must be between 30 and 500 KiB/s."));
        if (Integrity < 0 || Integrity > 100) throw new ArgumentException(L.T("Целостность кадра: 0–100.", "Frame integrity must be between 0 and 100."));
        ValidateColor();
        if (Dither is not ("bayer" or "bayer4" or "blue" or "none") || Fit is not ("fit" or "crop" or "stretch")) throw new ArgumentException(L.T("Неизвестный способ обработки.", "Unknown processing method."));
        if (!double.IsFinite(Start) || !double.IsFinite(Duration) || Start < 0 || Duration < 0) throw new ArgumentException(L.T("Некорректный диапазон времени.", "Invalid time range."));
        if (SegmentFrames is < 1 or > 60000) throw new ArgumentException(L.T("Часть должна содержать 1–60000 кадров.", "A part must contain 1–60000 frames."));
        var stem = Path.GetFileNameWithoutExtension(Output).ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(stem, "^[A-Z0-9_]{1,8}$") ||
            System.Text.RegularExpressions.Regex.IsMatch(stem, "^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$"))
            throw new ArgumentException(L.T("Имя XDV: 1–8 латинских букв, цифр или _, например MOVIE.XDV.", "XDV name: 1–8 letters A–Z, digits or _, e.g. MOVIE.XDV."));
        if (!Path.GetExtension(Output).Equals(".xdv", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.T("Выходной файл должен иметь расширение .XDV.", "The output file must have the .XDV extension."));
        if (Input.Equals(Output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(L.T("Выходной файл совпадает с исходным.", "The output file is the same as the input."));
    }
    public void ValidateColor()
    {
        if (TemporalStability < 0 || TemporalStability > 100) throw new ArgumentException(L.T("Стабилизация: 0–100%.", "Temporal stability must be between 0 and 100%."));
        _ = DitherMask.Get(Dither);
        if (CompositeAlgorithm is not ("ntsc" or "legacy")) throw new ArgumentException(L.T("Неизвестный алгоритм композитного цвета.", "Unknown composite colour algorithm."));
        if (!double.IsFinite(Gamma) || Gamma < 0.3 || Gamma > 3) throw new ArgumentException(L.T("Гамма: 0,30–3,00.", "Gamma must be between 0.30 and 3.00."));
        if (!double.IsFinite(Saturation) || Saturation < 0 || Saturation > 2) throw new ArgumentException(L.T("Насыщенность: 0–200%.", "Saturation must be between 0 and 200%."));
        if (DitherStrength < 0 || DitherStrength > 100) throw new ArgumentException(L.T("Сила дизеринга: 0–100%.", "Dithering strength must be between 0 and 100%."));
    }
}

public static class Data
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
    public static string Tool(string relative) => Path.Combine(AppContext.BaseDirectory, "tools", relative);
}
