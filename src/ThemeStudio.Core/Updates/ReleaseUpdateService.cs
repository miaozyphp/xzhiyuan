using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThemeStudio.Core.Updates;

public sealed record AppUpdateStatus(
    string State,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    bool ReadyToInstall,
    int Progress,
    string Message,
    string? ReleaseUrl = null,
    long InstallerSize = 0);

public sealed record AppUpdateProgress(int Percent, long ReceivedBytes, long TotalBytes);

public sealed class ReleaseUpdateService
{
    public const string ReleasesApi = "https://api.github.com/repos/miaozyphp/xzhiyuan/releases?per_page=20";

    private readonly string _downloadRoot;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusGate = new();
    private AvailableUpdate? _available;
    private string? _verifiedInstaller;
    private AppUpdateStatus _status;

    public ReleaseUpdateService(string downloadRoot, string? currentVersion = null, HttpClient? httpClient = null)
    {
        _downloadRoot = Path.GetFullPath(downloadRoot);
        CurrentVersion = NormalizeVersion(currentVersion ?? EntryVersion());
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("XZhiYuan", CurrentVersion));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _status = new AppUpdateStatus("idle", CurrentVersion, null, false, false, 0, "尚未检查更新");
    }

    public string CurrentVersion { get; }

    public AppUpdateStatus Status
    {
        get { lock (_statusGate) return _status; }
    }

    public async Task<AppUpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetStatus(new AppUpdateStatus("checking", CurrentVersion, null, false, false, 0, "正在检查更新"));
            using var response = await _http.GetAsync(ReleasesApi, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, JsonOptions, cancellationToken) ?? [];
            var current = ParseVersion(CurrentVersion);
            var latest = releases
                .Where(release => !release.Draft && TryParseVersion(release.TagName, out _))
                .Select(release => new { Release = release, Version = ParseVersion(release.TagName) })
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            if (latest is null || latest.Version <= current)
            {
                _available = null;
                _verifiedInstaller = null;
                return SetStatus(new AppUpdateStatus("current", CurrentVersion, latest?.Version.ToString(3), false, false, 100, "当前已是最新版本", latest?.Release.HtmlUrl));
            }

            var version = latest.Version.ToString(3);
            var expectedName = $"XZhiYuan-Setup-{version}-win-x64.exe";
            var installer = latest.Release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, expectedName, StringComparison.Ordinal));
            if (installer is null || !IsTrustedDownloadUrl(installer.BrowserDownloadUrl))
                throw new InvalidDataException("新版本没有可验证的 Windows 安装包。");

            var sha256 = ParseDigest(installer.Digest) ?? await ReadChecksumAsync(latest.Release, expectedName, cancellationToken);
            if (sha256 is null)
                throw new InvalidDataException("新版本没有提供有效的 SHA-256 校验值。");

            _available = new AvailableUpdate(version, latest.Release.HtmlUrl, installer.BrowserDownloadUrl, sha256, installer.Size);
            _verifiedInstaller = null;
            return SetStatus(new AppUpdateStatus("available", CurrentVersion, version, true, false, 0, $"发现新版本 {version}", latest.Release.HtmlUrl, installer.Size));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            SetStatus(new AppUpdateStatus("error", CurrentVersion, null, false, false, 0, "暂时无法检查更新"));
            throw new InvalidOperationException("暂时无法连接 GitHub 更新服务，请稍后重试。", error);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppUpdateStatus> DownloadAsync(IProgress<AppUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        string? partialPath = null;
        try
        {
            var update = _available ?? throw new InvalidOperationException("请先检查更新。");
            Directory.CreateDirectory(_downloadRoot);
            var installerPath = Path.Combine(_downloadRoot, $"XZhiYuan-Setup-{update.Version}-win-x64.exe");
            partialPath = installerPath + ".part";

            if (File.Exists(installerPath) && await VerifyFileAsync(installerPath, update.Sha256, cancellationToken))
            {
                _verifiedInstaller = installerPath;
                progress?.Report(new AppUpdateProgress(100, new FileInfo(installerPath).Length, update.Size));
                return SetStatus(new AppUpdateStatus("ready", CurrentVersion, update.Version, true, true, 100, "更新已下载并通过校验", update.ReleaseUrl, update.Size));
            }

            File.Delete(partialPath);
            SetStatus(new AppUpdateStatus("downloading", CurrentVersion, update.Version, true, false, 0, "正在下载更新", update.ReleaseUrl, update.Size));
            using var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long received = 0;
            var lastPercent = -1;
            while (true)
            {
                var count = await input.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                    break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                hash.AppendData(buffer, 0, count);
                received += count;
                var percent = total > 0 ? (int)Math.Clamp(received * 100 / total, 0, 99) : 0;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report(new AppUpdateProgress(percent, received, total));
                    SetStatus(new AppUpdateStatus("downloading", CurrentVersion, update.Version, true, false, percent, $"正在下载更新 {percent}%", update.ReleaseUrl, update.Size));
                }
            }
            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(update.Sha256)))
                throw new InvalidDataException("下载文件的 SHA-256 校验失败，已阻止安装。");
            if (update.Size > 0 && received != update.Size)
                throw new InvalidDataException("下载文件大小与 GitHub Release 不一致，已阻止安装。");

            File.Move(partialPath, installerPath, true);
            partialPath = null;
            _verifiedInstaller = installerPath;
            progress?.Report(new AppUpdateProgress(100, received, total));
            return SetStatus(new AppUpdateStatus("ready", CurrentVersion, update.Version, true, true, 100, "更新已下载并通过校验", update.ReleaseUrl, update.Size));
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            if (!string.IsNullOrWhiteSpace(partialPath))
                File.Delete(partialPath);
            var latest = _available?.Version;
            SetStatus(new AppUpdateStatus("error", CurrentVersion, latest, latest is not null, false, 0, error.Message, _available?.ReleaseUrl, _available?.Size ?? 0));
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> GetVerifiedInstallerAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var update = _available ?? throw new InvalidOperationException("没有待安装的新版本。");
            var path = _verifiedInstaller;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !await VerifyFileAsync(path, update.Sha256, cancellationToken))
                throw new InvalidDataException("安装包不存在或校验已失效，请重新下载。");
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Process LaunchInstaller(string installerPath)
    {
        var fullPath = Path.GetFullPath(installerPath);
        var prefix = _downloadRoot.EndsWith(Path.DirectorySeparatorChar) ? _downloadRoot : _downloadRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidOperationException("更新安装包不在受信任目录中。");

        return Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("更新安装程序没有启动。");
    }

    private async Task<string?> ReadChecksumAsync(GitHubRelease release, string installerName, CancellationToken cancellationToken)
    {
        var checksums = release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.Ordinal));
        if (checksums is null || !IsTrustedDownloadUrl(checksums.BrowserDownloadUrl))
            return null;
        var content = await _http.GetStringAsync(checksums.BrowserDownloadUrl, cancellationToken);
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[^1], installerName, StringComparison.Ordinal) && IsSha256(parts[0]))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    private AppUpdateStatus SetStatus(AppUpdateStatus status)
    {
        lock (_statusGate) _status = status;
        return status;
    }

    private static async Task<bool> VerifyFileAsync(string path, string expectedHash, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedHash));
    }

    private static bool IsTrustedDownloadUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith("/miaozyphp/xzhiyuan/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static string? ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) != true)
            return null;
        var value = digest[prefix.Length..];
        return IsSha256(value) ? value.ToLowerInvariant() : null;
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool TryParseVersion(string value, out Version version) =>
        Version.TryParse(NormalizeVersion(value), out version!);

    private static Version ParseVersion(string value) =>
        TryParseVersion(value, out var version) ? version : new Version();

    private static string NormalizeVersion(string value)
    {
        var normalized = value.Trim().TrimStart('v', 'V').Split('-', 2)[0];
        return Version.TryParse(normalized, out var version) ? version.ToString(3) : "0.0.0";
    }

    private static string EntryVersion() =>
        (Assembly.GetEntryAssembly()?.GetName().Version ?? Assembly.GetExecutingAssembly().GetName().Version ?? new Version()).ToString(3);

    private sealed record AvailableUpdate(string Version, string ReleaseUrl, string DownloadUrl, string Sha256, long Size);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
