using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using XdcConverter;

string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var normal = new MonitorOptions();
int[] Channels(int pixel) => [(pixel >> 16) & 255, (pixel >> 8) & 255, pixel & 255];
var vram = new byte[16384];
Check(CgaNtsc.Render(vram, normal).All(p => (p & 0xffffff) == 0), "Black signal");
Array.Fill(vram, (byte)255);
Check(Channels(CgaNtsc.Render(vram, normal)[320]).All(c => c >= 254), "White calibration");

// Closed-form, steady-state 4-sample signal, independent of the FIR implementation.
// Palette hue order must match XDC's mode-6 bit order, not rotate by one pixel.
int[] expected = [0x000000,0x00731b,0x281ef0,0x009fff,0x9f0055,0x717170,0xd01dff,0x999eff,
    0x4a5400,0x21d400,0x727172,0x40ff82,0xff5200,0xd6d200,0xff70ef,0xfeffff];
var actual = new List<int>();
for (int pattern = 0; pattern < 16; pattern++)
{
    Array.Fill(vram, (byte)(pattern * 17));
    var pixels = CgaNtsc.Render(vram, normal); int middle = pixels[320]; actual.Add(middle & 0xffffff);
    Check(Channels(middle).Zip(Channels(expected[pattern])).All(p => Math.Abs(p.First - p.Second) <= 3), $"Steady pattern {pattern}: {middle & 0xffffff:X6} vs {expected[pattern]:X6}");
    Check(pixels.AsSpan(0,640).SequenceEqual(pixels.AsSpan(100*640,640)), "No alternating scanline phase");
}
Array.Fill(vram, (byte)0x55);
var neutral = Channels(CgaNtsc.Render(vram, normal)[320]);
Check(neutral.Max() - neutral.Min() < 3, "Two-pixel alternating signal has no 3.58 MHz chroma");
Array.Fill(vram, (byte)0x11);
var green = CgaNtsc.Render(vram, normal);
Check(!green.SequenceEqual(CgaNtsc.Render(vram, normal with { Hue = 30 })), "Hue rotation");
Check(!green.SequenceEqual(CgaNtsc.Render(vram, normal with { NewCga = true })), "Old/new DAC model");
Check(CgaNtsc.Render(vram, normal with { Saturation = 0 }).All(p => { var c = Channels(p); return c[0] == c[1] && c[1] == c[2]; }), "Monochrome monitor setting");

Array.Clear(vram); vram[40] = 0x80;
var impulse = CgaNtsc.Render(vram, normal);
Check(Enumerable.Range(308,12).Any(x => (impulse[x] & 0xffffff) != 0), "Signal affects neighbours outside its nibble");
Check(impulse.AsSpan(640).ToArray().All(p => (p & 0xffffff) == 0), "Horizontal filters cannot leak into other scanlines");
var random = new Random(5150); random.NextBytes(vram); var copy = (byte[])vram.Clone();
var first = CgaNtsc.Render(vram, normal); _ = CgaNtsc.Render(new byte[16384], normal);
Check(first.SequenceEqual(CgaNtsc.Render(vram, normal)) && vram.SequenceEqual(copy), "No history or VRAM mutation");
foreach (int mode in new[] { 2,3 })
{
    using var a = FrameImage.Create(vram,mode,normal); using var b = FrameImage.Create(vram,mode,normal with { Enabled=false });
    Check(Hash(a)==Hash(b), $"Mode {mode} unaffected by NTSC");
}
string Hash(Bitmap image) { using var m = new MemoryStream(); image.Save(m,ImageFormat.Png); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(m.ToArray())); }

// XDV frames are reconstructed first; both renderers receive identical CGA memory.
string mirPath = Path.Combine(root,"local/reference/0.5/MIR_CGA.XDV");
if (!File.Exists(mirPath)) mirPath = Path.Combine(root,"tests/color04/final-integration/MIR_CGA.XDV");
using var reader = new XdvReader(mirPath);
var mir = reader.ReadFrame(1510);
using (var legacy = FrameImage.Create(mir,1,normal with { Enabled=false })) legacy.Save(Path.Combine(output,"mir-palette.png"));
using (var ntsc = FrameImage.Create(mir,1,normal)) ntsc.Save(Path.Combine(output,"mir-ntsc.png"));
using (var newer = FrameImage.Create(mir,1,normal with { NewCga=true })) newer.Save(Path.Combine(output,"mir-ntsc-new.png"));
var elapsed = new List<double>();
for (int i=0;i<120;i++)
{
    var clock=Stopwatch.StartNew(); _=CgaNtsc.Render(mir,normal); clock.Stop(); if(i>=20) elapsed.Add(clock.Elapsed.TotalMilliseconds);
}
Check(elapsed.Average()<33.33,"NTSC renderer cannot sustain 30 FPS");
var report = new { Success=true,Checks=checks,SteadyStateRgb=actual.Select(x=>x.ToString("X6")),MeanRenderMilliseconds=elapsed.Average(),MaximumRenderMilliseconds=elapsed.Max(),HardwareCaptureCompared=false };
File.WriteAllText(Path.Combine(output,"ntsc-tests.json"),JsonSerializer.Serialize(report,Data.Json));
Console.WriteLine(JsonSerializer.Serialize(report,Data.Json));
