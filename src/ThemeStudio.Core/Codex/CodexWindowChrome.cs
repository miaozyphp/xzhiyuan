using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThemeStudio.Core.Codex;

public sealed class CodexWindowChrome
{
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private readonly Dictionary<nint, (int Attribute, int Value)> _originalModes = [];

    public int Apply(IReadOnlyList<Process> processes, bool dark)
    {
        var applied = 0;
        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                var window = process.MainWindowHandle;
                if (window == nint.Zero)
                    continue;

                if (!_originalModes.ContainsKey(window) && TryGetMode(window, out var attribute, out var original))
                    _originalModes[window] = (attribute, original);

                if (TrySetMode(window, dark ? 1 : 0))
                    applied++;
            }
            catch (InvalidOperationException)
            {
                // The Codex process can exit while its window is being refreshed.
            }
        }

        return applied;
    }

    public int Restore(IReadOnlyList<Process> processes)
    {
        var restored = 0;
        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                var window = process.MainWindowHandle;
                if (window == nint.Zero || !_originalModes.Remove(window, out var original))
                    continue;

                var value = original.Value;
                if (DwmSetWindowAttribute(window, original.Attribute, ref value, sizeof(int)) >= 0)
                    restored++;
            }
            catch (InvalidOperationException)
            {
                // The window has already closed, so there is nothing left to restore.
            }
        }

        return restored;
    }

    private static bool TryGetMode(nint window, out int attribute, out int value)
    {
        attribute = DwmwaUseImmersiveDarkMode;
        if (DwmGetWindowAttribute(window, attribute, out value, sizeof(int)) >= 0)
            return true;

        attribute = DwmwaUseImmersiveDarkModeBefore20H1;
        return DwmGetWindowAttribute(window, attribute, out value, sizeof(int)) >= 0;
    }

    private static bool TrySetMode(nint window, int mode)
    {
        var value = mode;
        if (DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) >= 0)
            return true;

        value = mode;
        return DwmSetWindowAttribute(window, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int)) >= 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
