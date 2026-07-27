using System.Runtime.InteropServices;

namespace ThemeStudio.App.Services;

internal static class ExistingWindowActivator
{
    private const int RestoreWindow = 9;

    public static void Restore()
    {
        var handle = FindWindow(null, "x纸鸢");
        if (handle == IntPtr.Zero)
            return;

        ShowWindowAsync(handle, RestoreWindow);
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
