using System.Text.Json;
using ThemeStudio.Core.Codex;
using ThemeStudio.Core.Storage;

namespace ThemeStudio.Core.Runtime;

public sealed class CdpAssetTransport
{
    private const int ChunkSize = 128 * 1024;

    public async Task<string?> UploadAsync(CdpClient client, string? filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var info = new FileInfo(filePath);
        ThemeMediaPolicy.ValidateLength(info.Extension, info.Length);

        var transferId = $"asset_{Guid.NewGuid():N}";
        var idJson = JsonSerializer.Serialize(transferId);
        await client.EvaluateAsync($"window.__themeStudioTransfers ??= Object.create(null); window.__themeStudioTransfers[{idJson}] = {{ parts: [], bytes: 0 }}; true;", cancellationToken);

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
                await client.EvaluateAsync(
                    $$"""
                    (() => {
                      const transfer = window.__themeStudioTransfers[{{idJson}}];
                      if (!transfer) throw new Error('Theme media transfer was cancelled.');
                      const binary = atob({{chunkJson}});
                      const bytes = new Uint8Array(binary.length);
                      for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
                      transfer.parts.push(bytes);
                      transfer.bytes += bytes.length;
                      return true;
                    })()
                    """,
                    cancellationToken);
            }

            var mimeJson = JsonSerializer.Serialize(GetContentType(filePath));
            var result = await client.EvaluateAsync(
                $$"""
                (() => {
                  const transfer = window.__themeStudioTransfers[{{idJson}}];
                  delete window.__themeStudioTransfers[{{idJson}}];
                  if (!transfer) throw new Error('Theme media transfer was cancelled.');
                  return URL.createObjectURL(new Blob(transfer.parts, { type: {{mimeJson}} }));
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
