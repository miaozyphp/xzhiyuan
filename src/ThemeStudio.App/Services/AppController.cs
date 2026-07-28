using System.Text.Json;
using ThemeStudio.Core.Models;
using ThemeStudio.Core.Storage;
using ThemeStudio.Core.Updates;

namespace ThemeStudio.App.Services;

public sealed class AppController(ThemeRepository repository, StudioRuntime runtime, ReleaseUpdateService updates)
{
    private readonly ThemeDiagnosticService diagnostics = new(repository);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<object?> HandleAsync(
        string method,
        JsonElement parameters,
        IProgress<AppUpdateProgress>? updateProgress = null,
        CancellationToken cancellationToken = default) => method switch
    {
        "bootstrap" => await CreateBootstrapAsync(cancellationToken),
        "refresh" => await CreateBootstrapAsync(cancellationToken),
        "saveTheme" => await SaveThemeAsync(parameters, cancellationToken),
        "createThemeCopy" => await CreateThemeCopyAsync(parameters, cancellationToken),
        "createThemeFromDroppedAsset" => await CreateThemeFromDroppedAssetAsync(parameters, cancellationToken),
        "duplicateTheme" => await DuplicateThemeAsync(parameters, cancellationToken),
        "deleteTheme" => await DeleteThemeAsync(parameters, cancellationToken),
        "deleteThemes" => await DeleteThemesAsync(parameters, cancellationToken),
        "setDefaultTheme" => await SetDefaultThemeAsync(parameters, cancellationToken),
        "setAutoApply" => await SetAutoApplyAsync(parameters, cancellationToken),
        "setSafeMode" => await SetSafeModeAsync(parameters, cancellationToken),
        "exportDiagnostics" => await ExportDiagnosticsAsync(cancellationToken),
        "applyTheme" => await ApplyThemeAsync(parameters, cancellationToken),
        "restartAndApply" => await RestartAndApplyAsync(parameters, cancellationToken),
        "launchCodex" => await runtime.ApplyDefaultAsync(cancellationToken),
        "removeTheme" => RemoveThemeAsync(cancellationToken),
        "checkUpdate" => await updates.CheckAsync(cancellationToken),
        "downloadUpdate" => await updates.DownloadAsync(updateProgress, cancellationToken),
        _ => throw new InvalidOperationException("不支持的工作台操作。")
    };

    public async Task<object> ImportAssetAsync(string themeId, string sourcePath, CancellationToken cancellationToken = default)
    {
        var relativePath = await repository.ImportAssetAsync(themeId, sourcePath, cancellationToken);
        return new { assetPath = relativePath, url = runtime.GetAssetUrl(relativePath) };
    }

    private async Task<object> CreateBootstrapAsync(CancellationToken cancellationToken)
    {
        var themes = await repository.GetAllAsync(cancellationToken);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        _ = runtime.RefreshStatusAsync(cancellationToken);
        return new
        {
            themes = themes.Select(theme => new
            {
                theme,
                mediaUrl = runtime.GetAssetUrl(theme.Media.AssetPath),
                badgeUrl = runtime.GetAssetUrl(theme.Badge.AssetPath)
            }),
            settings,
            status = runtime.Status,
            update = updates.Status
        };
    }

