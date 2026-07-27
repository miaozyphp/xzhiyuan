using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Runtime;

public sealed record DomSnapshot(
    IReadOnlyDictionary<string, int> Matches,
    IReadOnlyList<string> GeometryWarnings,
    int ViewportWidth,
    int ViewportHeight);

public static class CompatibilityContract
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredSelectors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["hero"] = ["homeHero", "mainHeading"],
            ["suggestions"] = ["suggestions", "promptCards"],
            ["homeLayout"] = ["homeRoot", "mainLandmark"]
        };

    public static CompatibilityReport Evaluate(ThemeDefinition theme, DomSnapshot snapshot)
    {
        var results = new List<LayerCompatibility>();
        foreach (var (layer, alternatives) in RequiredSelectors)
        {
            var enabled = layer switch
            {
                "hero" => theme.Layers.Hero,
                "suggestions" => theme.Layers.Suggestions,
                "homeLayout" => theme.Layers.HomeLayout,
                _ => false
            };

            if (theme.Mode != ThemeMode.Deep || !enabled)
            {
                results.Add(new LayerCompatibility(layer, true));
                continue;
            }

            var found = alternatives.Any(key => snapshot.Matches.TryGetValue(key, out var count) && count > 0);
            results.Add(new LayerCompatibility(layer, found, found ? null : $"Codex DOM does not expose a compatible {layer} target."));
        }

        return new CompatibilityReport(true, results, snapshot.GeometryWarnings);
    }

    public static string CreateProbeScript() =>
        """
        (() => {
          const count = selector => { try { return document.querySelectorAll(selector).length; } catch { return 0; } };
          const warnings = [];
          for (const element of document.querySelectorAll('main, [role="main"], textarea, [contenteditable="true"]')) {
            const rect = element.getBoundingClientRect();
            if (rect.right > innerWidth + 2 || rect.bottom > innerHeight + 2 || rect.left < -2) {
              warnings.push(`overflow:${element.tagName.toLowerCase()}`);
            }
          }
          return JSON.stringify({
            matches: {
              homeHero: count('[data-testid="home-hero"], [data-slot="home-hero"]'),
              mainHeading: count('main h1, [role="main"] h1'),
              suggestions: count('[data-testid*="suggest"], [data-slot*="suggest"]'),
              promptCards: count('main button, [role="main"] button'),
              homeRoot: count('[data-testid="home"], [data-slot="home"]'),
              mainLandmark: count('main, [role="main"]')
            },
            geometryWarnings: [...new Set(warnings)].slice(0, 20),
            viewportWidth: innerWidth,
            viewportHeight: innerHeight
          });
        })()
        """;
}
