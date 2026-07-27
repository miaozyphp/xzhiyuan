namespace ThemeStudio.Core.Storage;

public static class ThemeDataRoot
{
    public static string Resolve(string configuredPath)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredPath));
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        var attributes = File.GetAttributes(fullPath);
        if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            return fullPath;

        // Resolving the final target traverses the junction and is blocked by
        // Windows redirection-trust enforcement before the app can start.
        var target = Directory.ResolveLinkTarget(fullPath, returnFinalTarget: false)
            ?? throw new IOException($"The theme data directory link cannot be resolved: '{fullPath}'.");
        if (!target.Attributes.HasFlag(FileAttributes.Directory))
            throw new IOException($"The theme data directory link does not point to a directory: '{fullPath}'.");

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.FullName));
    }
}