    private async Task<object> SaveThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var theme = parameters.GetProperty("theme").Deserialize<ThemeDefinition>(_json)
            ?? throw new InvalidDataException("主题配置为空。");
        var saved = await repository.SaveAsync(theme, cancellationToken);
        return new { theme = saved, mediaUrl = runtime.GetAssetUrl(saved.Media.AssetPath), badgeUrl = runtime.GetAssetUrl(saved.Badge.AssetPath) };
    }

    private async Task<object> DuplicateThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var sourceId = parameters.GetProperty("id").GetString() ?? throw new InvalidDataException("主题编号为空。");
        var name = parameters.GetProperty("name").GetString() ?? "主题副本";
        var theme = await repository.DuplicateAsync(sourceId, name, cancellationToken);
        return new { theme, mediaUrl = runtime.GetAssetUrl(theme.Media.AssetPath), badgeUrl = runtime.GetAssetUrl(theme.Badge.AssetPath) };
    }

    private async Task<object> CreateThemeCopyAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var source = parameters.GetProperty("theme").Deserialize<ThemeDefinition>(_json)
            ?? throw new InvalidDataException("主题配置为空。");
        var name = parameters.GetProperty("name").GetString() ?? $"{source.Name} 副本";
        var theme = await repository.CreateCopyAsync(source, name, cancellationToken);
        return new { theme, mediaUrl = runtime.GetAssetUrl(theme.Media.AssetPath), badgeUrl = runtime.GetAssetUrl(theme.Badge.AssetPath) };
    }

    private async Task<object> CreateThemeFromDroppedAssetAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var source = parameters.GetProperty("theme").Deserialize<ThemeDefinition>(_json)
            ?? throw new InvalidDataException("Theme configuration is empty.");
        var fileName = parameters.GetProperty("fileName").GetString() ?? "dropped-media";
        var dataUrl = parameters.GetProperty("dataUrl").GetString() ?? string.Empty;
        var separator = dataUrl.IndexOf(',');
        if (separator < 0 || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            !dataUrl[..separator].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Dropped media data is invalid.");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var maxDroppedMediaBytes = ThemeMediaPolicy.MaximumBytes(extension);
        var encodedLength = dataUrl.Length - separator - 1;
        if (encodedLength > ((maxDroppedMediaBytes + 2) / 3) * 4 + 4)
            throw new InvalidDataException($"媒体文件不能超过 {maxDroppedMediaBytes / 1024 / 1024} MB。");
        var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        ThemeMediaPolicy.ValidateLength(extension, bytes.Length);

        var mediaKind = extension is ".mp4" or ".webm" or ".mov" ? MediaKind.Video : MediaKind.Image;
        var name = parameters.TryGetProperty("name", out var nameValue)
            ? nameValue.GetString()
            : Path.GetFileNameWithoutExtension(fileName);
        var theme = await repository.CreateCopyWithMediaAsync(source, name ?? "新建主题", mediaKind, bytes, extension, cancellationToken);
        return new { theme, mediaUrl = runtime.GetAssetUrl(theme.Media.AssetPath), badgeUrl = runtime.GetAssetUrl(theme.Badge.AssetPath) };
    }

    private async Task<object> DeleteThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var id = parameters.GetProperty("id").GetString() ?? throw new InvalidDataException("主题编号为空。");
        await repository.DeleteAsync(id, cancellationToken);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        if (settings.DefaultThemeId == id)
            await repository.SaveSettingsAsync(settings with { DefaultThemeId = DefaultThemeCatalog.DefaultThemeId }, cancellationToken);
        return new { deleted = true };
    }

    private async Task<object> DeleteThemesAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var ids = ReadThemeIds(parameters);
        var deletedIds = new List<string>();
        var failedIds = new List<string>();
        foreach (var id in ids)
        {
            try
            {
                await repository.DeleteAsync(id, cancellationToken);
                deletedIds.Add(id);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                failedIds.Add(id);
            }
        }

        var settings = await repository.GetSettingsAsync(cancellationToken);
        if (deletedIds.Contains(settings.DefaultThemeId, StringComparer.Ordinal))
        {
            settings = settings with { DefaultThemeId = DefaultThemeCatalog.DefaultThemeId };
            await repository.SaveSettingsAsync(settings, cancellationToken);
        }

        return new { deletedIds, failedIds, defaultThemeId = settings.DefaultThemeId };
    }

    private async Task<object> SetDefaultThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var id = parameters.GetProperty("id").GetString() ?? throw new InvalidDataException("主题编号为空。");
        if (await repository.GetAsync(id, cancellationToken) is null)
            throw new FileNotFoundException("主题不存在。");
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var updated = settings with { DefaultThemeId = id };
        await repository.SaveSettingsAsync(updated, cancellationToken);
        return updated;
    }

    private static IReadOnlyList<string> ReadThemeIds(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("ids", out var values) || values.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("没有选择要删除的主题。");
        var ids = values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length is 0 or > 100)
            throw new InvalidDataException("一次最多删除 100 个自定义主题。");
        return ids!;
    }

    private async Task<object> SetAutoApplyAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var enabled = parameters.GetProperty("enabled").GetBoolean();
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var updated = settings with { BrokerEnabled = enabled, RestartUnmanagedCodex = false };
        await repository.SaveSettingsAsync(updated, cancellationToken);
        BrokerRegistration.SetEnabled(enabled);
        return updated;
    }

    private async Task<object> SetSafeModeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var enabled = parameters.GetProperty("enabled").GetBoolean();
        var settings = await repository.GetSettingsAsync(cancellationToken);
        var updated = settings with { SafeMode = enabled };
        await repository.SaveSettingsAsync(updated, cancellationToken);
        return updated;
    }

    private async Task<object> ExportDiagnosticsAsync(CancellationToken cancellationToken) =>
        new { path = await diagnostics.CreateAsync(runtime.Status, cancellationToken) };

    private async Task<object> ApplyThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.TryGetProperty("theme", out var value))
        {
            var theme = value.Deserialize<ThemeDefinition>(_json) ?? throw new InvalidDataException("主题配置为空。");
            return await runtime.LaunchAndApplyAsync(theme, cancellationToken);
        }
        var id = parameters.GetProperty("id").GetString() ?? throw new InvalidDataException("主题编号为空。");
        return await runtime.LaunchAndApplyAsync(id, cancellationToken);
    }

    private async Task<object> RestartAndApplyAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var theme = parameters.GetProperty("theme").Deserialize<ThemeDefinition>(_json)
            ?? throw new InvalidDataException("主题配置为空。");
        return await runtime.RestartAndApplyAsync(theme, cancellationToken);
    }

    private async Task<object> RemoveThemeAsync(CancellationToken cancellationToken)
    {
        await runtime.RemoveThemeAsync(cancellationToken);
        return new { removed = true };
    }
}
