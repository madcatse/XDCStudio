using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace XdcConverter;

public sealed class Converter
{
    public const string Version = "0.7.2";
    public const string Ffmpeg = "ffmpeg/ffmpeg.exe";
    private static string FramingFilter(Job job)
    {
        string size = job.Fit switch
        {
            "fit" => "scale=640:480:force_original_aspect_ratio=decrease:reset_sar=1,pad=640:480:(ow-iw)/2:(oh-ih)/2,",
            "crop" => "scale=640:480:force_original_aspect_ratio=increase:reset_sar=1,crop=640:480,",
            _ => ""
        };
        return (job.Deinterlace ? "bwdif=mode=send_frame," : "") + size;
    }
    private static string VideoFilter(Job job, string fps) => FramingFilter(job) +
        $"scale={job.Width}:{job.Height}:flags=lanczos,setsar=1,fps={fps},tpad=stop_mode=clone:stop_duration=1";

    public static async Task<PreviewFrames> Preview(Job job, double time, CancellationToken ct, int videoIndex = 0)
    {
        job.ValidateColor();
        // One decoded frame feeds both views. Pack the original and low-resolution
        // RGB into a single pipe; no second seek, temporary image, or correction of the original.
        string filter = $"[0:{videoIndex}]" + FramingFilter(job) + $"fps={Data.N(job.Fps)},tpad=stop_mode=clone:stop_duration=1,split=2[base][small];" +
            "[base]scale=640:480:flags=lanczos,setsar=1,format=rgb24[original];" +
            $"[small]scale={job.Width}:{job.Height}:flags=lanczos,setsar=1,format=rgb24,pad=640:480:0:0[packed];[original][packed]vstack[comparison]";
        await using var pipe = new MediaPipe(["-v", "error", "-nostdin", "-ss", Data.N(time), "-i", job.Input, "-filter_complex", filter, "-map", "[comparison]", "-an", "-sn",
            "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"], ct);
        var packed = new byte[640 * 960 * 3];
        await pipe.ReadVideo(packed, ct);
        await pipe.Complete(ct);
        var rgb = new byte[job.Width * job.Height * 3];
        for (int y = 0; y < job.Height; y++) Array.Copy(packed, (480 + y) * 640 * 3, rgb, y * job.Width * 3, job.Width * 3);
        var writer = new FrameWriter(job, ct);
        ct.ThrowIfCancellationRequested();
        var result = writer.Preview(writer.Quantize(rgb));
        try { return new PreviewFrames(FrameImage.FromRgb(packed.AsSpan(0, 640 * 480 * 3), 640, 480), result); }
        catch { result.Dispose(); throw; }
    }

