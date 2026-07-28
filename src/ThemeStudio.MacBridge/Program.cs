using System.Text;
using System.Text.Json;
using ThemeStudio.Core.Storage;
using ThemeStudio.Core.Updates;
using ThemeStudio.MacBridge;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(false);

var arguments = ParseArguments(args);
var configuredRoot = arguments.GetValueOrDefault("data-root")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "xzhiyuan");
var resourcesRoot = arguments.GetValueOrDefault("resources-root") ?? AppContext.BaseDirectory;
var root = ThemeDataRoot.Resolve(configuredRoot);
using var log = new MacLog(Path.Combine(root, "theme-studio.log"));

try
{
    var repository = new ThemeRepository(root);
    var seedRoot = Path.Combine(resourcesRoot, "SeedAssets");
    await repository.InitializeAsync(
        Path.Combine(seedRoot, "rain-archive.png"),
        Path.Combine(seedRoot, "x-zhiyuan-emblem.png"));

    await using var runtime = new MacStudioRuntime(repository, log);
    runtime.Start();
    var updates = new ReleaseUpdateService(Path.Combine(root, "updates"), target: ReleaseUpdateTarget.MacOSArm64);
    var controller = new MacAppController(repository, runtime, updates);
    var protocol = new JsonLineProtocol();
    runtime.StatusChanged += (_, status) => protocol.Write(new { @event = "runtimeStatus", data = status });

    var activeRequests = new List<Task>();
    string? line;
    while ((line = await Console.In.ReadLineAsync()) is not null)
    {
        if (string.IsNullOrWhiteSpace(line))
            continue;
        activeRequests.RemoveAll(task => task.IsCompleted);
        activeRequests.Add(HandleRequestAsync(line, controller, protocol, log));
    }
    await Task.WhenAll(activeRequests);
}
catch (Exception error)
{
    log.Error("Mac bridge startup failed.", error);
    new JsonLineProtocol().Write(new { @event = "fatalError", data = new { message = "x纸鸢后端没有启动成功，请重新安装。" } });
    Environment.ExitCode = 2;
}

static async Task HandleRequestAsync(string line, MacAppController controller, JsonLineProtocol protocol, MacLog log)
{
    string? id = null;
    try
    {
        line = line.TrimStart('\uFEFF');
        using var message = JsonDocument.Parse(line);
        var root = message.RootElement;
        id = root.GetProperty("id").GetString();
        var method = root.GetProperty("method").GetString() ?? string.Empty;
        var parameters = root.TryGetProperty("params", out var value) ? value.Clone() : EmptyParameters();
        var progress = method == "downloadUpdate"
            ? new SynchronousProgress<AppUpdateProgress>(item => protocol.Write(new { @event = "updateProgress", data = item }))
            : null;
        var result = await controller.HandleAsync(method, parameters, progress);
        protocol.Write(new { id, ok = true, result, error = (string?)null });
    }
    catch (Exception error)
    {
        log.Error("Bridge request failed.", error);
        protocol.Write(new { id, ok = false, result = (object?)null, error = FriendlyMessage(error) });
    }
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < values.Length; index++)
    {
        if (!values[index].StartsWith("--", StringComparison.Ordinal))
            continue;
        result[values[index][2..]] = values[++index];
    }
    return result;
}

static JsonElement EmptyParameters() => JsonDocument.Parse("{}").RootElement.Clone();

static string FriendlyMessage(Exception error) => error switch
{
    InvalidDataException or InvalidOperationException or FileNotFoundException => error.Message,
    UnauthorizedAccessException => "没有权限读取这个文件，请换一个位置后重试。",
    _ => "操作没有完成，请稍后重试。Codex 和已保存主题不会受影响。"
};

sealed class JsonLineProtocol
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public void Write(object message)
    {
        lock (_gate)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(message, _json));
            Console.Out.Flush();
        }
    }
}
