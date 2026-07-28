using System.IO.Compression;
using System.Text.Json;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Storage;

public sealed class ThemeDiagnosticService(ThemeRepository repository)
{
    public async Task<string> CreateAsync(RuntimeStatus status, CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(repository.RootPath, "diagnostics");
        Directory.CreateDirectory(folder);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var path = Path.Combine(folder, $"xzhiyuan-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{suffix}.zip");
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        await AddFileAsync(archive, repository.SettingsPath, "settings.json", cancellationToken);
        await AddFileAsync(archive, Path.Combine(repository.RootPath, "theme-studio.log"), "theme-studio.log", cancellationToken);
        await AddFileAsync(archive, Path.Combine(repository.RootPath, "theme-studio.previous.log"), "theme-studio.previous.log", cancellationToken);
        foreach (var theme in Directory.EnumerateFiles(repository.ThemesPath, "*.json"))
            await AddFileAsync(archive, theme, $"themes/{Path.GetFileName(theme)}", cancellationToken);

        var metadata = new
        {
            createdAt = DateTimeOffset.UtcNow,
            appVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                ?? typeof(ThemeDiagnosticService).Assembly.GetName().Version?.ToString(),
            os = Environment.OSVersion.VersionString,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            status
        };
        var entry = archive.CreateEntry("environment.json", CompressionLevel.Fastest);
        await using (var stream = entry.Open())
            await JsonSerializer.SerializeAsync(stream, metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, cancellationToken);
        return path;
    }

    private static async Task AddFileAsync(ZipArchive archive, string path, string entryName, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return;
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
        await using var destination = entry.Open();
        await source.CopyToAsync(destination, cancellationToken);
    }
}
