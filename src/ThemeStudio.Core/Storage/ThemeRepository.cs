using System.Text.Json;
using System.Text.Json.Serialization;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Storage;

public sealed class ThemeRepository
{
    private const int CurrentBadgeBrandingVersion = 1;

    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".mp4", ".webm", ".mov"
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ThemeRepository(string rootPath)
    {
        RootPath = Path.GetFullPath(rootPath);
        ThemesPath = Path.Combine(RootPath, "themes");
        AssetsPath = Path.Combine(RootPath, "assets");
        SettingsPath = Path.Combine(RootPath, "settings.json");
    }

    public string RootPath { get; }
    public string ThemesPath { get; }
    public string AssetsPath { get; }
    public string SettingsPath { get; }

    public async Task InitializeAsync(
        string? builtInWallpaperPath = null,
        string? builtInEmblemPath = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ThemesPath);
        Directory.CreateDirectory(AssetsPath);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wallpaperTarget = Path.Combine(AssetsPath, "built-in", "rain-archive.png");
            if (!File.Exists(wallpaperTarget) && !string.IsNullOrWhiteSpace(builtInWallpaperPath) && File.Exists(builtInWallpaperPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(wallpaperTarget)!);
                File.Copy(builtInWallpaperPath, wallpaperTarget, true);
            }

            var emblemTarget = Path.Combine(AssetsPath, "built-in", "theme-studio-emblem.png");
            if (!string.IsNullOrWhiteSpace(builtInEmblemPath) && File.Exists(builtInEmblemPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(emblemTarget)!);
                File.Copy(builtInEmblemPath, emblemTarget, true);
            }

            foreach (var theme in DefaultThemeCatalog.Create())
            {
                var path = GetThemePath(theme.Id);
                var existing = await ReadThemeAsync(path, cancellationToken);
                if (existing is null || (existing.BuiltIn && !string.Equals(existing.Version, theme.Version, StringComparison.Ordinal)))
                    await WriteJsonAtomicAsync(path, theme, cancellationToken);
            }

            var settingsExists = File.Exists(SettingsPath);
            var settings = settingsExists
                ? await ReadSettingsAsync(cancellationToken)
                : new StudioSettings();
            var settingsChanged = !settingsExists;
            if (settings.BadgeBrandingVersion < CurrentBadgeBrandingVersion)
            {
                await ApplyBrandBadgeDefaultsAsync(cancellationToken);
                settings = settings with { BadgeBrandingVersion = CurrentBadgeBrandingVersion };
                settingsChanged = true;
            }

