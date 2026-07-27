using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Storage;

public static class DefaultThemeCatalog
{
    public const string DefaultThemeId = "rain-archive";

    public static IReadOnlyList<ThemeDefinition> Create() =>
    [
        Theme("rain-archive", "雨夜档案馆", "冷雨、黑石与温暖阅览灯。", ThemeMode.Deep,
            Palette("#0B1013", "#141B1F", "#1C272D", "#F1F4F3", "#A7B2B6", "#334047", "#35B8CE", "#061215", "#44B982", "#D6A64B", "#E66B62"),
            new ThemeMedia { Kind = MediaKind.Image, AssetPath = "assets/built-in/rain-archive.png", Opacity = 0.92 },
            new ThemeSurfaces { Opacity = 0.22, Blur = 6, SidebarOpacity = 0.18, ComposerOpacity = 0.72, BubbleOpacity = 0.82 },
            "1.0.4"),
        Theme("paper-sky", "纸上晴空", "清透纸白与天青标记。", ThemeMode.Standard,
            Palette("#EAF2F3", "#F8FBFA", "#FFFFFF", "#142023", "#66777C", "#C5D3D6", "#168DA3", "#FFFFFF", "#218C66", "#B47A15", "#C6534C")),
        Theme("mint-station", "薄荷车站", "低饱和薄荷与工业灰。", ThemeMode.Standard,
            Palette("#E8F1ED", "#F4F8F5", "#FFFFFF", "#17211D", "#63736A", "#C5D2CB", "#23866A", "#FFFFFF", "#248963", "#B07A21", "#C75B52")),
        Theme("morning-film", "晨光胶片", "微暖白、石墨字与胶片红。", ThemeMode.Standard,
            Palette("#F2F0EC", "#FBFAF7", "#FFFFFF", "#201F1D", "#716E68", "#D6D1C8", "#C94842", "#FFFFFF", "#39835C", "#B37925", "#C94842")),
        Theme("blueprint-air", "蓝图空气", "工程蓝与冷白工作面。", ThemeMode.Standard,
            Palette("#E9F0F5", "#F6F9FB", "#FFFFFF", "#14212B", "#667681", "#C7D4DD", "#2C78A0", "#FFFFFF", "#2B8765", "#AD792C", "#C75350")),
        Theme("sakura-draft", "樱色草稿", "灰粉强调，不牺牲文字对比度。", ThemeMode.Standard,
            Palette("#F3ECEE", "#FBF8F8", "#FFFFFF", "#291F22", "#78696E", "#DCCDD1", "#B84F70", "#FFFFFF", "#36825F", "#AA7528", "#C34E55")),
        Theme("amber-library", "琥珀图书馆", "深墨表面与琥珀灯火。", ThemeMode.Deep,
            Palette("#17130E", "#211A12", "#2A2117", "#F4EBDD", "#BDAE99", "#493B2A", "#D79A36", "#1B1207", "#65A77B", "#E1A747", "#DB6D5D")),
        Theme("sunset-terminal", "落日终端", "铁锈红、暖灰与终端绿。", ThemeMode.Standard,
            Palette("#1B1715", "#261F1C", "#302722", "#F3E9E2", "#B8A89E", "#514139", "#D66A46", "#1B0C07", "#68A978", "#DFA244", "#DF695E")),
        Theme("tea-house", "茶室演算", "茶绿、木色与柔和米白。", ThemeMode.Standard,
            Palette("#191A15", "#24251D", "#2E3025", "#EFF0E6", "#B3B5A5", "#484A39", "#9BA35D", "#111308", "#7AAC6B", "#D0A04E", "#D56D5C")),
        Theme("festival-lantern", "灯会余温", "暗红底色与克制金光。", ThemeMode.Deep,
            Palette("#1B1110", "#271817", "#32201E", "#F5E9E5", "#BCA5A0", "#503633", "#CE7850", "#190B07", "#6FAA77", "#E2A445", "#DE675E")),
        Theme("apricot-studio", "杏色工作室", "柔暖杏色与炭黑文本。", ThemeMode.Standard,
            Palette("#F3E8DE", "#FBF5EF", "#FFFFFF", "#2A211D", "#7A6B63", "#DDCABE", "#BF704B", "#FFFFFF", "#3E8762", "#AC752A", "#C5524E"))
    ];

    private static ThemeDefinition Theme(
        string id,
        string name,
        string description,
        ThemeMode mode,
        ThemePalette palette,
        ThemeMedia? media = null,
        ThemeSurfaces? surfaces = null,
        string version = "1.0.0") => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Version = version,
            Mode = mode,
            Palette = palette,
            Media = media ?? new ThemeMedia(),
            Surfaces = surfaces ?? new ThemeSurfaces(),
            BuiltIn = true,
            Badge = new ThemeBadge { Text = "X" }
        };

    private static ThemePalette Palette(
        string canvas, string surface, string elevated, string text, string muted,
        string border, string accent, string accentText, string success, string warning, string danger) => new()
        {
            Canvas = canvas,
            Surface = surface,
            Elevated = elevated,
            Text = text,
            MutedText = muted,
            Border = border,
            Accent = accent,
            AccentText = accentText,
            Success = success,
            Warning = warning,
            Danger = danger
        };
}
