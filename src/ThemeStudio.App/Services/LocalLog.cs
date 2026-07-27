namespace ThemeStudio.App.Services;

public sealed class LocalLog(string path) : IDisposable
{
    private readonly object _gate = new();

    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? error = null) => Write("ERROR", message, error);

    private void Write(string level, string message, Exception? error)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{(error is null ? string.Empty : Environment.NewLine + error)}{Environment.NewLine}";
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never block the workbench or Codex launch path.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 2 * 1024 * 1024)
            return;
        var previous = Path.ChangeExtension(path, ".previous.log");
        File.Move(path, previous, true);
    }

    public void Dispose() { }
}