            if (settingsChanged)
                await WriteJsonAtomicAsync(SettingsPath, settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ThemeDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var themes = new List<ThemeDefinition>();
        foreach (var file in Directory.EnumerateFiles(ThemesPath, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            await using var stream = File.OpenRead(file);
            var theme = await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, _json, cancellationToken);
            if (theme is not null)
                themes.Add(theme);
        }

        return themes.OrderByDescending(item => item.Id == DefaultThemeCatalog.DefaultThemeId)
            .ThenBy(item => item.BuiltIn ? 0 : 1)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<ThemeDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ValidateId(id);
        var path = GetThemePath(id);
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, _json, cancellationToken);
    }

    public async Task<ThemeDefinition> SaveAsync(ThemeDefinition theme, CancellationToken cancellationToken = default)
    {
        ThemeValidator.Validate(theme);
        var existing = await GetAsync(theme.Id, cancellationToken);
        if (existing?.BuiltIn == true)
            throw new InvalidOperationException("Built-in themes are read-only. Save a copy before editing.");

        var saved = theme with { BuiltIn = false, UpdatedAt = DateTimeOffset.UtcNow };
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteJsonAtomicAsync(GetThemePath(saved.Id), saved, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return saved;
    }

    public async Task<ThemeDefinition> DuplicateAsync(string sourceId, string newName, CancellationToken cancellationToken = default)
    {
        var source = await GetAsync(sourceId, cancellationToken) ?? throw new FileNotFoundException("Theme not found.");
        var id = await CreateAvailableIdAsync(Slugify(newName), cancellationToken);
        var duplicate = source with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} 副本" : newName.Trim(),
            BuiltIn = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        duplicate = duplicate with
        {
            Media = duplicate.Media with { AssetPath = await CopyOwnedAssetAsync(source.Media.AssetPath, id, cancellationToken) },
            Badge = duplicate.Badge with { AssetPath = await CopyOwnedAssetAsync(source.Badge.AssetPath, id, cancellationToken) }
        };

        return await SaveAsync(duplicate, cancellationToken);
    }

    public async Task<ThemeDefinition> CreateCopyAsync(ThemeDefinition source, string newName, CancellationToken cancellationToken = default)
    {
        ThemeValidator.Validate(source);
        var displayName = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} 副本" : newName.Trim();
        var id = await CreateAvailableIdAsync(Slugify(displayName), cancellationToken);
        var copy = source with
        {
            Id = id,
            Name = displayName,
            BuiltIn = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        copy = copy with
        {
            Media = copy.Media with { AssetPath = await CopyOwnedAssetAsync(source.Media.AssetPath, id, cancellationToken) },
            Badge = copy.Badge with { AssetPath = await CopyOwnedAssetAsync(source.Badge.AssetPath, id, cancellationToken) }
        };
        return await SaveAsync(copy, cancellationToken);
    }

    public async Task<ThemeDefinition> CreateCopyWithMediaAsync(
        ThemeDefinition source,
        string newName,
        MediaKind mediaKind,
        ReadOnlyMemory<byte> mediaBytes,
        string mediaExtension,
        CancellationToken cancellationToken = default)
    {
        ThemeValidator.Validate(source);
        var displayName = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} 自定义" : newName.Trim();
        var id = await CreateAvailableIdAsync(Slugify(displayName), cancellationToken);
        var copy = source with
        {
            Id = id,
            Name = displayName,
            BuiltIn = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            Layers = source.Layers with { Badge = true },
            Media = source.Media with
            {
                Kind = mediaKind,
                AssetPath = await ImportAssetBytesAsync(id, mediaBytes, mediaExtension, cancellationToken)
            },
            Badge = source.Badge with { AssetPath = ThemeBadge.DefaultAssetPath }
        };
        return await SaveAsync(copy, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var theme = await GetAsync(id, cancellationToken);
        if (theme is null)
            return;
        if (theme.BuiltIn)
            throw new InvalidOperationException("Built-in themes cannot be deleted.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(GetThemePath(id));
            var ownedAssets = Path.Combine(AssetsPath, id);
            if (Directory.Exists(ownedAssets))
                Directory.Delete(ownedAssets, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> ImportAssetAsync(string themeId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ValidateId(themeId);
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
            throw new FileNotFoundException("Selected media file does not exist.", source);

        var extension = Path.GetExtension(source);
        if (!AllowedAssetExtensions.Contains(extension))
            throw new InvalidDataException("Unsupported media format.");

        var folder = Path.Combine(AssetsPath, themeId);
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var target = Path.Combine(folder, fileName);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken);
        return Path.GetRelativePath(RootPath, target).Replace('\\', '/');
    }

    public async Task<string> ImportAssetBytesAsync(
        string themeId,
        ReadOnlyMemory<byte> bytes,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ValidateId(themeId);
        if (bytes.Length == 0)
            throw new InvalidDataException("Dropped media file is empty.");

        extension = extension.Trim().ToLowerInvariant();
        if (!AllowedAssetExtensions.Contains(extension))
            throw new InvalidDataException("Unsupported media format.");

        var folder = Path.Combine(AssetsPath, themeId);
        Directory.CreateDirectory(folder);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var target = Path.Combine(folder, fileName);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await output.WriteAsync(bytes, cancellationToken);
        return Path.GetRelativePath(RootPath, target).Replace('\\', '/');
    }

    public string ResolveAssetPath(string relativePath)
    {
        ThemeValidator.ValidateRelativePath(relativePath);
        var resolved = Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = RootPath.EndsWith(Path.DirectorySeparatorChar) ? RootPath : RootPath + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Asset path escapes the theme repository.");
        return resolved;
    }

    public async Task<StudioSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
            return new StudioSettings();
        return await ReadSettingsAsync(cancellationToken);
    }

    public async Task SaveSettingsAsync(StudioSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WriteJsonAtomicAsync(SettingsPath, settings, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> CopyOwnedAssetAsync(string? relativePath, string newThemeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        var source = ResolveAssetPath(relativePath);
        return File.Exists(source) ? await ImportAssetAsync(newThemeId, source, cancellationToken) : null;
    }

    private async Task<string> CreateAvailableIdAsync(string candidate, CancellationToken cancellationToken)
    {
        candidate = string.IsNullOrWhiteSpace(candidate) ? "custom-theme" : candidate;
        var id = candidate;
        for (var suffix = 2; await GetAsync(id, cancellationToken) is not null; suffix++)
            id = $"{candidate}-{suffix}";
        return id;
    }

    private string GetThemePath(string id)
    {
        ValidateId(id);
        return Path.Combine(ThemesPath, $"{id}.json");
    }

    private async Task<ThemeDefinition?> ReadThemeAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ThemeDefinition>(stream, _json, cancellationToken);
    }

    private async Task<StudioSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<StudioSettings>(stream, _json, cancellationToken) ?? new StudioSettings();
    }

    private async Task ApplyBrandBadgeDefaultsAsync(CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(ThemesPath, "*.json"))
        {
            var theme = await ReadThemeAsync(path, cancellationToken);
            if (theme is null)
                continue;

            var branded = theme with
            {
                Layers = theme.Layers with { Badge = true },
                Badge = theme.Badge with
                {
                    AssetPath = ThemeBadge.DefaultAssetPath,
                    Position = "top-left",
                    Style = "icon"
                }
            };
            if (branded != theme)
                await WriteJsonAtomicAsync(path, branded, cancellationToken);
        }
    }

    private static void ValidateId(string id)
    {
        ThemeValidator.Validate(new ThemeDefinition { Id = id, Name = "validation" });
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await JsonSerializer.SerializeAsync(stream, value, _json, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, path, true);
    }

    private static string Slugify(string value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "custom-theme" : value.Trim().ToLowerInvariant();
        var chars = source.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length is >= 2 and <= 64 ? slug : $"custom-{Guid.NewGuid():N}"[..15];
    }
}
