using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThemeStudio.Core.Updates;

namespace ThemeStudio.Core.Tests;

public sealed class ReleaseUpdateServiceTests
{
    [Fact]
    public async Task DownloadsAndVerifiesNewerGitHubRelease()
    {
        using var temp = new TempDirectory();
        var installer = Encoding.UTF8.GetBytes("verified installer payload");
        var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        using var client = new HttpClient(new ReleaseHandler(installer, digest));
        var service = new ReleaseUpdateService(temp.Path, "0.1.0", client);

        var available = await service.CheckAsync();
        var progress = new List<AppUpdateProgress>();
        var ready = await service.DownloadAsync(new InlineProgress<AppUpdateProgress>(progress.Add));

        Assert.True(available.UpdateAvailable);
        Assert.Equal("9.9.9", available.LatestVersion);
        Assert.Equal("ready", ready.State);
        Assert.True(ready.ReadyToInstall);
        Assert.Equal(100, progress[^1].Percent);
        Assert.Single(Directory.GetFiles(temp.Path, "*.exe"));
        Assert.Equal(Path.GetFullPath(Directory.GetFiles(temp.Path, "*.exe")[0]), await service.GetVerifiedInstallerAsync());
    }

    [Fact]
    public async Task RejectsInstallerWhenReleaseDigestDoesNotMatch()
    {
        using var temp = new TempDirectory();
        var installer = Encoding.UTF8.GetBytes("tampered installer payload");
        var wrongDigest = new string('a', 64);
        using var client = new HttpClient(new ReleaseHandler(installer, wrongDigest));
        var service = new ReleaseUpdateService(temp.Path, "0.1.0", client);
        await service.CheckAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync());

        Assert.Empty(Directory.GetFiles(temp.Path));
        Assert.False(service.Status.ReadyToInstall);
    }

    [Fact]
    public async Task SelectsAppleSiliconDmgForMacUpdates()
    {
        using var temp = new TempDirectory();
        var installer = Encoding.UTF8.GetBytes("verified macOS package");
        var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        const string assetName = "XZhiYuan-Setup-9.9.9-macos-arm64.dmg";
        using var client = new HttpClient(new ReleaseHandler(installer, digest, assetName));
        var service = new ReleaseUpdateService(temp.Path, "0.1.0", client, ReleaseUpdateTarget.MacOSArm64);

        await service.CheckAsync();
        var ready = await service.DownloadAsync();

        Assert.True(ready.ReadyToInstall);
        Assert.EndsWith(assetName, await service.GetVerifiedInstallerAsync(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(temp.Path, "*.exe"));
    }

    [Fact]
    public async Task DoesNotDisplayAnOlderReleaseAsLatest()
    {
        using var temp = new TempDirectory();
        var installer = Encoding.UTF8.GetBytes("older package");
        var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        using var client = new HttpClient(new ReleaseHandler(installer, digest));
        var service = new ReleaseUpdateService(temp.Path, "10.0.0", client);

        var status = await service.CheckAsync();

        Assert.False(status.UpdateAvailable);
        Assert.Equal("10.0.0", status.LatestVersion);
    }

    private sealed class ReleaseHandler(
        byte[] installer,
        string digest,
        string assetName = "XZhiYuan-Setup-9.9.9-win-x64.exe") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == ReleaseUpdateService.ReleasesApi)
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        tag_name = "v9.9.9",
                        html_url = "https://github.com/miaozyphp/xzhiyuan/releases/tag/v9.9.9",
                        draft = false,
                        prerelease = true,
                        assets = new[]
                        {
                            new
                            {
                                name = assetName,
                                browser_download_url = $"https://github.com/miaozyphp/xzhiyuan/releases/download/v9.9.9/{assetName}",
                                size = installer.Length,
                                digest = $"sha256:{digest}"
                            }
                        }
                    }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsoluteUri.EndsWith(assetName, StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(installer)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
