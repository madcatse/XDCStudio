namespace XdcConverter;

internal static class AppBrand
{
    public static Icon Icon { get; } = Load();
    private static Icon Load()
    {
        using var stream = typeof(AppBrand).Assembly.GetManifestResourceStream("XdcStudio.Icon")!;
        using var icon = new Icon(stream); return (Icon)icon.Clone();
    }
}
