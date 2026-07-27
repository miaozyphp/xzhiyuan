using ThemeStudio.Core.Storage;

namespace ThemeStudio.Core.Tests;

public sealed class ThemeDataRootTests
{
    [Fact]
    public void ReturnsExistingPhysicalDirectory()
    {
        using var temp = new TempDirectory();

        var resolved = ThemeDataRoot.Resolve(temp.Path);

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(temp.Path)), resolved);
    }

    [Fact]
    public void CreatesMissingPhysicalDirectory()
    {
        using var temp = new TempDirectory();
        var requested = Path.Combine(temp.Path, "theme-data");

        var resolved = ThemeDataRoot.Resolve(requested);

        Assert.Equal(Path.GetFullPath(requested), resolved);
        Assert.True(Directory.Exists(resolved));
    }
}
