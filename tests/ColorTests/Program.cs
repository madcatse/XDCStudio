using System.Diagnostics;
using System.Text.Json;
using XdcConverter;

string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
int assertions = 0;
void Check(bool condition, string text) { if (!condition) throw new Exception(text); assertions++; }
byte[] Solid(Job job, byte value) => Enumerable.Repeat(value, job.Width * job.Height * 3).ToArray();
double Brightness(byte[] pixels, int mode)
{
    if (mode == 2) return pixels.Average(v => (double)v);
    var palette = mode == 1 ? FrameWriter.Composite : FrameWriter.Tandy;
    return pixels.Average(v => { var c = ColorMixer.FromColor(palette[v]); return .2126 * c.R + .7152 * c.G + .0722 * c.B; });
}
foreach (int mode in new[] { 1, 2, 3 })
{
    var j = new Job { Mode = mode, CompositeAlgorithm = "legacy" };
    var rgb = Solid(j, 96);
    var normal = new FrameWriter(j).Quantize(rgb);
    j.Gamma = 1.8; var bright = new FrameWriter(j).Quantize(rgb);
    j.Gamma = .6; var dark = new FrameWriter(j).Quantize(rgb);
    Check(Brightness(dark, mode) < Brightness(normal, mode) && Brightness(normal, mode) < Brightness(bright, mode), $"Gamma direction, mode {mode}");
    j.Gamma = 3;
    Check(Brightness(new FrameWriter(j).Quantize(Solid(j, 4)), mode) > Brightness(new FrameWriter(j).Quantize(Solid(j, 0)), mode), $"Lift deep shadows before reducing precision, mode {mode}");
    j.Gamma = 1; j.DitherStrength = 0; var zero = new FrameWriter(j).Quantize(rgb);
    j.DitherStrength = 100; j.Dither = "none"; var none = new FrameWriter(j).Quantize(rgb);
    Check(zero.SequenceEqual(none) && none.Distinct().Count() == 1, $"Zero strength = disabled, mode {mode}");
    j.Dither = "bayer"; j.DitherStrength = 40; var partial = new FrameWriter(j).Quantize(rgb);
    Check(!partial.SequenceEqual(normal) && !partial.SequenceEqual(zero), $"Intermediate strength, mode {mode}");
    var writer = new FrameWriter(j);
    Check(writer.Quantize(rgb).SequenceEqual(writer.Quantize(rgb)), $"Stable pattern, mode {mode}");
    Check(new FrameWriter(j).Quantize(Solid(j, 0)).All(v => v == 0), $"Black endpoint, mode {mode}");
    Check(new FrameWriter(j).Quantize(Solid(j, 255)).All(v => v == (mode == 2 ? 1 : 15)), $"White endpoint, mode {mode}");
    if (mode == 2)
    {
        j.Saturation = 0; var mono = new FrameWriter(j).Quantize(rgb); j.Saturation = 2;
        Check(mono.SequenceEqual(new FrameWriter(j).Quantize(rgb)), "Saturation disabled in monochrome");
    }
}
foreach (var job in new[] { new Job { Gamma = double.NaN }, new Job { Gamma = 0 }, new Job { Gamma = 3.01 }, new Job { Saturation = double.PositiveInfinity }, new Job { Saturation = -1 }, new Job { DitherStrength = 101 } })
{
    bool rejected = false; try { job.ValidateColor(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid settings rejected");
}
// Oklab reference: sRGB white is (1,0,0), neutral linear gray has zero chroma.
var white = ColorMixer.Lab(new(1, 1, 1)); var gray = ColorMixer.Lab(new(.125, .125, .125));
Check(Math.Abs(white.R - 1) < .000001 && Math.Abs(white.G) < .000001 && Math.Abs(gray.R - .5) < .000001, "Oklab reference values");

// Compare average displayed colour in 8x8 patches, in linear light. A per-pixel
// error would incorrectly penalize the very spatial mixing under test.
double PatchError(byte[] rgb, byte[] pixels, int[] palette)
{
    double sum = 0; int patches = 0;
    for (int y = 0; y < 200; y += 8) for (int x = 0; x < 160; x += 8)
    {
        var target = new ColorMixer.Rgb(); var actual = new ColorMixer.Rgb();
        for (int yy = y; yy < y + 8; yy++) for (int xx = x; xx < x + 8; xx++)
        {
            int i = yy * 160 + xx, p = i * 3;
            target += ColorMixer.FromColor((rgb[p] << 16) | (rgb[p + 1] << 8) | rgb[p + 2]);
            actual += ColorMixer.FromColor(palette[pixels[i]]);
        }
        sum += ColorMixer.Distance(ColorMixer.Lab(target * (1 / 64.0)), ColorMixer.Lab(actual * (1 / 64.0))); patches++;
    }
    return sum / patches;
}
byte[] Legacy(byte[] rgb, int[] palette)
{
    int[] bayer = [0,48,12,60,3,51,15,63,32,16,44,28,35,19,47,31,8,56,4,52,11,59,7,55,40,24,36,20,43,27,39,23,2,50,14,62,1,49,13,61,34,18,46,30,33,17,45,29,10,58,6,54,9,57,5,53,42,26,38,22,41,25,37,21];
    var result = new byte[32000];
    for (int i = 0; i < result.Length; i++)
    {
        int offset = bayer[((i / 160) & 7) * 8 + ((i % 160) & 7)] - 32;
        int r = (Math.Clamp(rgb[i * 3] + offset, 0, 255) >> 3) * 255 / 31;
        int g = (Math.Clamp(rgb[i * 3 + 1] + offset, 0, 255) >> 3) * 255 / 31;
        int b = (Math.Clamp(rgb[i * 3 + 2] + offset, 0, 255) >> 3) * 255 / 31;
        int best = int.MaxValue;
        for (byte c = 0; c < 16; c++)
        {
            int dr = r - (palette[c] >> 16), dg = g - ((palette[c] >> 8) & 255), db = b - (palette[c] & 255);
            int distance = 2 * dr * dr + 4 * dg * dg + db * db;
            if (distance < best) { best = distance; result[i] = c; }
        }
    }
    return result;
}
var measurements = new List<object>();
foreach (int mode in new[] { 1, 3 })
{
    var j = new Job { Mode = mode, CompositeAlgorithm = "legacy" }; var writer = new FrameWriter(j);
    var palette = mode == 1 ? FrameWriter.Composite : FrameWriter.Tandy;
    double oldTotal = 0, newTotal = 0;
    foreach (int seconds in new[] { 30, 50, 80, 110, 150, 180 })
    {
        var start = new ProcessStartInfo(Path.Combine(root, ".tools/ffmpeg/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-v", "error", "-ss", seconds.ToString(), "-i", Path.Combine(root, "local/media/MIR.webm"), "-vf", "scale=160:200:flags=lanczos,setsar=1,fps=30", "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(arg);
        using var ffmpeg = Process.Start(start)!; var errors = ffmpeg.StandardError.ReadToEndAsync();
        var rgb = new byte[32000 * 3]; ffmpeg.StandardOutput.BaseStream.ReadExactly(rgb); ffmpeg.WaitForExit();
        Check(ffmpeg.ExitCode == 0, errors.Result);
        var oldPixels = Legacy(rgb, palette); var newPixels = writer.Quantize(rgb);
        double oldError = PatchError(rgb, oldPixels, palette), newError = PatchError(rgb, newPixels, palette);
        oldTotal += oldError; newTotal += newError;
        measurements.Add(new { Mode = mode, Seconds = seconds, LegacyPatchError = oldError, NewPatchError = newError });
        if (seconds == 50)
        {
            using var previous = writer.Preview(oldPixels); previous.Save(Path.Combine(output, $"mode{mode}-legacy.png"));
            using var current = writer.Preview(newPixels); current.Save(Path.Combine(output, $"mode{mode}-new.png"));
            j.Gamma = 1.15; j.Saturation = 1.2; j.DitherStrength = 65;
            using var adjusted = new FrameWriter(j).Preview(new FrameWriter(j).Quantize(rgb)); adjusted.Save(Path.Combine(output, $"mode{mode}-adjusted.png"));
            j.Gamma = 1; j.Saturation = 1; j.DitherStrength = 100;
        }
    }
    Check(newTotal < oldTotal, $"MIR patch colour fidelity regressed, mode {mode}: {newTotal} vs {oldTotal}");
}
File.WriteAllText(Path.Combine(output, "color-tests.json"), JsonSerializer.Serialize(new { Success = true, Assertions = assertions, Measurements = measurements }, Data.Json));
Console.WriteLine($"PASS: {assertions} colour checks; MIR colour measurements saved.");