    public async Task<ConversionResult> Run(Job job, IProgress<ConversionProgress>? progress, CancellationToken ct,
        Func<IReadOnlyList<string>, Task<bool>>? confirmOverwrite = null)
    {
        job.Validate();
        foreach (var component in new[] { Ffmpeg, "ffmpeg/ffprobe.exe", "xdc/XDC_PLAY.EXE" })
            if (!File.Exists(Data.Tool(component))) throw new FileNotFoundException(L.T("Не найден компонент portable-сборки: ", "Portable component not found: ") + component);
        var media = await Processes.Probe(job.Input, ct);
        if (job.Start >= media.Duration) throw new ArgumentException(L.T("Начало фрагмента находится за концом видео.", "The start time is beyond the end of the video."));
        if (job.AudioStream.HasValue && !media.Audio.Any(a => a.Index == job.AudioStream)) throw new ArgumentException(L.T("Выбранная звуковая дорожка отсутствует.", "The selected audio track does not exist."));
        int audioChunk = (int)Math.Round(job.SampleRate / job.Fps);
        int sampleRate = (int)Math.Round(audioChunk * job.Fps);
        double fps = (double)sampleRate / audioChunk;
        double duration = job.Duration > 0 ? Math.Min(job.Duration, media.Duration - job.Start) : media.Duration - job.Start;
        long totalFrames = checked((long)Math.Ceiling(duration * fps));
        int segments = checked((int)((totalFrames + job.SegmentFrames - 1) / job.SegmentFrames));
        if (segments > 999) throw new ArgumentException(L.T("Слишком много частей; увеличьте размер части.", "Too many parts; increase the part size."));
        string outputDir = Path.GetDirectoryName(job.Output)!;
        string stem = Path.GetFileNameWithoutExtension(job.Output).ToUpperInvariant();
        var destinations = Enumerable.Range(1, segments).Select(n => Path.Combine(outputDir,
            (segments == 1 ? stem : stem[..Math.Min(stem.Length, 4)] + n.ToString("000")) + ".XDV")).ToList();
        string reportPath = Path.Combine(outputDir, stem + ".report.json"), logPath = Path.Combine(outputDir, stem + ".log.txt");
        string batchPath = Path.Combine(outputDir, stem + ".BAT"), playerPath = Path.Combine(outputDir, "XDC_PLAY.EXE");
        if (File.Exists(playerPath) && !File.ReadAllBytes(playerPath).AsSpan().SequenceEqual(File.ReadAllBytes(Data.Tool("xdc/XDC_PLAY.EXE"))))
            throw new IOException(L.T("В папке уже есть другой XDC_PLAY.EXE. Выберите другую папку.", "A different XDC_PLAY.EXE exists in this folder. Choose another folder."));
        _ = new NativeEncoder(job.Mode, job.Height, fps, job.DiskRate, audioChunk, job.Integrity);
        bool copyPlayer = !File.Exists(playerPath);
        using var output = new OutputTransaction(destinations.Concat([reportPath, logPath, batchPath]).Concat(copyPlayer ? [playerPath] : []));
        if (output.Existing.Count > 0 && !job.Overwrite)
        {
            if (confirmOverwrite == null) throw new IOException(L.T("Файл уже существует: ", "File already exists: ") + output.Existing[0]);
            if (!await confirmOverwrite(output.Existing)) throw new OperationCanceledException(L.T("Перезапись отменена.", "Overwrite cancelled."), ct);
        }
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(outputDir);
        var stopwatch = Stopwatch.StartNew();
        var logs = new List<string> { $"XDC Studio {Version}; native Windows x64; streaming RGB/PCM in RAM.", $"{job.Input}; {Data.N(fps)} fps; {sampleRate} Hz; {totalFrames} frames" };
        var results = new List<XdvInfo>();
        var statistics = new List<EncoderStatistics>();
        var stabilization = new List<StabilizationStatistics>();
        long completed = 0;
        int? audioIndex = job.Silent ? null : job.AudioStream ?? media.Audio.FirstOrDefault()?.Index;
        var quantizer = new FrameWriter(job, ct);
        var rgb = new byte[job.Width * job.Height * 3];
        var audioBuffer = new byte[audioChunk];
        {
            for (int part = 0; part < segments; part++)
            {
                ct.ThrowIfCancellationRequested();
                int frames = (int)Math.Min(job.SegmentFrames, totalFrames - completed);
                double start = job.Start + completed / fps;
                string partial = output.Stage(destinations[part]);
                var encoder = new NativeEncoder(job.Mode, job.Height, fps, job.DiskRate, audioChunk, job.Integrity);
                var stabilizer = job.Mode == 1 && job.CompositeAlgorithm == "ntsc" && job.TemporalStability > 0 ? new NtscStabilizer(job) : null;
                await using var video = new MediaPipe(["-v", "error", "-nostdin", "-ss", Data.N(start), "-i", job.Input, "-map", "0:" + media.VideoIndex, "-an", "-sn",
                    "-vf", VideoFilter(job, Data.N(fps)), "-frames:v", frames.ToString(), "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"], ct);
                await using var audio = audioIndex.HasValue ? new MediaPipe(["-v", "error", "-nostdin", "-ss", Data.N(start), "-i", job.Input,
                    "-map", "0:" + audioIndex.Value, "-vn", "-sn", "-ac", "1", "-ar", sampleRate.ToString(),
                    "-af", "aresample=async=1:first_pts=0,apad", "-t", Data.N(frames / fps), "-c:a", "pcm_u8", "-f", "u8", "pipe:1"], ct) : null;
                using (var muxer = new XdvMuxer(partial, job.Mode, sampleRate, audioChunk, job.DiskRate))
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        await video.ReadVideo(rgb, ct);
                        Array.Fill(audioBuffer, (byte)128);
                        if (audio != null)
                        {
                            int read = await audio.Stream.ReadAtLeastAsync(audioBuffer, audioChunk, throwOnEndOfStream: false, ct);
                            if (read != audioChunk) throw new IOException(L.T("FFmpeg завершил звуковой поток раньше времени.", "FFmpeg ended the audio stream early."));
                        }
                        byte[] pixels = quantizer.Quantize(rgb);
                        stabilizer?.Apply(rgb, pixels, encoder.Reconstructed, ct);
                        byte[] packet = stabilizer == null ? encoder.Encode(pixels) : encoder.Encode(pixels, stabilizer.Benefits, stabilizer.SceneCut);
                        muxer.Write(packet, audioBuffer);
                        completed++;
                        if (frame % 30 == 0 || frame == frames - 1)
                            progress?.Report(new((int)(completed * 98 / totalFrames), L.T($"Часть {part + 1}/{segments} · кадр {completed}/{totalFrames} · {stopwatch.Elapsed:mm\\:ss}", $"Part {part + 1}/{segments} · frame {completed}/{totalFrames} · {stopwatch.Elapsed:mm\\:ss}")));
                    }
                    await video.Complete(ct);
                    if (audio != null) await audio.Complete(ct);
                    muxer.Finish();
                }
                var info = XdvValidator.Read(partial, job.Mode, frames);
                results.Add(info with { File = destinations[part] });
                statistics.Add(encoder.Statistics);
                if (stabilizer != null) stabilization.Add(stabilizer.Statistics);
                logs.Add($"Part {part + 1}: {info.Packets} packets, {info.Bytes} bytes; maximum {encoder.Statistics.MaximumCycles:F1}/{encoder.Statistics.CycleBudget:F1} cycles/frame.");
            }
            ct.ThrowIfCancellationRequested();
            if (copyPlayer) File.Copy(Data.Tool("xdc/XDC_PLAY.EXE"), output.Stage(playerPath));
            File.WriteAllText(output.Stage(batchPath), "@ECHO OFF\r\n" + string.Join("\r\n", destinations.Select(p => "XDC_PLAY.EXE /p " + Path.GetFileName(p) + " %1")) + "\r\nMODE CO80\r\n", Encoding.ASCII);
            var report = new { Version, Engine = "native-x64", IntermediateMediaFiles = 0, Streaming = true, Job = job, Media = media, Fps = fps, SampleRate = sampleRate,
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds, ApplicationPeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, Files = results, Encoder = statistics, Stabilization = stabilization };
            File.WriteAllText(output.Stage(reportPath), JsonSerializer.Serialize(report, Data.Json), Encoding.UTF8);
            logs.Add($"Completed in {stopwatch.Elapsed.TotalSeconds:F3} seconds; no intermediate media files.");
            File.WriteAllLines(output.Stage(logPath), logs, Encoding.UTF8);
            ct.ThrowIfCancellationRequested();
            output.Commit();
            progress?.Report(new(100, L.T("Готово. XDV проверен и сохранён.", "Done. XDV verified and saved.")));
            return new(results, outputDir, logPath);
        }
    }
}
