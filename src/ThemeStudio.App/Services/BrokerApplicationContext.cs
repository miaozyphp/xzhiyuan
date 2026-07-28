using ThemeStudio.Core.Codex;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.App.Services;

public sealed class BrokerApplicationContext : ApplicationContext
{
    private readonly StudioRuntime _runtime;
    private readonly ThemeRepository _repository;
    private readonly LocalLog _log;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CodexLauncher _launcher = new();
    private bool _busy;
    private bool _appliedForSession;
    private bool _nativeLaunchReported;

    public BrokerApplicationContext(StudioRuntime runtime, ThemeRepository repository, LocalLog log)
    {
        _runtime = runtime;
        _repository = repository;
        _log = log;
        _timer = new System.Windows.Forms.Timer { Interval = 1500 };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_busy)
            return;
        _busy = true;
        try
        {
            var settings = await _repository.GetSettingsAsync();
            if (!settings.BrokerEnabled)
            {
                ExitThread();
                return;
            }

            var installation = _runtime.LocateCodex();
            if (installation is null)
                return;
            var running = _launcher.FindRunning(installation);
            if (running.Count == 0)
            {
                _appliedForSession = false;
                _nativeLaunchReported = false;
                return;
            }

            var status = await _runtime.RefreshStatusAsync();
            if (status.State is ThemeStudio.Core.Models.RuntimeState.Idle or ThemeStudio.Core.Models.RuntimeState.Applied)
            {
                _nativeLaunchReported = false;
                if (!_appliedForSession)
                {
                    var result = await _runtime.ApplyDefaultAsync();
                    _appliedForSession = result.Success;
                }
                return;
            }

            if (!_nativeLaunchReported)
            {
                _nativeLaunchReported = true;
                _log.Info("Broker detected a native Codex launch and left it running unchanged.");
            }
        }
        catch (Exception error)
        {
            _log.Error("Broker tick failed.", error);
        }
        finally
        {
            _busy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _timer.Dispose();
        base.Dispose(disposing);
    }
}
