using System.Text.Json;

namespace XdcConverter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--convert")
        {
            string result = Path.GetFullPath(args[1]) + ".result.json";
            try
            {
                using var cancellation = new CancellationTokenSource();
                if (args.Length == 4 && args[2] == "--cancel-after-ms") cancellation.CancelAfter(int.Parse(args[3]));
                var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(args[1]), Data.Json) ?? throw new ArgumentException("Пустое задание.");
                var output = new Converter().Run(job, new Progress<ConversionProgress>(p => Console.WriteLine($"{p.Percent}% {p.Message}")), cancellation.Token).GetAwaiter().GetResult();
                File.WriteAllText(result, JsonSerializer.Serialize(new { Success = true, Result = output }, Data.Json));
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(result, JsonSerializer.Serialize(new { Success = false, Error = ex.ToString() }, Data.Json));
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--inspect")
        {
            try { File.WriteAllText(args[2], JsonSerializer.Serialize(XdvValidator.Read(args[1]), Data.Json)); return 0; }
            catch (Exception ex) { File.WriteAllText(args[2], ex.ToString()); return 1; }
        }
        ApplicationConfiguration.Initialize();
        L.Load(); MonitorSettings.Load();
        if (args.Length == 2 && args[0] == "--monitor-test") { L.Set(false, false); MonitorDialog.Show(null, args[1]); return 0; }
        if (args.Length == 3 && args[0] == "--comparison-test") return PreviewDiagnostics.ComparisonTest(args[1], args[2]).GetAwaiter().GetResult();
        if (args.Length == 3 && args[0] == "--reader-test") return PreviewDiagnostics.ReaderTest(args[1], args[2]);
        if (args.Length == 2 && args[0] == "--play") { Application.Run(new PlayerForm([Path.GetFullPath(args[1])])); return 0; }
        if (args.Length == 3 && args[0] == "--play-test") { Application.Run(new PlayerForm([Path.GetFullPath(args[1])], args[2])); return 0; }
        if (args.Length == 3 && args[0] == "--ui-test") Application.Run(new MainForm(args[1], args[2]));
        else Application.Run(new MainForm(args.FirstOrDefault()));
        return 0;
    }
}
