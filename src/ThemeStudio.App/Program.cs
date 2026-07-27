using ThemeStudio.App.Services;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var configuredRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThemeStudioForCodex");
        var root = ThemeDataRoot.Resolve(configuredRoot);

        using var log = new LocalLog(Path.Combine(root, "theme-studio.log"));
        try
        {
            log.Info("Application startup begin.");
            if (!string.Equals(configuredRoot, root, StringComparison.OrdinalIgnoreCase))
                log.Info($"Theme data directory resolved to redirected storage: {root}");
            var repository = new ThemeRepository(root);
            var seedRoot = Path.Combine(AppContext.BaseDirectory, "SeedAssets");
            repository.InitializeAsync(
                Path.Combine(seedRoot, "rain-archive.png"),
                Path.Combine(seedRoot, "x-zhiyuan-emblem.png")).GetAwaiter().GetResult();
            log.Info("Theme repository ready.");

            var runtime = new StudioRuntime(repository, log);
            runtime.Start();
            log.Info("Theme runtime ready.");

            if (args.Any(arg => string.Equals(arg, "--apply-once", StringComparison.OrdinalIgnoreCase)))
            {
                var result = runtime.ApplyDefaultAsync().GetAwaiter().GetResult();
                log.Info($"Apply-once completed: success={result.Success}; message={result.Message}");
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                Environment.ExitCode = result.Success ? 0 : 2;
                return;
            }

            if (args.Any(arg => string.Equals(arg, "--broker", StringComparison.OrdinalIgnoreCase)))
            {
                using var mutex = new Mutex(true, "Local\\ThemeStudioForCodex.Broker", out var firstInstance);
                if (firstInstance)
                    Application.Run(new BrokerApplicationContext(runtime, repository, log));
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }

            using var workbenchMutex = new Mutex(true, "Local\\ThemeStudioForCodex.Workbench", out var firstWorkbench);
            if (!firstWorkbench)
            {
                log.Info("Workbench is already running; restoring the existing window.");
                ExistingWindowActivator.Restore();
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }

            var controller = new AppController(repository, runtime);
            log.Info("Creating workbench window.");
            using var window = new MainWindow(controller, runtime, repository, log);
            log.Info("Workbench window created.");
            Application.Run(window);
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            log.Error("Application startup failed.", error);
            MessageBox.Show(
                "主题工作台未能启动。请重新安装，或查看本地日志。\r\n\r\n" + error.Message,
                "x纸鸢",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
