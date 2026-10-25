using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace XdcConverter;

public static class Processes
{
    public static Process Start(string exe, IEnumerable<string> args, string? cwd = null, bool binary = false, bool visible = false)
    {
        if (!File.Exists(exe)) throw new FileNotFoundException(L.T("Отсутствует компонент дистрибутива. Распакуйте ZIP целиком.", "A component is missing. Extract the entire ZIP archive."), exe);
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = !visible,
            WindowStyle = visible ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = cwd ?? AppContext.BaseDirectory };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info) ?? throw new IOException(L.T("Не удалось запустить ", "Could not start ") + exe);
    }

    public static async Task<string> Capture(string exe, IEnumerable<string> args, CancellationToken ct, string? cwd = null, bool visible = false)
    {
        using var p = Start(exe, args, cwd, visible: visible);
        using var registration = ct.Register(() => Kill(p));
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var output = await stdout;
        var errors = await stderr;
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0) throw new IOException(Path.GetFileName(exe) + ": " + errors[^Math.Min(errors.Length, 5000)..]);
        return output;
    }

    public static void Kill(Process p)
    {
        try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
    }

    public static async Task<MediaInfo> Probe(string input, CancellationToken ct)
    {
        var json = await Capture(Data.Tool("ffmpeg/ffprobe.exe"), ["-v", "error", "-show_streams", "-show_format", "-of", "json", "-i", input], ct);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToList();
        var videos = streams.Where(s => s.GetProperty("codec_type").GetString() == "video" &&
            !(s.TryGetProperty("disposition", out var d) && d.TryGetProperty("attached_pic", out var a) && a.GetInt32() != 0)).ToList();
        if (videos.Count == 0) throw new IOException(L.T("В файле нет видеодорожки.", "The file contains no video track."));
        var video = videos[0];
        double Parse(JsonElement s, string name) => s.TryGetProperty(name, out var el) &&
            double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) ? n : 0;
        double duration = Parse(video, "duration");
        if (duration <= 0 && video.TryGetProperty("tags", out var videoTags) && videoTags.TryGetProperty("DURATION", out var durationTag))
        {
            var components = durationTag.GetString()!.Split(':');
            if (components.Length == 3 && double.TryParse(components[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                duration = int.Parse(components[0]) * 3600 + int.Parse(components[1]) * 60 + seconds;
        }
        if (duration <= 0) duration = Parse(doc.RootElement.GetProperty("format"), "duration");
        if (!double.IsFinite(duration) || duration <= 0) throw new IOException(L.T("Не удалось определить длительность видео.", "Could not determine the video duration."));
        double fps = 0;
        if (video.TryGetProperty("avg_frame_rate", out var rate))
        {
            var r = rate.GetString()!.Split('/');
            if (r.Length == 2 && double.TryParse(r[0], out var n) && double.TryParse(r[1], out var d) && d != 0) fps = n / d;
        }
        var audio = new List<AudioTrack>();
        foreach (var s in streams.Where(s => s.GetProperty("codec_type").GetString() == "audio"))
        {
            string language = "";
            if (s.TryGetProperty("tags", out var tags) && tags.TryGetProperty("language", out var lang)) language = lang.GetString() ?? "";
            int index = s.GetProperty("index").GetInt32();
            audio.Add(new(index, $"#{index} · {s.GetProperty("codec_name").GetString()} · {language}"));
        }
        return new(duration, video.GetProperty("index").GetInt32(), video.GetProperty("width").GetInt32(), video.GetProperty("height").GetInt32(), fps, audio);
    }
}
