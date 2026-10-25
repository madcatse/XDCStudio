using System.Text.Json;

namespace XdcConverter;

public static class L
{
    public static bool English { get; private set; }
    public static event Action? Changed;
    public static string T(string ru, string en) => English ? en : ru;
    public static void Set(bool english, bool save = true)
    {
        English = english;
        Changed?.Invoke();
        if (!save) return;
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-settings.json"), JsonSerializer.Serialize(new { Language = english ? "en" : "ru" })); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public static void Load()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ui-settings.json")));
            English = doc.RootElement.GetProperty("Language").GetString() == "en";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException) { }
    }
    public static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss\.ff");
}
