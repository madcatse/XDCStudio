using System.Security.Cryptography;
using System.Text.Json;

namespace XdcConverter;

internal static class PreviewDiagnostics
{
    public static async Task<int> ComparisonTest(string input, string reportPath)
    {
        static string Hash(Bitmap image)
        {
            using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
        try
        {
            var cases = new List<object>();
            foreach (var (mode, half, fit) in new[] { (1, false, "fit"), (1, true, "fit"), (2, false, "crop"), (3, false, "stretch") })
            {
                var job = new Job { Input = input, Mode = mode, HalfHeight = half, Fit = fit, Gamma = 1.35, Saturation = 1.4, DitherStrength = 40 };
                using var preview = await Converter.Preview(job, 50, CancellationToken.None);
                string framing = fit switch
                {
                    "fit" => "scale=640:480:force_original_aspect_ratio=decrease:reset_sar=1,pad=640:480:(ow-iw)/2:(oh-ih)/2,",
                    "crop" => "scale=640:480:force_original_aspect_ratio=increase:reset_sar=1,crop=640:480,",
                    _ => ""
                };
                // Independent, ordinary conversion stream, without the comparison graph.
                await using var pipe = new MediaPipe(["-v", "error", "-nostdin", "-ss", "50", "-i", input, "-an", "-sn", "-vf",
                    framing + $"scale={job.Width}:{job.Height}:flags=lanczos,setsar=1,fps=30,tpad=stop_mode=clone:stop_duration=1",
                    "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1"], CancellationToken.None);
                var rgb = new byte[job.Width * job.Height * 3]; await pipe.ReadVideo(rgb, CancellationToken.None); await pipe.Complete(CancellationToken.None);
                var writer = new FrameWriter(job); using var reference = writer.Preview(writer.Quantize(rgb));
                if (Hash(reference) != Hash(preview.Result)) throw new Exception($"Preview differs from conversion input: mode {mode}, half {half}, fit {fit}.");
                job.Gamma = 1; job.Saturation = 1; job.DitherStrength = 100;
                using var neutral = await Converter.Preview(job, 50, CancellationToken.None);
                if (Hash(preview.Source) != Hash(neutral.Source)) throw new Exception("Adjustments changed the original view.");
                cases.Add(new { Mode = mode, HalfHeight = half, Fit = fit, PreviewMatchesConversion = true, OriginalUnchanged = true });
            }
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = true, Cases = cases }, Data.Json)); return 0;
        }
        catch (Exception ex) { File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json)); return 1; }
    }

    public static int ReaderTest(string path, string reportPath)
    {
        try
        {
            using var reader = new XdvReader(path);
            using var video = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var audio = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var hashes = new string[reader.Info.Packets];
            for (int i = 0; i < hashes.Length; i++)
            {
                var frame = reader.ReadFrame(i); video.AppendData(frame); audio.AppendData(reader.ReadAudio(i));
                hashes[i] = Convert.ToHexString(SHA256.HashData(frame));
            }
            int[] seeks = [hashes.Length - 1, 0, hashes.Length / 2, 1, hashes.Length - 2, hashes.Length / 3, 0];
            foreach (int frame in seeks)
                if (Convert.ToHexString(SHA256.HashData(reader.ReadFrame(frame))) != hashes[frame]) throw new Exception("Random seek differs from sequential playback.");
            if (reader.CheckpointCount > 65) throw new Exception("Checkpoint cache exceeded bound.");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = true, reader.Info, VideoSha256 = Convert.ToHexString(video.GetHashAndReset()),
                AudioSha256 = Convert.ToHexString(audio.GetHashAndReset()), RandomSeeks = seeks.Length, reader.CheckpointCount }, Data.Json));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json)); return 1; }
    }
}
