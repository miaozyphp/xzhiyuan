using System.Text.Json.Serialization;

namespace ThemeStudio.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<RuntimeState>))]
public enum RuntimeState
{
    Idle,
    Locating,
    CodexNotFound,
    CodexStopped,
    Launching,
    WaitingForCdp,
    Applying,
    Applied,
    NativeOnly,
    Faulted
}

public sealed record LayerCompatibility(string Layer, bool Supported, string? Reason = null);

public sealed record CompatibilityReport(
    bool StandardSupported,
    IReadOnlyList<LayerCompatibility> Layers,
    IReadOnlyList<string> GeometryWarnings)
{
    public bool IsLayerSupported(string layer) =>
        Layers.FirstOrDefault(item => string.Equals(item.Layer, layer, StringComparison.OrdinalIgnoreCase))?.Supported ?? true;
}

public sealed record RuntimeStatus(
    RuntimeState State,
    string Message,
    string? CodexVersion = null,
    string? AppliedThemeId = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset UpdatedAt => Timestamp ?? DateTimeOffset.UtcNow;
}

public sealed record CompiledTheme(
    string Script,
    IReadOnlyList<string> SuspendedLayers,
    string ThemeId);

public sealed record ThemeApplyResult(
    bool Success,
    string Message,
    CompatibilityReport? Compatibility = null,
    IReadOnlyList<string>? SuspendedLayers = null,
    bool RequiresRestart = false);
