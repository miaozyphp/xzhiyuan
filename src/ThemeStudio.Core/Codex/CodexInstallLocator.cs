using System.Xml.Linq;
using Microsoft.Win32;

namespace ThemeStudio.Core.Codex;

public sealed record CodexInstallation(
    string PackageFullName,
    string PackageFamilyName,
    string Version,
    string InstallRoot,
    string ExecutablePath,
    string ApplicationId);

public sealed class CodexInstallLocator
{
    private const string PackagesRegistryPath =
        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public CodexInstallation? Locate()
    {
        var candidates = new List<CodexInstallation>();
        using var packages = Registry.CurrentUser.OpenSubKey(PackagesRegistryPath);
        if (packages is null)
            return null;

        foreach (var packageName in packages.GetSubKeyNames().Where(name => name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)))
        {
            using var package = packages.OpenSubKey(packageName);
            var root = package?.GetValue("PackageRootFolder") as string;
            var candidate = TryReadManifest(packageName, root);
            if (candidate is not null)
                candidates.Add(candidate);
        }

        return candidates.OrderByDescending(item => ParseVersion(item.Version)).FirstOrDefault();
    }

    internal static CodexInstallation? TryReadManifest(string packageName, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;

        var manifestPath = Path.Combine(root, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var document = XDocument.Load(manifestPath, LoadOptions.None);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;
            var identity = document.Root?.Element(ns + "Identity");
            if (!string.Equals(identity?.Attribute("Name")?.Value, "OpenAI.Codex", StringComparison.OrdinalIgnoreCase))
                return null;

            var application = document.Root?.Element(ns + "Applications")?.Elements(ns + "Application").FirstOrDefault();
            var executable = application?.Attribute("Executable")?.Value;
            if (string.IsNullOrWhiteSpace(executable))
                return null;

            var executablePath = Path.GetFullPath(Path.Combine(root, executable.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(executablePath) || !executablePath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                return null;

            var familyName = BuildFamilyName(packageName);
            return new CodexInstallation(
                packageName,
                familyName,
                identity?.Attribute("Version")?.Value ?? "0.0.0.0",
                root,
                executablePath,
                application?.Attribute("Id")?.Value ?? "App");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string BuildFamilyName(string packageName)
    {
        var parts = packageName.Split('_');
        return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : "OpenAI.Codex_2p2nqsd0c76g0";
    }

    private static Version ParseVersion(string value) => Version.TryParse(value, out var version) ? version : new Version();
}
