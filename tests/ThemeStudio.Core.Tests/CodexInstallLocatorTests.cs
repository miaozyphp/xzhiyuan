using ThemeStudio.Core.Codex;

namespace ThemeStudio.Core.Tests;

public sealed class CodexInstallLocatorTests
{
    [Fact]
    public void LocatesOnlyManifestValidatedCodexPackage()
    {
        var installation = new CodexInstallLocator().Locate();

        Assert.NotNull(installation);
        Assert.Equal("OpenAI.Codex_2p2nqsd0c76g0", installation.PackageFamilyName);
        Assert.EndsWith("ChatGPT.exe", installation.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(installation.ExecutablePath));
    }
}
