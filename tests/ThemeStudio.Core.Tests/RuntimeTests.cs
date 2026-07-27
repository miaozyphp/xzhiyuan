using ThemeStudio.Core.Models;
using ThemeStudio.Core.Runtime;
using ThemeStudio.Core.Storage;
using ThemeStudio.Core.Codex;

namespace ThemeStudio.Core.Tests;

public sealed class RuntimeTests
{
    [Fact]
    public void DeepCompatibilitySuspendsOnlyMissingLayers()
    {
        var theme = ThemeValidatorTests.CreateTheme() with { Mode = ThemeMode.Deep };
        var snapshot = new DomSnapshot(new Dictionary<string, int>(), [], 1440, 900);

        var report = CompatibilityContract.Evaluate(theme, snapshot);
        var compiled = ThemeCompiler.Compile(theme, null, null, report);

        Assert.True(report.StandardSupported);
        Assert.Equal(3, compiled.SuspendedLayers.Count);
        Assert.Equal(ThemeMode.Deep, theme.Mode);
        Assert.Contains("window.__themeStudioRuntime", compiled.Script);
        Assert.Contains("dispose", ThemeCompiler.CreateRemoveScript());
    }

    [Fact]
    public void StandardModeDoesNotRequireDeepTargets()
    {
        var theme = ThemeValidatorTests.CreateTheme() with { Mode = ThemeMode.Standard };
        var report = CompatibilityContract.Evaluate(theme, new DomSnapshot(new Dictionary<string, int>(), [], 1200, 800));

        Assert.All(report.Layers, layer => Assert.True(layer.Supported));
        Assert.Empty(ThemeCompiler.Compile(theme, null, null, report).SuspendedLayers);
    }

    [Fact]
    public void AppliesOnlyToThePrimaryCodexPage()
    {
        Assert.True(CdpThemeApplicator.IsPrimaryCodexTarget(
            new CdpTarget("1", "page", "Codex", "app://-/index.html", "ws://127.0.0.1/primary")));
        Assert.False(CdpThemeApplicator.IsPrimaryCodexTarget(
            new CdpTarget("2", "page", "Codex", "app://-/index.html?initialRoute=%2Favatar-overlay", "ws://127.0.0.1/avatar")));
        Assert.False(CdpThemeApplicator.IsPrimaryCodexTarget(
            new CdpTarget("3", "page", "X ZhiYuan Workbench", "http://127.0.0.1:9467/", "ws://127.0.0.1/studio")));
    }

    [Fact]
    public void RuntimeAssetsUseCspCompatibleBlobUrls()
    {
        var theme = ThemeValidatorTests.CreateTheme();
        var report = CompatibilityContract.Evaluate(theme, new DomSnapshot(new Dictionary<string, int>(), [], 1200, 800));
        var compiled = ThemeCompiler.Compile(theme, "blob:media", "blob:badge", report, ["blob:media", "blob:badge"]);

        Assert.Contains("URL.revokeObjectURL", compiled.Script);
        Assert.Contains("main.main-surface", compiled.Script);
        Assert.Contains("app-shell-left-panel", compiled.Script);
        Assert.Contains("composer-surface-chrome", compiled.Script);
        Assert.Contains("--ts-composer-opacity", compiled.Script);
        Assert.Contains("badge.dataset.style", compiled.Script);
        Assert.Contains("--ts-badge-background-opacity", compiled.Script);
        Assert.Contains("data-theme-studio-tone", compiled.Script);
        Assert.Contains("MutationObserver", compiled.Script);
        Assert.Contains("refreshText", compiled.Script);
        Assert.Contains("--color-text-primary", compiled.Script);
        Assert.Contains("preservedTonePattern", compiled.Script);
        Assert.Contains("textObserver.disconnect", compiled.Script);
        Assert.Contains("removeAttribute('data-theme-studio-tone')", ThemeCompiler.CreateRemoveScript());
        Assert.DoesNotContain("button, [role='button'], input", compiled.Script);
        Assert.Equal("video/mp4", CdpAssetTransport.GetContentType("wallpaper.mp4"));
        Assert.Equal("image/png", CdpAssetTransport.GetContentType("badge.png"));
    }

    [Fact]
    public void BrokerHasOneRestartBudgetPerSession()
    {
        var broker = new BrokerStateMachine(true);

        Assert.Equal(BrokerAction.RestartManagedOnce, broker.Observe(BrokerObservation.UnmanagedCodex));
        Assert.Equal(BrokerAction.LeaveNative, broker.Observe(BrokerObservation.UnmanagedCodex));
        broker.ResetSession();
        Assert.Equal(BrokerAction.RestartManagedOnce, broker.Observe(BrokerObservation.UnmanagedCodex));
    }

    [Fact]
    public void ManagedRestartNeverKillsAProcessTree()
    {
        Assert.False(CodexLauncher.KillEntireProcessTree);
    }

    [Fact]
    public async Task AssetServerSupportsByteRanges()
    {
        using var temp = new TempDirectory();
        var repository = new ThemeRepository(temp.Path);
        await repository.InitializeAsync();
        var source = System.IO.Path.Combine(temp.Path, "clip.mp4");
        await File.WriteAllBytesAsync(source, Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var relative = await repository.ImportAssetAsync("video-theme", source);
        await using var server = new LoopbackAssetServer(repository);
        server.Start();

        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, server.GetAssetUrl(relative));
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(4, 11);
        using var response = await client.SendAsync(request);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(System.Net.HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(Enumerable.Range(4, 8).Select(value => (byte)value), bytes);
    }
}
