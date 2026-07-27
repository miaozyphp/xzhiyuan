using System.Text.Json;
using ThemeStudio.Core.Codex;

namespace ThemeStudio.Core.Runtime;

public sealed class CdpAssetTransport
{
    private const int ChunkSize = 256 * 1024;
    private const long MaximumAssetBytes = 256L * 1024 * 1024;

    public async Task<string?> UploadAsync(CdpClient client, string? filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var info = new FileInfo(filePath);
        if (info.Length > MaximumAssetBytes)
            throw new InvalidDataException("Theme media exceeds the 256 MB runtime limit.");

        var transferId = $"asset_{Guid.NewGuid():N}";
        var idJson = JsonSerializer.Serialize(transferId);
        await client.EvaluateAsync($"window.__themeStudioTransfers ??= Object.create(null); window.__themeStudioTransfers[{idJson}] = []; true;", cancellationToken);

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
            var buffer = new byte[ChunkSize];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                    break;
                var chunkJson = JsonSerializer.Serialize(Convert.ToBase64String(buffer, 0, read));
                await client.EvaluateAsync($"window.__themeStudioTransfers[{idJson}].push({chunkJson}); true;", cancellationToken);
            }

            var mimeJson = JsonSerializer.Serialize(GetContentType(filePath));
            var result = await client.EvaluateAsync(
                $$"""
                (() => {
                  const chunks = window.__themeStudioTransfers[{{idJson}}] || [];
                  const parts = chunks.map(chunk => {
                    const binary = atob(chunk);
                    const bytes = new Uint8Array(binary.length);
                    for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
                    return bytes;
                  });
                  delete window.__themeStudioTransfers[{{idJson}}];
                  return URL.createObjectURL(new Blob(parts, { type: {{mimeJson}} }));
                })()
                """,
                cancellationToken);
            return result;
        }
        catch
        {
            try { await client.EvaluateAsync($"delete window.__themeStudioTransfers?.[{idJson}]; true;", CancellationToken.None); }
            catch { }
            throw;
        }
    }

    public static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
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
}
