using ThemeStudio.MacBridge;

namespace ThemeStudio.Core.Tests;

public sealed class MacCodexLocatorTests
{
    [Fact]
    public void ReadsCodexApplicationBundle()
    {
        using var temp = new TempDirectory();
        var bundle = Path.Combine(temp.Path, "Codex.app");
        var contents = Path.Combine(bundle, "Contents");
        var executableDirectory = Path.Combine(contents, "MacOS");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "Codex"), "test");
        File.WriteAllText(Path.Combine(contents, "Info.plist"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>com.openai.codex</string>
              <key>CFBundleDisplayName</key><string>Codex</string>
              <key>CFBundleExecutable</key><string>Codex</string>
              <key>CFBundleShortVersionString</key><string>1.2.3</string>
            </dict></plist>
            """);

        var installation = MacCodexLocator.TryReadBundle(bundle);

        Assert.NotNull(installation);
        Assert.Equal("com.openai.codex", installation.PackageFullName);
        Assert.Equal("1.2.3", installation.Version);
        Assert.Equal(Path.Combine(executableDirectory, "Codex"), installation.ExecutablePath);
    }

    [Fact]
    public void RejectsBundleWhoseExecutableEscapesContentsDirectory()
    {
        using var temp = new TempDirectory();
        var bundle = Path.Combine(temp.Path, "Codex.app");
        var contents = Path.Combine(bundle, "Contents");
        Directory.CreateDirectory(contents);
        File.WriteAllText(Path.Combine(temp.Path, "outside"), "test");
        File.WriteAllText(Path.Combine(contents, "Info.plist"),
            """
            <plist version="1.0"><dict>
              <key>CFBundleDisplayName</key><string>Codex</string>
              <key>CFBundleExecutable</key><string>../../../outside</string>
            </dict></plist>
            """);

        Assert.Null(MacCodexLocator.TryReadBundle(bundle));
    }
}
