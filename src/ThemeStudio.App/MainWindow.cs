using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ThemeStudio.App.Services;
using ThemeStudio.Core.Models;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.App;

public sealed class MainWindow : Form
{
    private readonly AppController _controller;
    private readonly StudioRuntime _runtime;
    private readonly ThemeRepository _repository;
    private readonly LocalLog _log;
    private readonly WebView2 _webView;
    private readonly NotifyIcon _tray;
    private bool _webReady;
    private bool _exitRequested;

    public MainWindow(AppController controller, StudioRuntime runtime, ThemeRepository repository, LocalLog log)
    {
        _controller = controller;
        _runtime = runtime;
        _repository = repository;
        _log = log;
        _log.Info("Main window constructor begin.");
        Text = "x纸鸢";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 720);
        Size = new Size(1480, 900);
        BackColor = Color.FromArgb(8, 11, 13);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _log.Info("Creating WebView2 control.");
        _webView = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_webView);

        _tray = CreateTrayIcon();
        FormClosing += OnFormClosing;
        Shown += async (_, _) => await OnShownAsync();
        _runtime.StatusChanged += OnRuntimeStatusChanged;
        _log.Info("Main window constructor complete.");
    }

    private async Task OnShownAsync()
    {
        _tray.Visible = true;
        await InitializeWebViewAsync();
    }

    private NotifyIcon CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 x纸鸢", null, (_, _) => RestoreWindow());
        menu.Items.Add("启动 Codex", null, async (_, _) => await _runtime.ApplyDefaultAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出 x纸鸢", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });

        var tray = new NotifyIcon
        {
            Text = "x纸鸢",
            Icon = (Icon?)Icon?.Clone() ?? SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => RestoreWindow();
        return tray;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(_repository.RootPath, "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);

            var core = _webView.CoreWebView2;
            _webReady = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                "theme-studio.local",
                Path.Combine(AppContext.BaseDirectory, "ui"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnWebMessageReceived;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += (_, eventArgs) =>
            {
                _webReady = eventArgs.IsSuccess;
                if (!eventArgs.IsSuccess)
                    _log.Error($"Workbench navigation failed: {eventArgs.WebErrorStatus}");
            };
            _webView.DefaultBackgroundColor = Color.FromArgb(8, 11, 13);
            core.Navigate("https://theme-studio.local/index.html");
        }
        catch (Exception error)
        {
            _log.Error("WebView2 initialization failed.", error);
            ShowWebViewFailure(error.Message);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        string? id = null;
        try
        {
            using var message = JsonDocument.Parse(eventArgs.WebMessageAsJson);
            var root = message.RootElement;
            id = root.GetProperty("id").GetString();
            var method = root.GetProperty("method").GetString() ?? string.Empty;
            _log.Info($"Workbench request received: {method}.");
            var parameters = root.TryGetProperty("params", out var value) ? value.Clone() : EmptyParameters();

            object? result;
            if (method is "pickMedia" or "pickBadge")
            {
                var themeId = parameters.GetProperty("themeId").GetString() ?? "custom-theme";
                var source = PickAsset(method == "pickBadge");
                result = source is null ? new { cancelled = true } : await _controller.ImportAssetAsync(themeId, source);
            }
            else
            {
                result = await _controller.HandleAsync(method, parameters);
            }

            PostResponse(id, true, result, null);
            _log.Info($"Workbench request completed: {method}.");
        }
        catch (Exception error)
        {
            _log.Error("Workbench operation failed.", error);
            PostResponse(id, false, null, FriendlyMessage(error));
        }
    }

    private string? PickAsset(bool badgeOnly)
    {
        using var dialog = new OpenFileDialog
        {
            Title = badgeOnly ? "选择角标图片" : "选择背景图片或视频",
            Filter = badgeOnly
                ? "图片|*.png;*.jpg;*.jpeg;*.webp;*.gif"
                : "图片和视频|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.mp4;*.webm;*.mov|图片|*.png;*.jpg;*.jpeg;*.webp;*.gif|视频|*.mp4;*.webm;*.mov",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void OnRuntimeStatusChanged(object? sender, RuntimeStatus status)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try
        {
            BeginInvoke(() => PostEvent("runtimeStatus", status));
        }
        catch (InvalidOperationException)
        {
            // The window closed between the handle check and the dispatch.
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (Uri.TryCreate(eventArgs.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
            catch (Exception error) { _log.Error("External link could not be opened.", error); }
        }
    }

    private void PostResponse(string? id, bool ok, object? result, string? error)
    {
        if (!_webReady || _webView.CoreWebView2 is null)
            return;
        var json = JsonSerializer.Serialize(new { id, ok, result, error }, JsonOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void PostEvent(string name, object data)
    {
        if (!_webReady || _webView.CoreWebView2 is null)
            return;
        _webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { @event = name, data }, JsonOptions));
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_exitRequested && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            Hide();
            _tray.Visible = true;
        }
    }

    private void ShowWebViewFailure(string details)
    {
        Controls.Clear();
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(8, 11, 13),
            Text = "主题工作台界面没有加载成功。\r\n请安装或修复 Microsoft Edge WebView2 Runtime 后重试。\r\n\r\n" + details
        };
        Controls.Add(label);
    }

    private static JsonElement EmptyParameters() => JsonDocument.Parse("{}").RootElement.Clone();

    private static string FriendlyMessage(Exception error) => error switch
    {
        InvalidDataException => error.Message,
        InvalidOperationException => error.Message,
        FileNotFoundException => error.Message,
        UnauthorizedAccessException => "没有权限读取这个文件，请换一个位置后重试。",
        _ => "操作没有完成，请稍后重试。Codex 和已保存主题都不会受影响。"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _runtime.StatusChanged -= OnRuntimeStatusChanged;
            _tray.Visible = false;
            _tray.Dispose();
            _webView.Dispose();
        }
        base.Dispose(disposing);
    }
}
