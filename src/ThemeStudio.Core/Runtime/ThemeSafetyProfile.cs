using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Runtime;

public static class ThemeSafetyProfile
{
    public static ThemeDefinition Apply(ThemeDefinition theme, bool safeMode)
    {
        if (!safeMode)
            return theme;

        return theme with
        {
            Mode = ThemeMode.Standard,
            Media = theme.Media.Kind == MediaKind.Video
                ? theme.Media with { Kind = MediaKind.None, AssetPath = null }
                : theme.Media,
            Layers = theme.Layers with
            {
                Components = false,
                Hero = false,
                Suggestions = false,
                HomeLayout = false
            }
        };
    }
}
