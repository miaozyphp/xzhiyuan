using System.Diagnostics;

namespace ThemeStudio.Core.Codex;

public sealed class CodexLauncher
{
    public const bool KillEntireProcessTree = false;

    public Process Launch(CodexInstallation installation, int debugPort)
    {
        if (debugPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(debugPort));

        var startInfo = new ProcessStartInfo
        {
            FileName = installation.ExecutablePath,
            Arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={debugPort}",
            WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath)!,
            UseShellExecute = false
        };
        startInfo.Environment["THEME_STUDIO_MANAGED_LAUNCH"] = "1";
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Codex process could not be started.");
    }

    public IReadOnlyList<Process> FindRunning(CodexInstallation installation)
    {
        var processName = Path.GetFileNameWithoutExtension(installation.ExecutablePath);
        return Process.GetProcessesByName(processName).Where(process => MatchesExecutable(process, installation.ExecutablePath)).ToArray();
    }

    public async Task<bool> StopForManagedRestartAsync(
        IReadOnlyList<Process> processes,
        CancellationToken cancellationToken = default)
    {
        if (processes.Count == 0)
            return true;

        return await Task.Run(() =>
        {
            var owner = processes.FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero);
            try
            {
                owner?.CloseMainWindow();
                if (owner?.WaitForExit(3000) == true && WaitForAll(processes, TimeSpan.FromSeconds(2), cancellationToken))
                    return true;
            }
            catch
            {
                // Fall through to exact-process termination after the graceful close window.
            }

            foreach (var process in processes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!process.HasExited)
                        process.Kill(KillEntireProcessTree);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the snapshot and this loop.
                }
            }

            return WaitForAll(processes, TimeSpan.FromSeconds(4), cancellationToken);
        }, cancellationToken);
    }

    private static bool MatchesExecutable(Process process, string expectedPath)
    {
        try
        {
            return string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForAll(IReadOnlyList<Process> processes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processes.All(HasExited))
                return true;
            Thread.Sleep(50);
        }
        return processes.All(HasExited);
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
}
