using XdcConverter;

string root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
string first = Path.Combine(root, "first.dat"), second = Path.Combine(root, "second.dat");
if (Directory.GetFiles(root).Length != 0) throw new Exception("A fresh test directory is required.");
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Clean() => Assert(Directory.GetFiles(root, "*.partial").Length == 0 && Directory.GetFiles(root, "*.rollback").Length == 0, "Temporary files leaked");

File.WriteAllText(first, "old first"); File.WriteAllText(second, "old second");
using (var tx = new OutputTransaction([first, second]))
{
    File.WriteAllText(tx.Stage(first), "new first"); File.WriteAllText(tx.Stage(second), "new second");
    // Disposing before commit simulates cancellation or an encoder error.
}
Assert(File.ReadAllText(first) == "old first" && File.ReadAllText(second) == "old second", "Uncommitted output changed originals"); Clean();

using (var tx = new OutputTransaction([first, second]))
{
    File.WriteAllText(tx.Stage(first), "new first"); File.WriteAllText(tx.Stage(second), "new second");
    using var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None);
    bool failed = false;
    try { tx.Commit(); } catch (IOException) { failed = true; }
    Assert(failed, "A locked output was overwritten");
}
Assert(File.ReadAllText(first) == "old first" && File.ReadAllText(second) == "old second", "Commit failure did not roll back first file"); Clean();

using (var tx = new OutputTransaction([first, second]))
{
    File.WriteAllText(tx.Stage(first), "new first"); File.WriteAllText(tx.Stage(second), "new second");
    File.WriteAllText(second, "external edit");
    bool failed = false; try { tx.Commit(); } catch (IOException) { failed = true; }
    Assert(failed, "Changed file was overwritten");
}
Assert(File.ReadAllText(first) == "old first" && File.ReadAllText(second) == "external edit", "External edit was lost"); Clean();

using (var tx = new OutputTransaction([first, second]))
{
    File.WriteAllText(tx.Stage(first), "new first"); File.WriteAllText(tx.Stage(second), "new second"); tx.Commit();
}
Assert(File.ReadAllText(first) == "new first" && File.ReadAllText(second) == "new second", "Replacement failed"); Clean();
string third = Path.Combine(root, "third.dat");
using (var tx = new OutputTransaction([third])) { File.WriteAllText(tx.Stage(third), "created"); tx.Commit(); }
Assert(File.ReadAllText(third) == "created", "New output failed"); Clean();
Console.WriteLine("PASS: cancellation, rollback after partial commit, external changes, overwrite, new files; no staged/backup files left.");
