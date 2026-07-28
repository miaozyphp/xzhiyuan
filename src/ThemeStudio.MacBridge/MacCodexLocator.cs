using System.Diagnostics;
using System.Xml.Linq;
using ThemeStudio.Core.Codex;

namespace ThemeStudio.MacBridge;

public sealed class MacCodexLocator
{
    public CodexInstallation? Locate()
    {
        var candidates = CandidateBundles()
            .Distinct(StringComparer.Ordinal)
            .Select(TryReadBundle)
            .Where(item => item is not null)
            .Cast<CodexInstallation>()
            .OrderByDescending(item => ParseVersion(item.Version))
            .ToArray();
        return candidates.FirstOrDefault();
    }

    public static CodexInstallation? TryReadBundle(string bundlePath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundlePath));
            var plistPath = Path.Combine(root, "Contents", "Info.plist");
            if (!Directory.Exists(root) || !File.Exists(plistPath))
                return null;

            var executableName = ReadPlistValue(plistPath, "CFBundleExecutable");
            if (string.IsNullOrWhiteSpace(executableName))
                return null;
            var executablePath = Path.GetFullPath(Path.Combine(root, "Contents", "MacOS", executableName));
            var rootPrefix = root + Path.DirectorySeparatorChar;
            if (!File.Exists(executablePath) || !executablePath.StartsWith(rootPrefix, StringComparison.Ordinal))
                return null;

            var displayName = ReadPlistValue(plistPath, "CFBundleDisplayName")
                ?? ReadPlistValue(plistPath, "CFBundleName")
                ?? Path.GetFileNameWithoutExtension(root);
            if (!displayName.Contains("Codex", StringComparison.OrdinalIgnoreCase))
                return null;

            var bundleId = ReadPlistValue(plistPath, "CFBundleIdentifier") ?? "com.openai.codex";
            var version = ReadPlistValue(plistPath, "CFBundleShortVersionString")
                ?? ReadPlistValue(plistPath, "CFBundleVersion")
                ?? "0.0.0";
            return new CodexInstallation(bundleId, bundleId, version, root, executablePath, displayName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static IEnumerable<string> CandidateBundles()
    {
        yield return "/Applications/Codex.app";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            yield return Path.Combine(home, "Applications", "Codex.app");

        if (!OperatingSystem.IsMacOS())
            yield break;
        foreach (var path in RunAndReadLines("/usr/bin/mdfind", "kMDItemFSName == 'Codex.app'c"))
            yield return path;
    }

    private static string? ReadPlistValue(string plistPath, string key)
    {
        try
        {
            var document = XDocument.Load(plistPath, LoadOptions.None);
            var dictionary = document.Root?.Element("dict");
            if (dictionary is not null)
            {
                var elements = dictionary.Elements().ToArray();
                for (var index = 0; index + 1 < elements.Length; index++)
                {
                    if (elements[index].Name.LocalName == "key" && elements[index].Value == key)
                        return elements[index + 1].Value;
                }
            }
        }
        catch (System.Xml.XmlException) when (OperatingSystem.IsMacOS())
        {
            // Binary plists are read through plutil below.
        }

        if (!OperatingSystem.IsMacOS())
            return null;
        return RunAndReadLines("/usr/bin/plutil", "-extract", key, "raw", "-o", "-", plistPath).FirstOrDefault();
    }

    private static IReadOnlyList<string> RunAndReadLines(string fileName, params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null)
                return [];
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0
                ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];
        }
        catch
        {
            return [];
        }
    }

    private static Version ParseVersion(string value) =>
        Version.TryParse(value.Split('-', 2)[0], out var version) ? version : new Version();
}
