namespace XdcConverter;

// Staging is in the destination directory. Replacements and rollback use rename /
// ReplaceFile operations, so the old video is not copied to a second large file.
public sealed class OutputTransaction : IDisposable
{
    private sealed record Stamp(long Length, DateTime Written);
    private sealed record Published(string Destination, string? Backup);
    private readonly Dictionary<string, Stamp?> originals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> staged = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Published> published = [];
    private bool committed;
    public IReadOnlyList<string> Existing => originals.Where(x => x.Value != null).Select(x => x.Key).ToArray();

    public OutputTransaction(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full)) throw new IOException(L.T("Путь результата занят папкой: ", "Output path is a directory: ") + full);
            originals.Add(full, ReadStamp(full));
        }
    }
    private static Stamp? ReadStamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new(info.Length, info.LastWriteTimeUtc) : null;
    }
    public string Stage(string path)
    {
        string full = Path.GetFullPath(path);
        if (committed || !originals.ContainsKey(full) || staged.ContainsKey(full)) throw new InvalidOperationException("Invalid staged destination.");
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".partial";
        staged.Add(full, temporary); return temporary;
    }
    public void Commit()
    {
        if (committed || staged.Count != originals.Count) throw new InvalidOperationException("Incomplete output transaction.");
        // A new/changed file during conversion has not been approved for replacement.
        foreach (var pair in originals)
            if (ReadStamp(pair.Key) != pair.Value || Directory.Exists(pair.Key))
                throw new IOException(L.T("Файл изменился во время конвертации; повторите операцию: ", "A file changed during conversion; try again: ") + pair.Key);
        try
        {
            foreach (var pair in staged)
            {
                string? backup = originals[pair.Key] == null ? null : pair.Key + "." + Guid.NewGuid().ToString("N") + ".rollback";
                if (backup == null) File.Move(pair.Value, pair.Key);
                else File.Replace(pair.Value, pair.Key, backup);
                published.Add(new(pair.Key, backup));
            }
            committed = true;
        }
        catch (Exception error)
        {
            var recoveryErrors = new List<Exception>();
            foreach (var item in published.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.Backup == null) File.Delete(item.Destination);
                    else if (File.Exists(item.Destination)) File.Replace(item.Backup, item.Destination, null);
                    else File.Move(item.Backup, item.Destination);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { recoveryErrors.Add(ex); }
            }
            if (recoveryErrors.Count > 0)
                throw new IOException(L.T("Не удалось полностью восстановить предыдущий результат. Резервные файлы .rollback сохранены рядом с ним.", "The previous result could not be fully restored. Recovery files (.rollback) were kept beside it."), new AggregateException([error, .. recoveryErrors]));
            throw;
        }
        // Failure to remove an obsolete backup must not invalidate the published video.
        foreach (var item in published) if (item.Backup != null) TryDelete(item.Backup);
    }
    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    public void Dispose() { foreach (var path in staged.Values) TryDelete(path); }
}
