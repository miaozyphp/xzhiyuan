using System.Runtime.InteropServices;
using System.Text.Json;
using ThemeStudio.Core.Models;
using ThemeStudio.Core.Storage;
using ThemeStudio.Core.Updates;

namespace ThemeStudio.MacBridge;

public sealed class MacAppController(ThemeRepository repository, MacStudioRuntime runtime, ReleaseUpdateService updates)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<object?> HandleAsync(
        string method,
        JsonElement parameters,
        IProgress<AppUpdateProgress>? updateProgress = null,
        CancellationToken cancellationToken = default) => method switch
    {
        "bootstrap" or "refresh" => await CreateBootstrapAsync(cancellationToken),
        "saveTheme" => await SaveThemeAsync(parameters, cancellationToken),
        "createThemeCopy" => await CreateThemeCopyAsync(parameters, cancellationToken),
        "createThemeFromDroppedAsset" => await CreateThemeFromDroppedAssetAsync(parameters, cancellationToken),
        "duplicateTheme" => await DuplicateThemeAsync(parameters, cancellationToken),
        "deleteTheme" => await DeleteThemeAsync(parameters, cancellationToken),
        "deleteThemes" => await DeleteThemesAsync(parameters, cancellationToken),
        "setDefaultTheme" => await SetDefaultThemeAsync(parameters, cancellationToken),
        "setAutoApply" => await SetAutoApplyAsync(parameters, cancellationToken),
        "applyTheme" => await ApplyThemeAsync(parameters, cancellationToken),
        "restartAndApply" => await RestartAndApplyAsync(parameters, cancellationToken),
        "launchCodex" => await runtime.ApplyDefaultAsync(cancellationToken),
        "removeTheme" => await RemoveThemeAsync(cancellationToken),
        "importAsset" => await ImportAssetAsync(parameters, cancellationToken),
        "checkUpdate" => await updates.CheckAsync(cancellationToken),
        "downloadUpdate" => await updates.DownloadAsync(updateProgress, cancellationToken),
        "installUpdate" => new { installerPath = await updates.GetVerifiedInstallerAsync(cancellationToken) },
        "brokerTick" => await BrokerTickAsync(cancellationToken),
        _ => throw new InvalidOperationException("不支持的工作台操作。")
    };

    private async Task<object> CreateBootstrapAsync(CancellationToken cancellationToken)
    {
        var themes = await repository.GetAllAsync(cancellationToken);
        var settings = await repository.GetSettingsAsync(cancellationToken);
        _ = runtime.RefreshStatusAsync(cancellationToken);
        return new
        {
            platform = "macos",
            architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
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

    private async Task<object> ImportAssetAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var themeId = parameters.GetProperty("themeId").GetString() ?? "custom-theme";
        var sourcePath = parameters.GetProperty("sourcePath").GetString() ?? throw new InvalidDataException("没有选择文件。");
        var relativePath = await repository.ImportAssetAsync(themeId, sourcePath, cancellationToken);
        return new { assetPath = relativePath, url = runtime.GetAssetUrl(relativePath) };
    }

    private async Task<object> SaveThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var theme = ReadTheme(parameters);
        var saved = await repository.SaveAsync(theme, cancellationToken);
        return ThemeResult(saved);
    }

    private async Task<object> DuplicateThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var sourceId = RequiredString(parameters, "id", "主题编号为空。");
        var name = parameters.TryGetProperty("name", out var value) ? value.GetString() : null;
        return ThemeResult(await repository.DuplicateAsync(sourceId, name ?? "主题副本", cancellationToken));
    }

    private async Task<object> CreateThemeCopyAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var source = ReadTheme(parameters);
        var name = parameters.TryGetProperty("name", out var value) ? value.GetString() : null;
        return ThemeResult(await repository.CreateCopyAsync(source, name ?? $"{source.Name} 副本", cancellationToken));
    }

    private async Task<object> CreateThemeFromDroppedAssetAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var source = ReadTheme(parameters);
        var fileName = RequiredString(parameters, "fileName", "媒体文件名为空。");
        var dataUrl = RequiredString(parameters, "dataUrl", "拖入的媒体数据为空。");
        var separator = dataUrl.IndexOf(',');
        if (separator < 0 || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            !dataUrl[..separator].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("拖入的媒体数据无效。");

        const int maxDroppedMediaBytes = 80 * 1024 * 1024;
        var encodedLength = dataUrl.Length - separator - 1;
        if (encodedLength > ((maxDroppedMediaBytes + 2) / 3) * 4 + 4)
            throw new InvalidDataException("单个媒体文件不能超过 80 MB。");
        var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        if (bytes.Length > maxDroppedMediaBytes)
            throw new InvalidDataException("单个媒体文件不能超过 80 MB。");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var kind = extension is ".mp4" or ".webm" or ".mov" ? MediaKind.Video : MediaKind.Image;
        var name = parameters.TryGetProperty("name", out var value) ? value.GetString() : Path.GetFileNameWithoutExtension(fileName);
        return ThemeResult(await repository.CreateCopyWithMediaAsync(source, name ?? "新建主题", kind, bytes, extension, cancellationToken));
    }

    private async Task<object> DeleteThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var id = RequiredString(parameters, "id", "主题编号为空。");
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
        var id = RequiredString(parameters, "id", "主题编号为空。");
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
        var updated = settings with { BrokerEnabled = enabled, RestartUnmanagedCodex = enabled };
        await repository.SaveSettingsAsync(updated, cancellationToken);
        return updated;
    }

    private async Task<object> ApplyThemeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.TryGetProperty("theme", out _))
            return await runtime.LaunchAndApplyAsync(ReadTheme(parameters), cancellationToken);
        return await runtime.LaunchAndApplyAsync(RequiredString(parameters, "id", "主题编号为空。"), cancellationToken);
    }

    private async Task<object> RestartAndApplyAsync(JsonElement parameters, CancellationToken cancellationToken) =>
        await runtime.RestartAndApplyAsync(ReadTheme(parameters), cancellationToken);

    private async Task<object> RemoveThemeAsync(CancellationToken cancellationToken)
    {
        await runtime.RemoveThemeAsync(cancellationToken);
        return new { removed = true };
    }

    private async Task<object> BrokerTickAsync(CancellationToken cancellationToken)
    {
        await runtime.BrokerTickAsync(cancellationToken);
        return new { completed = true };
    }

    private ThemeDefinition ReadTheme(JsonElement parameters) =>
        parameters.GetProperty("theme").Deserialize<ThemeDefinition>(_json)
        ?? throw new InvalidDataException("主题配置为空。");

    private object ThemeResult(ThemeDefinition theme) => new
    {
        theme,
        mediaUrl = runtime.GetAssetUrl(theme.Media.AssetPath),
        badgeUrl = runtime.GetAssetUrl(theme.Badge.AssetPath)
    };

    private static string RequiredString(JsonElement parameters, string name, string message) =>
        parameters.GetProperty(name).GetString() ?? throw new InvalidDataException(message);
}
