using System.Text.Json;

namespace XdcConverter;

public sealed record MonitorOptions(bool Enabled = true, bool NewCga = false, double Hue = 0, double Saturation = 1)
{
    public void Validate()
    {
        if (!double.IsFinite(Hue) || Hue < -45 || Hue > 45 || !double.IsFinite(Saturation) || Saturation < 0 || Saturation > 2)
            throw new ArgumentException("Invalid NTSC monitor settings.");
    }
}

public static class MonitorSettings
{
    public static MonitorOptions Current { get; private set; } = new();
    public static event Action? Changed;
    private static string FileName => Path.Combine(AppContext.BaseDirectory, "monitor-settings.json");
    public static void Set(MonitorOptions options, bool save = true)
    {
        options.Validate(); if (options == Current) return;
        Current = options; Changed?.Invoke();
        if (!save) return;
        try { File.WriteAllText(FileName, JsonSerializer.Serialize(options, Data.Json)); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public static void Load()
    {
        try
        {
            var options = JsonSerializer.Deserialize<MonitorOptions>(File.ReadAllText(FileName), Data.Json);
            if (options != null) { options.Validate(); Current = options; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
    }
}
