using System.Net;
using System.Net.Sockets;
using System.Text;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.Core.Runtime;

public sealed class LoopbackAssetServer(ThemeRepository repository) : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _token = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant();
    private TcpListener? _listener;
    private Task? _acceptLoop;

    public int Port { get; private set; }

    public void Start()
    {
        if (_listener is not null)
            return;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
    }

    public string? GetAssetUrl(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;
        var file = repository.ResolveAssetPath(relativePath);
        if (!File.Exists(file))
            return null;
        var escaped = string.Join('/', relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return $"http://127.0.0.1:{Port}/{_token}/{escaped}";
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 8192, true);
            var requestLine = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(requestLine))
                return;

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || (parts[0] != "GET" && parts[0] != "HEAD"))
            {
                await WriteErrorAsync(stream, 405, "Method Not Allowed", cancellationToken);
                return;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(cancellationToken)))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                    headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            var requestPath = parts[1].Split('?', 2)[0];
            var segments = requestPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2 || !CryptographicEquals(segments[0], _token))
            {
                await WriteErrorAsync(stream, 404, "Not Found", cancellationToken);
                return;
            }

            var relativePath = string.Join('/', segments.Skip(1).Select(Uri.UnescapeDataString));
            string filePath;
            try
            {
                filePath = repository.ResolveAssetPath(relativePath);
            }
            catch (InvalidDataException)
            {
                await WriteErrorAsync(stream, 404, "Not Found", cancellationToken);
                return;
            }

            if (!File.Exists(filePath))
            {
                await WriteErrorAsync(stream, 404, "Not Found", cancellationToken);
                return;
            }

            await SendFileAsync(stream, filePath, headers.GetValueOrDefault("Range"), parts[0] == "HEAD", cancellationToken);
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException)
        {
            // The client may close media requests at any time while switching themes.
        }
    }

    private static async Task SendFileAsync(Stream stream, string path, string? rangeHeader, bool headOnly, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        var start = 0L;
        var end = info.Length - 1;
        var partial = false;

        if (!string.IsNullOrWhiteSpace(rangeHeader) && rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            var range = rangeHeader[6..].Split('-', 2);
            if (long.TryParse(range[0], out var parsedStart))
                start = Math.Clamp(parsedStart, 0, Math.Max(0, info.Length - 1));
            if (range.Length > 1 && long.TryParse(range[1], out var parsedEnd))
                end = Math.Clamp(parsedEnd, start, Math.Max(start, info.Length - 1));
            partial = true;
        }

        var length = Math.Max(0, end - start + 1);
        var status = partial ? "206 Partial Content" : "200 OK";
        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append("\r\n")
            .Append("Content-Type: ").Append(GetContentType(path)).Append("\r\n")
            .Append("Content-Length: ").Append(length).Append("\r\n")
            .Append("Accept-Ranges: bytes\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Cache-Control: private, max-age=3600\r\n");
        if (partial)
            headers.Append("Content-Range: bytes ").Append(start).Append('-').Append(end).Append('/').Append(info.Length).Append("\r\n");
        headers.Append("Connection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers.ToString()), cancellationToken);
        if (headOnly || length == 0)
            return;

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        file.Position = start;
        var buffer = new byte[81920];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
                break;
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }
    }

    private static async Task WriteErrorAsync(Stream stream, int statusCode, string status, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(status);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        _ => "application/octet-stream"
    };

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch (OperationCanceledException) { }
        }
        _shutdown.Dispose();
    }
}
