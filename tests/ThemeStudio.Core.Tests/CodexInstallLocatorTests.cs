using ThemeStudio.Core.Codex;

namespace ThemeStudio.Core.Tests;

public sealed class CodexInstallLocatorTests
{
    [Fact]
    public void LocatesOnlyManifestValidatedCodexPackage()
    {
        using var temp = new TempDirectory();
        var executableDirectory = Path.Combine(temp.Path, "app");
        Directory.CreateDirectory(executableDirectory);
        File.WriteAllText(Path.Combine(executableDirectory, "ChatGPT.exe"), string.Empty);
        File.WriteAllText(
            Path.Combine(temp.Path, "AppxManifest.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="OpenAI.Codex" Version="0.1.13.0" />
              <Applications>
                <Application Id="App" Executable="app/ChatGPT.exe" />
              </Applications>
            </Package>
            """);

        var installation = CodexInstallLocator.TryReadManifest(
            "OpenAI.Codex_0.1.13.0_x64__2p2nqsd0c76g0",
            temp.Path);

        Assert.NotNull(installation);
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0", installation.PackageFamilyName);
        Assert.Equal("0.1.13.0", installation.Version);
        Assert.EndsWith("ChatGPT.exe", installation.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(installation.ExecutablePath));
    }

    [Fact]
    public void RejectsPackageWithUnexpectedManifestIdentity()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            Path.Combine(temp.Path, "AppxManifest.xml"),
            """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
              <Identity Name="Example.UnrelatedApp" Version="1.0.0.0" />
              <Applications>
                <Application Id="App" Executable="Unrelated.exe" />
              </Applications>
            </Package>
            """);
        File.WriteAllText(Path.Combine(temp.Path, "Unrelated.exe"), string.Empty);

        var installation = CodexInstallLocator.TryReadManifest(
            "OpenAI.Codex_1.0.0.0_x64__2p2nqsd0c76g0",
            temp.Path);

        Assert.Null(installation);
    }
}
