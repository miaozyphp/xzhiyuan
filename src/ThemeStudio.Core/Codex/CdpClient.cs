using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ThemeStudio.Core.Codex;

public sealed record CdpTarget(string Id, string Type, string Title, string Url, string WebSocketDebuggerUrl);

public sealed class CdpEndpointDiscovery(HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<CdpTarget>> GetPageTargetsAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync($"http://127.0.0.1:{port}/json/list", cancellationToken);
            var targets = await JsonSerializer.DeserializeAsync<List<CdpTarget>>(stream, _json, cancellationToken) ?? [];
            return targets.Where(target => string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)).ToArray();
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<CdpTarget>> WaitForPageTargetsAsync(int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targets = await GetPageTargetsAsync(port, cancellationToken);
            if (targets.Count > 0)
                return targets;
            await Task.Delay(250, cancellationToken);
        }
        return [];
    }
}

public sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _commands = new(1, 1);
    private long _nextId;

    public async Task ConnectAsync(string webSocketUrl, CancellationToken cancellationToken = default)
    {
        await _socket.ConnectAsync(new Uri(webSocketUrl), cancellationToken);
    }

    public async Task<JsonDocument> ExecuteAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await _commands.WaitAsync(cancellationToken);
        try
        {
            var id = Interlocked.Increment(ref _nextId);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);

            while (true)
            {
                var document = await ReceiveDocumentAsync(cancellationToken);
                if (document.RootElement.TryGetProperty("id", out var responseId) && responseId.GetInt64() == id)
                {
                    if (document.RootElement.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var value) ? value.GetString() : error.ToString();
                        document.Dispose();
                        throw new InvalidOperationException($"CDP command failed: {message}");
                    }
                    return document;
                }
                document.Dispose();
            }
        }
        finally
        {
            _commands.Release();
        }
    }

    public async Task<string?> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        using var response = await ExecuteAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true,
            userGesture = false
        }, cancellationToken);

        var root = response.RootElement;
        var outer = root.GetProperty("result");
        if (outer.TryGetProperty("exceptionDetails", out var exception))
            throw new InvalidOperationException(exception.ToString());
        var remote = outer.GetProperty("result");
        if (!remote.TryGetProperty("value", out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
    }

    private async Task<JsonDocument> ReceiveDocumentAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];
        await using var stream = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("CDP connection closed unexpectedly.");
            await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(stream.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "X ZhiYuan complete", CancellationToken.None);
        _socket.Dispose();
        _commands.Dispose();
    }
}
