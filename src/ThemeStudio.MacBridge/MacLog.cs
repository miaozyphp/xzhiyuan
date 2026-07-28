namespace ThemeStudio.MacBridge;

public sealed class MacLog(string path) : IDisposable
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
                var info = new FileInfo(path);
                if (info.Exists && info.Length >= 2 * 1024 * 1024)
                    File.Move(path, Path.ChangeExtension(path, ".previous.log"), true);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Diagnostics must never block the workbench or Codex launch path.
        }
    }

    public void Dispose() { }
}
