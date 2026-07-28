using System.Text.Json;
using ThemeStudio.Core.Codex;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Runtime;

public sealed class CdpThemeApplicator(CdpEndpointDiscovery discovery)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CdpAssetTransport _assets = new();

    public async Task<ThemeApplyResult> ApplyAsync(
        int debugPort,
        ThemeDefinition theme,
        Func<string?, string?> assetPath,
        TimeSpan timeout,
        bool includeWindowControlsBackdrop = true,
        CancellationToken cancellationToken = default)
    {
        var targets = await discovery.WaitForPageTargetsAsync(debugPort, timeout, cancellationToken);
        if (targets.Count == 0)
            return new ThemeApplyResult(false, "Codex 已打开，但当前版本没有提供皮肤连接。Codex 将保持原生界面运行。");

        var primaryTargets = targets.Where(IsPrimaryCodexTarget).ToArray();
        if (primaryTargets.Length == 0)
            return new ThemeApplyResult(false, "已建立主题连接，但没有找到 Codex 主页面。Codex 将保持原生界面运行。");

        foreach (var auxiliary in targets.Where(target => !IsPrimaryCodexTarget(target)))
            await RemoveFromTargetAsync(auxiliary, cancellationToken);

        var applied = 0;
        CompatibilityReport? lastReport = null;
        IReadOnlyList<string> suspended = [];
        var failures = new List<string>();

        foreach (var target in primaryTargets)
        {
            await using var client = new CdpClient();
            var pendingObjectUrls = new List<string>();
            try
            {
                await client.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
                var snapshotJson = await client.EvaluateAsync(CompatibilityContract.CreateProbeScript(), cancellationToken);
                var snapshot = snapshotJson is null
                    ? new DomSnapshot(new Dictionary<string, int>(), [], 0, 0)
                    : JsonSerializer.Deserialize<DomSnapshot>(snapshotJson, _json) ?? new DomSnapshot(new Dictionary<string, int>(), [], 0, 0);
                lastReport = CompatibilityContract.Evaluate(theme, snapshot);
                var mediaUrl = await _assets.UploadAsync(client, assetPath(theme.Media.AssetPath), cancellationToken);
                if (!string.IsNullOrWhiteSpace(mediaUrl))
                    pendingObjectUrls.Add(mediaUrl);
                var badgeUrl = await _assets.UploadAsync(client, assetPath(theme.Badge.AssetPath), cancellationToken);
                if (!string.IsNullOrWhiteSpace(badgeUrl))
                    pendingObjectUrls.Add(badgeUrl);
                var objectUrls = pendingObjectUrls.ToArray();
                var compiled = ThemeCompiler.Compile(theme, mediaUrl, badgeUrl, lastReport, objectUrls, includeWindowControlsBackdrop);
                suspended = compiled.SuspendedLayers;
                await client.EvaluateAsync(compiled.Script, cancellationToken);
                pendingObjectUrls.Clear();
                applied++;
            }
            catch (Exception error)
            {
                if (pendingObjectUrls.Count > 0)
                {
                    var urlsJson = JsonSerializer.Serialize(pendingObjectUrls);
                    try
                    {
                        await client.EvaluateAsync($"for (const url of {urlsJson}) {{ try {{ URL.revokeObjectURL(url); }} catch {{}} }} true;", CancellationToken.None);
                    }
                    catch { }
                }
                failures.Add(error.Message);
            }
        }

        if (applied == 0)
            return new ThemeApplyResult(false, $"皮肤没有挂载成功，Codex 保持原生界面。{failures.FirstOrDefault()}", lastReport, suspended);

        var message = suspended.Count == 0
            ? "皮肤已应用。"
            : $"皮肤已应用；当前 Codex 暂停了 {string.Join("、", suspended)} 深度层。";
        return new ThemeApplyResult(true, message, lastReport, suspended);
    }

    public async Task RemoveAsync(int debugPort, CancellationToken cancellationToken = default)
    {
        var targets = await discovery.GetPageTargetsAsync(debugPort, cancellationToken);
        foreach (var target in targets)
            await RemoveFromTargetAsync(target, cancellationToken);
    }

    public static bool IsPrimaryCodexTarget(CdpTarget target) =>
        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(target.Title, "Codex", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(target.Url, "app://-/index.html", StringComparison.OrdinalIgnoreCase);

    private static async Task RemoveFromTargetAsync(CdpTarget target, CancellationToken cancellationToken)
    {
        try
        {
            await using var client = new CdpClient();
            await client.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken);
            await client.EvaluateAsync(ThemeCompiler.CreateRemoveScript(), cancellationToken);
        }
        catch
        {
            // Removing a theme is best effort and must never affect the Codex process.
        }
    }
}
