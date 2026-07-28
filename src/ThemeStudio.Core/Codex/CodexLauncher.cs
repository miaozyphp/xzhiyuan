using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThemeStudio.Core.Codex;

public sealed class CodexLauncher
{
    public const bool KillEntireProcessTree = false;

    public Process Launch(CodexInstallation installation, int debugPort)
    {
        if (debugPort is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(debugPort));

        var arguments = $"--remote-debugging-address=127.0.0.1 --remote-debugging-port={debugPort}";
        var activationError = TryActivatePackagedApplication(installation, arguments, out var activatedProcess);
        if (activationError is null && activatedProcess is not null)
            return activatedProcess;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = installation.ExecutablePath,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(installation.ExecutablePath)!,
                UseShellExecute = true
            };
            return Process.Start(startInfo) ?? throw new InvalidOperationException("Codex process could not be started.");
        }
        catch (Exception managedError) when (managedError is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            var fallbackError = TryActivatePackagedApplication(installation, string.Empty, out _);
            throw new CodexLaunchException(
                fallbackError is null
                    ? "Codex 无法建立主题连接，已恢复为普通启动。"
                    : "Codex 无法建立主题连接，也没有成功恢复普通启动。",
                fallbackError is null,
                new AggregateException(new[] { activationError, managedError, fallbackError }.Where(error => error is not null).Cast<Exception>()));
        }
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
                // A failed graceful close is reported to the caller below.
            }

            // A theme operation must never force-terminate Codex. If graceful
            // shutdown is refused, the caller leaves the native session intact.
            return false;
        }, cancellationToken);
    }

    private static Exception? TryActivatePackagedApplication(
        CodexInstallation installation,
        string arguments,
        out Process? process)
    {
        process = null;
        if (!OperatingSystem.IsWindows())
            return new PlatformNotSupportedException();

        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            var appUserModelId = $"{installation.PackageFamilyName}!{installation.ApplicationId}";
            var result = manager.ActivateApplication(appUserModelId, arguments, ActivateOptions.None, out var processId);
            Marshal.ThrowExceptionForHR(result);
            process = Process.GetProcessById(checked((int)processId));
            return null;
        }
        catch (Exception error) when (error is COMException or ExternalException or InvalidCastException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return error;
        }
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

public sealed class CodexLaunchException(string message, bool nativeFallbackStarted, Exception innerException)
    : InvalidOperationException(message, innerException)
{
    public bool NativeFallbackStarted { get; } = nativeFallbackStarted;
}

[Flags]
internal enum ActivateOptions
{
    None = 0
}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        ActivateOptions options,
        out uint processId);

    [PreserveSig]
    int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, IntPtr verb, out uint processId);

    [PreserveSig]
    int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
internal class ApplicationActivationManager
{
}
