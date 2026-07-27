using ThemeStudio.Core.Models;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.Core.Tests;

public sealed class ThemeRepositoryTests
{
    [Fact]
    public async Task InitializesBuiltInCatalogAndSettings()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();

        var themes = await repository.GetAllAsync();
        var settings = await repository.GetSettingsAsync();

        Assert.Equal(11, themes.Count);
        Assert.Equal(DefaultThemeCatalog.DefaultThemeId, settings.DefaultThemeId);
        Assert.Equal(1, settings.BadgeBrandingVersion);
        Assert.All(themes, theme => Assert.True(theme.BuiltIn));
        Assert.All(themes, theme => Assert.Equal(ThemeBadge.DefaultAssetPath, theme.Badge.AssetPath));
    }

    [Fact]
    public async Task CopyOwnsMediaAndSurvivesSourceDeletion()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var sourceFile = System.IO.Path.Combine(temp.Path, "source.png");
        await File.WriteAllBytesAsync(sourceFile, [1, 2, 3, 4]);

        var source = ThemeValidatorTests.CreateTheme("source-theme") with
        {
            Media = new ThemeMedia
            {
                Kind = MediaKind.Image,
                AssetPath = await repository.ImportAssetAsync("source-theme", sourceFile)
            }
        };
        source = await repository.SaveAsync(source);
        var copy = await repository.CreateCopyAsync(source, "Independent Copy");
        await repository.DeleteAsync(source.Id);

        Assert.NotEqual(source.Media.AssetPath, copy.Media.AssetPath);
        Assert.True(File.Exists(repository.ResolveAssetPath(copy.Media.AssetPath!)));
    }

    [Fact]
    public async Task CreatesCopyWithDroppedMediaAndBrandBadge()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var source = ThemeValidatorTests.CreateTheme("source-theme");

        var copy = await repository.CreateCopyWithMediaAsync(source, "Dropped Video", MediaKind.Video, new byte[] { 5, 4, 3, 2 }, ".mp4");

        Assert.False(copy.BuiltIn);
        Assert.Equal(MediaKind.Video, copy.Media.Kind);
        Assert.Equal(ThemeBadge.DefaultAssetPath, copy.Badge.AssetPath);
        Assert.True(copy.Layers.Badge);
        Assert.Equal([5, 4, 3, 2], await File.ReadAllBytesAsync(repository.ResolveAssetPath(copy.Media.AssetPath!)));
    }

    [Fact]
    public async Task SavingCustomThemePersistsReplacementMedia()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var firstSource = System.IO.Path.Combine(temp.Path, "first.png");
        var replacementSource = System.IO.Path.Combine(temp.Path, "replacement.png");
        await File.WriteAllBytesAsync(firstSource, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(replacementSource, [9, 8, 7, 6]);

        var theme = ThemeValidatorTests.CreateTheme("editable-theme") with
        {
            Media = new ThemeMedia
            {
                Kind = MediaKind.Image,
                AssetPath = await repository.ImportAssetAsync("editable-theme", firstSource)
            }
        };
        theme = await repository.SaveAsync(theme);
        var replacementPath = await repository.ImportAssetAsync(theme.Id, replacementSource);
        await repository.SaveAsync(theme with
        {
            Media = theme.Media with { Kind = MediaKind.Image, AssetPath = replacementPath }
        });

        var reopened = new ThemeRepository(temp.Path);
        await reopened.InitializeAsync();
        var saved = await reopened.GetAsync(theme.Id);

        Assert.NotNull(saved);
        Assert.Equal(replacementPath, saved.Media.AssetPath);
        Assert.Equal([9, 8, 7, 6], await File.ReadAllBytesAsync(reopened.ResolveAssetPath(saved.Media.AssetPath!)));
    }

    [Fact]
    public async Task BuiltInThemeCannotBeUpdatedOrDeleted()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var builtIn = (await repository.GetAllAsync()).First(theme => theme.BuiltIn);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(builtIn with { Name = "Changed" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.DeleteAsync(builtIn.Id));
    }

    [Fact]
    public async Task UpgradesOutdatedBuiltInThemeWithoutChangingCustomThemes()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var rain = (await repository.GetAsync(DefaultThemeCatalog.DefaultThemeId))!;
        var custom = await repository.SaveAsync(ThemeValidatorTests.CreateTheme("personal-theme"));

        var rainPath = Path.Combine(repository.ThemesPath, $"{rain.Id}.json");
        await File.WriteAllTextAsync(rainPath, System.Text.Json.JsonSerializer.Serialize(rain with { Version = "0.9.0" }));
        await repository.InitializeAsync();

        Assert.Equal("1.0.4", (await repository.GetAsync(rain.Id))!.Version);
        Assert.Equal(custom, await repository.GetAsync(custom.Id));
    }

    [Fact]
    public async Task RefreshesBuiltInEmblemDuringUpgrade()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        var source = Path.Combine(temp.Path, "x-zhiyuan-emblem.png");
        await File.WriteAllBytesAsync(source, [9, 8, 7, 6]);

        await repository.InitializeAsync(builtInEmblemPath: source);
        var target = repository.ResolveAssetPath("assets/built-in/theme-studio-emblem.png");
        await File.WriteAllBytesAsync(target, [1, 2, 3]);
        await repository.InitializeAsync(builtInEmblemPath: source);

        Assert.Equal([9, 8, 7, 6], await File.ReadAllBytesAsync(target));
    }

    [Fact]
    public async Task MigratesExistingThemesToBrandBadgeOnlyOnce()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var legacySource = Path.Combine(temp.Path, "legacy-badge.png");
        await File.WriteAllBytesAsync(legacySource, [1, 2, 3, 4]);
        var legacyPath = await repository.ImportAssetAsync("personal-theme", legacySource);
        var custom = await repository.SaveAsync(ThemeValidatorTests.CreateTheme("personal-theme") with
        {
            Badge = new ThemeBadge { AssetPath = legacyPath, Position = "bottom-right", Style = "glass" },
            Layers = new ThemeLayers { Badge = false }
        });
        var settings = await repository.GetSettingsAsync();
        await repository.SaveSettingsAsync(settings with { BadgeBrandingVersion = 0 });

        await repository.InitializeAsync();

        var migrated = (await repository.GetAsync(custom.Id))!;
        Assert.Equal(ThemeBadge.DefaultAssetPath, migrated.Badge.AssetPath);
        Assert.Equal("top-left", migrated.Badge.Position);
        Assert.Equal("icon", migrated.Badge.Style);
        Assert.True(migrated.Layers.Badge);

        var replacementSource = Path.Combine(temp.Path, "replacement-badge.png");
        await File.WriteAllBytesAsync(replacementSource, [9, 8, 7, 6]);
        var replacementPath = await repository.ImportAssetAsync(custom.Id, replacementSource);
        await repository.SaveAsync(migrated with { Badge = migrated.Badge with { AssetPath = replacementPath } });

        await repository.InitializeAsync();

        Assert.Equal(replacementPath, (await repository.GetAsync(custom.Id))!.Badge.AssetPath);
    }
}
