using ThemeStudio.Core.Codex;
using ThemeStudio.Core.Models;
using ThemeStudio.Core.Runtime;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.App.Services;

public sealed class StudioRuntime : IAsyncDisposable
{
    private readonly ThemeRepository _repository;
    private readonly LocalLog _log;
    private readonly CodexInstallLocator _locator = new();
    private readonly CodexLauncher _launcher = new();
    private readonly CdpEndpointDiscovery _discovery = new();
    private readonly CdpThemeApplicator _applicator;
    private readonly CodexWindowChrome _windowChrome = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private LoopbackAssetServer? _assets;

    public StudioRuntime(ThemeRepository repository, LocalLog log)
    {
        _repository = repository;
        _log = log;
        _applicator = new CdpThemeApplicator(_discovery);
    }

    public RuntimeStatus Status { get; private set; } = new(RuntimeState.Idle, "正在准备主题工作台");
    public event EventHandler<RuntimeStatus>? StatusChanged;

    public void Start()
    {
        _assets = new LoopbackAssetServer(_repository);
        _assets.Start();
        _ = RefreshStatusAsync();
    }

    public string? GetAssetUrl(string? path) => _assets?.GetAssetUrl(path);

    public CodexInstallation? LocateCodex() => _locator.Locate();

    public async Task<RuntimeStatus> RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var installation = _locator.Locate();
            if (installation is null)
                return Update(new RuntimeStatus(RuntimeState.CodexNotFound, "未检测到 Codex，请先安装官方应用"));

            var settings = await _repository.GetSettingsAsync(cancellationToken);
            var targets = await _discovery.GetPageTargetsAsync(settings.DebugPort, cancellationToken);
            if (targets.Any(CdpThemeApplicator.IsPrimaryCodexTarget))
                return Update(new RuntimeStatus(RuntimeState.Idle, "Codex 已连接", installation.Version, Status.AppliedThemeId));

