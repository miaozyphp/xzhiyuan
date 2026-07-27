using System.Diagnostics;
using Microsoft.Win32;

namespace ThemeStudio.App.Services;

public static class BrokerRegistration
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ThemeStudioForCodex";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Application.ExecutablePath}\" --broker", RegistryValueKind.String);
            StartBrokerIfNeeded();
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static void StartBrokerIfNeeded()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--broker",
                UseShellExecute = true
            });
        }
        catch
        {
            // The Run entry will start the broker on the next sign-in.
        }
    }
}
