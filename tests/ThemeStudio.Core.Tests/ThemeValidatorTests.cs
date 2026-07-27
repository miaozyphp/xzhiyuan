using ThemeStudio.Core.Models;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.Core.Tests;

public sealed class ThemeValidatorTests
{
    [Fact]
    public void RejectsAssetPathTraversal()
    {
        var theme = CreateTheme() with { Media = new ThemeMedia { Kind = MediaKind.Image, AssetPath = "../outside.png" } };
        Assert.Throws<InvalidDataException>(() => ThemeValidator.Validate(theme));
    }

    [Fact]
    public void RejectsInvalidColors()
    {
        var theme = CreateTheme() with { Palette = new ThemePalette { Accent = "cyan" } };
        Assert.Throws<InvalidDataException>(() => ThemeValidator.Validate(theme));
    }

    [Fact]
    public void AcceptsACompleteTheme()
    {
        ThemeValidator.Validate(CreateTheme());
    }

    [Fact]
    public void AllowsWallpaperFirstLargeSurfaces()
    {
        var surfaces = new ThemeSurfaces { Opacity = 0.05, SidebarOpacity = 0.05, ComposerOpacity = 0.2, BubbleOpacity = 0.2 };
        ThemeValidator.Validate(CreateTheme() with { Surfaces = surfaces });
    }

    [Fact]
    public void RejectsUnknownBadgeStyle()
    {
        Assert.Throws<InvalidDataException>(() => ThemeValidator.Validate(
            CreateTheme() with { Badge = new ThemeBadge { Style = "orb" } }));
    }

    internal static ThemeDefinition CreateTheme(string id = "test-theme") => new()
    {
        Id = id,
        Name = "Test Theme",
        BuiltIn = false
    };
}
