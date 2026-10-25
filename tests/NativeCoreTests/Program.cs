using XdcConverter;

// Independent pixel-to-VRAM oracle. Exercise convergence, not a copy of the optimizer.
static byte[] Expected(byte[] pixels, int mode, int height)
{
    var memory = new byte[16384];
    int width = mode == 2 ? 640 : 160;
    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
    {
        int line = height == 100 ? y * 2 + 1 : y;
        int cell = (line % 2 == 1 ? 8192 : 0) + line / 2 * 80;
        if (mode == 2) memory[cell + x / 8] |= (byte)(pixels[y * width + x] * (128 >> (x % 8)));
        else memory[cell + x / 2] |= (byte)(pixels[y * width + x] * (x % 2 == 0 ? 16 : 1));
    }
    return memory;
}

int checks = 0, frames = 0;
var random = new Random(8088);
foreach (var (mode, height) in new[] { (1, 200), (1, 100), (2, 200), (3, 200) })
foreach (var (fps, disk, audio) in new[] { (30.0, 150, 735), (60.0, 60, 367), (15.0, 500, 1470), (10.0, 30, 800) })
{
    var encoder = new NativeEncoder(mode, height, fps, disk, audio, 100);
    var pixels = new byte[(mode == 2 ? 640 : 160) * height];
    int colors = mode == 2 ? 2 : 16;
    foreach (string pattern in new[] { "black", "white", "last-pixel", "two-bytes", "noise", "black", "stripes" })
    {
        Array.Clear(pixels);
        if (pattern == "white") Array.Fill(pixels, (byte)(colors - 1));
        if (pattern == "last-pixel") pixels[^1] = (byte)(colors - 1);
        if (pattern == "two-bytes") { pixels[^1] = 1; pixels[^(mode == 2 ? 9 : 3)] = 1; }
        if (pattern == "noise") for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)random.Next(colors);
        if (pattern == "stripes") for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)((i / 32) % colors);
        byte[] expected = Expected(pixels, mode, height);
        bool converged = false;
        for (int i = 0; i < 1000; i++)
        {
            byte[] video = encoder.Encode(pixels); frames++;
            if (video.Length > encoder.Statistics.VideoByteBudget || encoder.Statistics.MaximumCycles > encoder.Statistics.CycleBudget + 0.001)
                throw new Exception("Exceeded 8088/disk budget");
            if (encoder.Reconstructed.SequenceEqual(expected)) { converged = true; break; }
        }
        if (!converged) throw new Exception($"Did not converge: mode={mode}, height={height}, fps={fps}, pattern={pattern}");
        checks++;
    }
}
// A deliberately malformed stream must fail rather than read beyond the input.
try { NativeVideoDecoder.Apply([0x1e, 0x0e, 0x1f], new byte[16384]); throw new Exception("Accepted malformed stream"); }
catch (InvalidDataException) { checks++; }
Console.WriteLine($"PASS: {checks} cases, {frames} encoded frames; every pattern converged exactly, within both budgets.");