            var running = _launcher.FindRunning(installation).Count > 0;
            return Update(new RuntimeStatus(
                running ? RuntimeState.NativeOnly : RuntimeState.CodexStopped,
                running ? "Codex 已打开，等待主题连接" : "Codex 已就绪",
                installation.Version,
                Status.AppliedThemeId));
        }
        catch (Exception error)
        {
            _log.Error("Codex status refresh failed.", error);
            return Update(new RuntimeStatus(RuntimeState.Faulted, "暂时无法读取 Codex 状态"));
        }
    }

    public async Task<ThemeApplyResult> LaunchAndApplyAsync(string themeId, CancellationToken cancellationToken = default)
    {
        var theme = await _repository.GetAsync(themeId, cancellationToken);
        if (theme is null)
            return new ThemeApplyResult(false, "没有找到这个主题，请刷新主题图库。");
        return await LaunchAndApplyAsync(theme, false, cancellationToken);
    }

    public Task<ThemeApplyResult> LaunchAndApplyAsync(ThemeDefinition theme, CancellationToken cancellationToken = default) =>
        LaunchAndApplyAsync(theme, false, cancellationToken);

    public Task<ThemeApplyResult> RestartAndApplyAsync(ThemeDefinition theme, CancellationToken cancellationToken = default) =>
        LaunchAndApplyAsync(theme, true, cancellationToken);

    private async Task<ThemeApplyResult> LaunchAndApplyAsync(ThemeDefinition theme, bool restartExisting, CancellationToken cancellationToken)
    {
        await _operations.WaitAsync(cancellationToken);
        try
        {
            _log.Info($"Theme apply requested: {theme.Id}; restartExisting={restartExisting}.");
            var installation = _locator.Locate();
            if (installation is null)
            {
                Update(new RuntimeStatus(RuntimeState.CodexNotFound, "未检测到 Codex，请先安装官方应用"));
                return new ThemeApplyResult(false, "未检测到 Codex，请先安装官方应用。");
            }

            var settings = await _repository.GetSettingsAsync(cancellationToken);
            var effectiveTheme = ThemeSafetyProfile.Apply(theme, settings.SafeMode);
            var targets = await _discovery.GetPageTargetsAsync(settings.DebugPort, cancellationToken);
            if (targets.Count == 0)
            {
                var running = _launcher.FindRunning(installation);
                if (running.Count > 0)
                {
                    if (!restartExisting)
                    {
                        Update(new RuntimeStatus(RuntimeState.NativeOnly, "Codex 需要重新连接后才能应用主题", installation.Version));
                        return new ThemeApplyResult(false, "Codex 当前是普通启动，需要重新打开一次才能连接主题。", RequiresRestart: true);
                    }

                    Update(new RuntimeStatus(RuntimeState.Launching, "正在重新连接 Codex", installation.Version));
                    var stopped = await _launcher.StopForManagedRestartAsync(running, cancellationToken);
                    if (!stopped)
                    {
                        Update(new RuntimeStatus(RuntimeState.NativeOnly, "Codex 没有完全退出，已取消重新连接", installation.Version));
                        return new ThemeApplyResult(false, "Codex 没有完全退出。为了避免重复启动，本次没有继续应用皮肤。");
                    }
                    _log.Info("Codex exact-process restart stop completed.");
                }

                Update(new RuntimeStatus(RuntimeState.Launching, "正在打开 Codex", installation.Version));
                try
                {
                    _launcher.Launch(installation, settings.DebugPort);
                }
                catch (CodexLaunchException error)
                {
                    _log.Error("Managed Codex launch failed.", error);
                    var message = error.NativeFallbackStarted
                        ? "Codex 已恢复为普通启动，但当前无法连接皮肤。"
                        : "Codex 没有成功启动，请从开始菜单手动打开。";
                    Update(new RuntimeStatus(RuntimeState.NativeOnly, message, installation.Version));
                    return new ThemeApplyResult(false, message);
                }
            }

            Update(new RuntimeStatus(RuntimeState.Applying, "正在应用皮肤", installation.Version));
            var result = await _applicator.ApplyAsync(
                settings.DebugPort,
                effectiveTheme,
                ResolveAssetPath,
                TimeSpan.FromSeconds(20),
                cancellationToken: cancellationToken);

            if (result.Success)
            {
                var darkWindowChrome = !ThemeCompiler.UsesLightColorScheme(effectiveTheme.Palette.Canvas);
                var chromeWindows = _windowChrome.Apply(_launcher.FindRunning(installation), darkWindowChrome);
                _log.Info($"Codex window controls updated: dark={darkWindowChrome}; windows={chromeWindows}.");
            }

            Update(result.Success
                ? new RuntimeStatus(RuntimeState.Applied, result.Message, installation.Version, theme.Id)
                : new RuntimeStatus(RuntimeState.NativeOnly, result.Message, installation.Version));
            _log.Info($"Theme apply completed: success={result.Success}; suspended={string.Join(',', result.SuspendedLayers ?? [])}.");
            return result;
        }
        catch (Exception error)
        {
            _log.Error("Theme application failed.", error);
            Update(new RuntimeStatus(RuntimeState.Faulted, "皮肤应用失败，Codex 已保持原生界面"));
            return new ThemeApplyResult(false, "皮肤应用失败，Codex 已保持原生界面。请稍后重试。");
        }
        finally
        {
            _operations.Release();
        }
    }

    private string? ResolveAssetPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        var path = _repository.ResolveAssetPath(relativePath);
        return File.Exists(path) ? path : null;
    }

    public async Task<ThemeApplyResult> ApplyDefaultAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        return await LaunchAndApplyAsync(settings.DefaultThemeId, cancellationToken);
    }

    public async Task RemoveThemeAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _repository.GetSettingsAsync(cancellationToken);
        await _applicator.RemoveAsync(settings.DebugPort, cancellationToken);
        var installation = _locator.Locate();
        if (installation is not null)
            _windowChrome.Restore(_launcher.FindRunning(installation));
        Update(new RuntimeStatus(RuntimeState.Idle, "皮肤已卸下，Codex 保持运行"));
    }

    private RuntimeStatus Update(RuntimeStatus status)
    {
        Status = status;
        try
        {
            StatusChanged?.Invoke(this, status);
        }
        catch (Exception error)
        {
            _log.Error("Runtime status subscriber failed.", error);
        }
        return status;
    }

    public async ValueTask DisposeAsync()
    {
        if (_assets is not null)
            await _assets.DisposeAsync();
        _operations.Dispose();
    }
}
