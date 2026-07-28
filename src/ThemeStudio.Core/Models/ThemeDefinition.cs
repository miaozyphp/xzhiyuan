using System.Text.Json.Serialization;

namespace ThemeStudio.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    Standard,
    Deep
}

[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
    None,
    Image,
    Video
}

public sealed record ThemePalette
{
    public string Canvas { get; init; } = "#101416";
    public string Surface { get; init; } = "#171D20";
    public string Elevated { get; init; } = "#20282C";
    public string Text { get; init; } = "#F4F6F6";
    public string MutedText { get; init; } = "#AAB4B8";
    public string Border { get; init; } = "#354146";
    public string Accent { get; init; } = "#2CB8D2";
    public string AccentText { get; init; } = "#071316";
    public string Success { get; init; } = "#42B883";
    public string Warning { get; init; } = "#E2A93B";
    public string Danger { get; init; } = "#E76F64";
}

public sealed record ThemeMedia
{
    public MediaKind Kind { get; init; } = MediaKind.None;
    public string? AssetPath { get; init; }
    public string? ContentHash { get; init; }
    public double Opacity { get; init; } = 0.62;
    public double Blur { get; init; }
    public string Fit { get; init; } = "cover";
    public string Position { get; init; } = "center";
    public bool Muted { get; init; } = true;
}

public sealed record ThemeLayers
{
    public bool Media { get; init; } = true;
    public bool Surfaces { get; init; } = true;
    public bool Components { get; init; } = true;
    public bool Badge { get; init; } = true;
    public bool Hero { get; init; } = true;
    public bool Suggestions { get; init; } = true;
    public bool HomeLayout { get; init; } = true;
}

public sealed record ThemeSurfaces
{
    public double Opacity { get; init; } = 0.88;
    public int Blur { get; init; } = 14;
    public int Radius { get; init; } = 6;
    public double SidebarOpacity { get; init; } = 0.9;
    public double ComposerOpacity { get; init; } = 0.94;
    public double BubbleOpacity { get; init; } = 0.92;
}

public sealed record ThemeBadge
{
    public const string DefaultAssetPath = "assets/built-in/theme-studio-emblem.png";

    public string? AssetPath { get; init; } = DefaultAssetPath;
    public string Text { get; init; } = "X";
    public string Position { get; init; } = "top-left";
    public string Style { get; init; } = "icon";
    public int Size { get; init; } = 24;
    public int OffsetX { get; init; } = 8;
    public int OffsetY { get; init; } = 6;
    public int Radius { get; init; } = 6;
    public double Opacity { get; init; } = 0.95;
    public double BackgroundOpacity { get; init; } = 0.82;
    public double BorderOpacity { get; init; } = 0.35;
}

public sealed record ThemeDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public bool BuiltIn { get; init; }
    public ThemeMode Mode { get; init; } = ThemeMode.Standard;
    public ThemePalette Palette { get; init; } = new();
    public ThemeMedia Media { get; init; } = new();
    public ThemeSurfaces Surfaces { get; init; } = new();
    public ThemeLayers Layers { get; init; } = new();
    public ThemeBadge Badge { get; init; } = new();
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record StudioSettings
{
    public string DefaultThemeId { get; init; } = "rain-archive";
    public bool BrokerEnabled { get; init; }
    public bool RestartUnmanagedCodex { get; init; }
    public bool SafeMode { get; init; }
    public int DebugPort { get; init; } = 9229;
    public int BadgeBrandingVersion { get; init; }
}
