using System.Text.RegularExpressions;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Storage;

public static partial class ThemeValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    public static void Validate(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (!IdPattern().IsMatch(theme.Id))
            throw new InvalidDataException("Theme id must contain 2-64 lowercase letters, numbers, or hyphens.");
        if (string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Length > 80)
            throw new InvalidDataException("Theme name must contain 1-80 characters.");

        foreach (var color in EnumerateColors(theme.Palette))
        {
            if (!ColorPattern().IsMatch(color))
                throw new InvalidDataException($"Invalid theme color: {color}");
        }

        if (theme.Media.Opacity is < 0 or > 1 || theme.Badge.Opacity is < 0 or > 1 ||
            theme.Badge.BackgroundOpacity is < 0 or > 1 || theme.Badge.BorderOpacity is < 0 or > 1)
            throw new InvalidDataException("Opacity must be between 0 and 1.");
        if (theme.Media.Blur is < 0 or > 40)
            throw new InvalidDataException("Blur must be between 0 and 40 pixels.");
        if (theme.Surfaces.Opacity is < 0.05 or > 1 || theme.Surfaces.SidebarOpacity is < 0.05 or > 1 ||
            theme.Surfaces.ComposerOpacity is < 0.2 or > 1 || theme.Surfaces.BubbleOpacity is < 0.2 or > 1)
            throw new InvalidDataException("Large-surface opacity must be between 0.05 and 1; compact surfaces must be between 0.2 and 1.");
        if (theme.Surfaces.Blur is < 0 or > 40 || theme.Surfaces.Radius is < 0 or > 20)
            throw new InvalidDataException("Surface blur or radius is outside the supported range.");
        if (theme.Badge.Size is < 16 or > 160)
            throw new InvalidDataException("Badge size must be between 16 and 160 pixels.");
        if (theme.Badge.OffsetX is < 0 or > 160 || theme.Badge.OffsetY is < 0 or > 160 || theme.Badge.Radius is < 0 or > 32)
            throw new InvalidDataException("Badge offsets or radius are outside the supported range.");
        if (theme.Badge.Style is not ("icon" or "glass" or "outline"))
            throw new InvalidDataException("Badge style must be icon, glass, or outline.");
        if (theme.Badge.Position is not ("top-left" or "top-right" or "bottom-left" or "bottom-right"))
            throw new InvalidDataException("Badge position is not supported.");

        ValidateRelativePath(theme.Media.AssetPath);
        ValidateRelativePath(theme.Badge.AssetPath);
    }

    public static void ValidateRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("Theme assets must use repository-relative paths.");
    }

    private static IEnumerable<string> EnumerateColors(ThemePalette palette)
    {
        yield return palette.Canvas;
        yield return palette.Surface;
        yield return palette.Elevated;
        yield return palette.Text;
        yield return palette.MutedText;
        yield return palette.Border;
        yield return palette.Accent;
        yield return palette.AccentText;
        yield return palette.Success;
        yield return palette.Warning;
        yield return palette.Danger;
    }
}
