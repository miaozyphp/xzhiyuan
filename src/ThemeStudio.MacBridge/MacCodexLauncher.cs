using System.Diagnostics;
using System.Text.RegularExpressions;
using ThemeStudio.Core.Codex;

namespace ThemeStudio.MacBridge;

public sealed partial class MacCodexLauncher
{
    public Process Launch(CodexInstallation installation, int debugPort)
    {
        if (debugPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(debugPort));
        var startInfo = new ProcessStartInfo(installation.ExecutablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        startInfo.ArgumentList.Add($"--remote-debugging-port={debugPort}");
        startInfo.Environment["THEME_STUDIO_MANAGED_LAUNCH"] = "1";
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Codex 没有成功启动。");
    }

    public IReadOnlyList<Process> FindRunning(CodexInstallation installation)
    {
        if (!OperatingSystem.IsMacOS())
            return [];
        var results = new List<Process>();
        foreach (var line in ReadProcessList())
        {
            var match = ProcessLine().Match(line);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var processId))
                continue;
            var command = match.Groups[2].Value;
            if (!command.Contains(installation.ExecutablePath, StringComparison.Ordinal))
                continue;
            try { results.Add(Process.GetProcessById(processId)); }
            catch (ArgumentException) { }
        }
        return results;
    }

    public async Task<bool> StopForManagedRestartAsync(IReadOnlyList<Process> processes, CancellationToken cancellationToken = default)
    {
        if (processes.Count == 0)
            return true;
        await TryGracefulQuitAsync(cancellationToken);
        if (await WaitForAllAsync(processes, TimeSpan.FromSeconds(5), cancellationToken))
            return true;

        // Theme application never force-quits Codex. Keep the native session
        // running when the application declines the normal quit request.
        return false;
    }

    private static IReadOnlyList<string> ReadProcessList()
    {
        try
        {
            var startInfo = new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-axo");
            startInfo.ArgumentList.Add("pid=,command=");
            using var process = Process.Start(startInfo);
            if (process is null)
                return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        }
        catch { return []; }
    }

    private static async Task TryGracefulQuitAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add("tell application \"Codex\" to quit");
            using var process = Process.Start(startInfo);
            if (process is not null)
                await process.WaitForExitAsync(cancellationToken);
        }
        catch { }
    }

    private static async Task<bool> WaitForAllAsync(IReadOnlyList<Process> processes, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processes.All(HasExited))
                return true;
            await Task.Delay(80, cancellationToken);
        }
        return processes.All(HasExited);
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }

    [GeneratedRegex(@"^\s*(\d+)\s+(.+)$")]
    private static partial Regex ProcessLine();
}
