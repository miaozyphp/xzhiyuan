using System.Text.Json;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Runtime;

public static class ThemeCompiler
{
    private const string RuntimeVersion = "1.0.3";

    public static CompiledTheme Compile(
        ThemeDefinition theme,
        string? mediaUrl,
        string? badgeUrl,
        CompatibilityReport report,
        IReadOnlyList<string>? objectUrls = null)
    {
        var suspended = report.Layers.Where(layer => !layer.Supported).Select(layer => layer.Layer).ToArray();
        var config = new
        {
            version = RuntimeVersion,
            themeId = theme.Id,
            objectUrls = objectUrls ?? [],
            mode = theme.Mode.ToString().ToLowerInvariant(),
            scheme = IsLight(theme.Palette.Canvas) ? "light" : "dark",
            palette = theme.Palette,
            surfaces = theme.Surfaces,
            media = new
            {
                kind = theme.Media.Kind.ToString().ToLowerInvariant(),
                url = mediaUrl,
                opacity = Math.Clamp(theme.Media.Opacity, 0, 1),
                blur = Math.Clamp(theme.Media.Blur, 0, 40),
                fit = theme.Media.Fit,
                position = theme.Media.Position,
                muted = true
            },
            badge = new
            {
                url = badgeUrl,
                text = theme.Badge.Text,
                position = theme.Badge.Position,
                style = theme.Badge.Style,
                size = theme.Badge.Size,
                offsetX = theme.Badge.OffsetX,
                offsetY = theme.Badge.OffsetY,
                radius = theme.Badge.Radius,
                opacity = theme.Badge.Opacity,
                backgroundOpacity = theme.Badge.BackgroundOpacity,
                borderOpacity = theme.Badge.BorderOpacity
            },
            layers = new
            {
                media = theme.Layers.Media,
                surfaces = theme.Layers.Surfaces,
                components = theme.Layers.Components,
                badge = theme.Layers.Badge,
                hero = theme.Mode == ThemeMode.Deep && theme.Layers.Hero && !suspended.Contains("hero"),
                suggestions = theme.Mode == ThemeMode.Deep && theme.Layers.Suggestions && !suspended.Contains("suggestions"),
                homeLayout = theme.Mode == ThemeMode.Deep && theme.Layers.HomeLayout && !suspended.Contains("homeLayout")
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new CompiledTheme(CreateScript(json), suspended, theme.Id);
    }

    public static string CreateRemoveScript() =>
        """
        (() => {
          const ownedElements = [...document.querySelectorAll('[data-theme-studio-owned="true"]')];
          const active = Boolean(window.__themeStudioRuntime || document.documentElement.dataset.themeStudio || ownedElements.length);
          if (!active) return true;
          try { window.__themeStudioRuntime?.dispose?.(); } catch {}
          for (const element of ownedElements) element.remove();
          const root = document.documentElement;
          for (const key of [
            '--ts-canvas','--ts-surface','--ts-elevated','--ts-text','--ts-muted','--ts-border','--ts-accent',
            '--ts-accent-text','--ts-success','--ts-warning','--ts-danger','--background','--foreground','--card',
            '--card-foreground','--popover','--popover-foreground','--primary','--primary-foreground','--secondary',
            '--secondary-foreground','--muted','--muted-foreground','--accent','--accent-foreground','--border',
            '--input','--ring','--destructive','--ts-media-opacity','--ts-media-blur','--ts-media-fit',
            '--ts-media-position','--ts-badge-size','--ts-badge-opacity','--ts-color-scheme','--ts-surface-opacity',
            '--ts-badge-radius','--ts-badge-background-opacity','--ts-badge-border-opacity','--ts-sidebar-opacity',
            '--ts-composer-opacity','--ts-bubble-opacity','--ts-surface-blur','--ts-surface-radius'
          ]) root.style.removeProperty(key);
          delete root.dataset.themeStudio;
          delete root.dataset.themeStudioDeep;
          delete root.dataset.themeStudioSurfaces;
          delete window.__themeStudioRuntime;
          return true;
        })()
        """;

    private static string CreateScript(string configJson) => $$"""
        (() => {
          const config = {{configJson}};
          window.__themeStudioRuntime?.dispose?.();
          const owned = [];
          const add = element => { element.dataset.themeStudioOwned = 'true'; owned.push(element); return element; };
          const root = document.documentElement;
          const p = config.palette;
          const properties = {
            '--ts-canvas': p.canvas, '--ts-surface': p.surface, '--ts-elevated': p.elevated,
            '--ts-text': p.text, '--ts-muted': p.mutedText, '--ts-border': p.border,
            '--ts-accent': p.accent, '--ts-accent-text': p.accentText,
            '--ts-success': p.success, '--ts-warning': p.warning, '--ts-danger': p.danger,
            '--background': p.canvas, '--foreground': p.text, '--card': p.surface,
            '--card-foreground': p.text, '--popover': p.elevated, '--popover-foreground': p.text,
            '--primary': p.accent, '--primary-foreground': p.accentText,
            '--secondary': p.elevated, '--secondary-foreground': p.text,
            '--muted': p.elevated, '--muted-foreground': p.mutedText,
            '--accent': p.elevated, '--accent-foreground': p.text,
            '--border': p.border, '--input': p.border, '--ring': p.accent,
            '--destructive': p.danger
          };
          for (const [key, value] of Object.entries(properties)) root.style.setProperty(key, value);
          root.dataset.themeStudio = config.themeId;

          const css = `
            :root[data-theme-studio] { color-scheme: var(--ts-color-scheme); }
            :root[data-theme-studio], :root[data-theme-studio] body { color: var(--ts-text) !important; background-color: var(--ts-canvas) !important; }
            :root[data-theme-studio] body { position: relative; }
            #theme-studio-media { position: fixed; inset: 0; z-index: 0; pointer-events: none; overflow: hidden; background: var(--ts-canvas); }
            #theme-studio-media::after { content: ''; position: absolute; inset: 0; background: color-mix(in srgb, var(--ts-canvas) 8%, transparent); }
            #theme-studio-media > img, #theme-studio-media > video { width: 100%; height: 100%; object-fit: var(--ts-media-fit); object-position: var(--ts-media-position); opacity: var(--ts-media-opacity); filter: blur(var(--ts-media-blur)); transform: scale(1.02); }
            :root[data-theme-studio] body > :not(#theme-studio-media):not(#theme-studio-badge) { position: relative; z-index: 1; }
            :root[data-theme-studio-surfaces='true'] aside.app-shell-left-panel { background-color: color-mix(in srgb, var(--ts-surface) calc(var(--ts-sidebar-opacity) * 100%), transparent) !important; backdrop-filter: none !important; }
            :root[data-theme-studio-surfaces='true'] main.main-surface { background-color: color-mix(in srgb, var(--ts-surface) calc(var(--ts-surface-opacity) * 100%), transparent) !important; backdrop-filter: none !important; }
            :root[data-theme-studio-surfaces='true'] main.main-surface [class~='bg-token-main-surface-primary'] { background-color: color-mix(in srgb, var(--ts-surface) calc(var(--ts-surface-opacity) * 100%), transparent) !important; }
            :root[data-theme-studio-surfaces='true'] [class~='composer-surface-chrome'] { background-color: color-mix(in srgb, var(--ts-elevated) calc(var(--ts-composer-opacity) * 100%), transparent) !important; border-color: color-mix(in srgb, var(--ts-border) 84%, transparent) !important; backdrop-filter: blur(var(--ts-surface-blur)); }
            :root[data-theme-studio] :where(input, textarea, [contenteditable='true']) { caret-color: var(--ts-accent) !important; }
            :root[data-theme-studio] :where(input, textarea, [contenteditable='true']) { border-color: color-mix(in srgb, var(--ts-border) 84%, transparent); }
            :root[data-theme-studio] :where([role='dialog'], [role='menu'], [role='listbox'], [data-radix-popper-content-wrapper] > *) { color: var(--ts-text); background-color: color-mix(in srgb, var(--ts-elevated) calc(var(--ts-surface-opacity) * 100%), transparent) !important; border-color: var(--ts-border) !important; border-radius: var(--ts-surface-radius); backdrop-filter: blur(var(--ts-surface-blur)); }
            :root[data-theme-studio] :where(button, [role='button']):focus-visible { outline: 2px solid var(--ts-accent) !important; outline-offset: 2px; }
            :root[data-theme-studio] :where(a, [aria-current='page'], [data-state='active']) { --ring: var(--ts-accent); }
            #theme-studio-badge { position: fixed; z-index: 2147483000; width: var(--ts-badge-size); height: var(--ts-badge-size); opacity: var(--ts-badge-opacity); pointer-events: none; display: grid; place-items: center; overflow: hidden; color: var(--ts-text); font: 700 11px/1 sans-serif; border: 1px solid color-mix(in srgb, var(--ts-text) calc(var(--ts-badge-border-opacity) * 100%), transparent); background: color-mix(in srgb, var(--ts-surface) calc(var(--ts-badge-background-opacity) * 100%), transparent); backdrop-filter: blur(10px); border-radius: var(--ts-badge-radius); box-shadow: 0 8px 24px #0005; }
            #theme-studio-badge img { width: 76%; height: 76%; object-fit: contain; }
            #theme-studio-badge[data-style='icon'] { overflow: visible; border-color: transparent; background: transparent; backdrop-filter: none; box-shadow: none; }
            #theme-studio-badge[data-style='icon'] img { width: 100%; height: 100%; filter: drop-shadow(0 1px 2px #000a); }
            #theme-studio-badge[data-style='outline'] { background: transparent; backdrop-filter: none; box-shadow: none; }
            #theme-studio-badge[data-style='outline'] img { width: 84%; height: 84%; }
            :root[data-theme-studio-deep='true'] :where([data-testid='home-hero'], [data-slot='home-hero'], main h1:first-of-type) { text-shadow: 0 2px 18px color-mix(in srgb, var(--ts-canvas) 80%, transparent); }
            :root[data-theme-studio-deep='true'] :where([data-testid*='suggest'], [data-slot*='suggest']) { background: color-mix(in srgb, var(--ts-surface) 82%, transparent) !important; border: 1px solid color-mix(in srgb, var(--ts-border) 82%, transparent) !important; backdrop-filter: blur(14px); }
            :root[data-theme-studio-deep='true'] :where([data-testid='home'], [data-slot='home']) { isolation: isolate; }
          `;
          const style = add(document.createElement('style'));
          style.id = 'theme-studio-style';
          style.textContent = css;
          document.head.append(style);

          root.style.setProperty('--ts-media-opacity', String(config.media.opacity));
          root.style.setProperty('--ts-media-blur', `${config.media.blur}px`);
          root.style.setProperty('--ts-media-fit', config.media.fit);
          root.style.setProperty('--ts-media-position', config.media.position);
          root.style.setProperty('--ts-badge-size', `${config.badge.size}px`);
          root.style.setProperty('--ts-badge-opacity', String(config.badge.opacity));
          root.style.setProperty('--ts-badge-radius', `${config.badge.radius}px`);
          root.style.setProperty('--ts-badge-background-opacity', String(config.badge.backgroundOpacity));
          root.style.setProperty('--ts-badge-border-opacity', String(config.badge.borderOpacity));
          root.style.setProperty('--ts-color-scheme', config.scheme);
          root.style.setProperty('--ts-surface-opacity', String(config.surfaces.opacity));
          root.style.setProperty('--ts-sidebar-opacity', String(config.surfaces.sidebarOpacity));
          root.style.setProperty('--ts-composer-opacity', String(config.surfaces.composerOpacity));
          root.style.setProperty('--ts-bubble-opacity', String(config.surfaces.bubbleOpacity));
          root.style.setProperty('--ts-surface-blur', `${config.surfaces.blur}px`);
          root.style.setProperty('--ts-surface-radius', `${config.surfaces.radius}px`);
          if (config.layers.surfaces) root.dataset.themeStudioSurfaces = 'true';
          if (config.mode === 'deep' && (config.layers.hero || config.layers.suggestions || config.layers.homeLayout)) root.dataset.themeStudioDeep = 'true';

          if (config.layers.media && config.media.url && config.media.kind !== 'none') {
            const mediaRoot = add(document.createElement('div'));
            mediaRoot.id = 'theme-studio-media';
            const media = document.createElement(config.media.kind === 'video' ? 'video' : 'img');
            media.src = config.media.url;
            media.setAttribute('aria-hidden', 'true');
            if (media.tagName === 'VIDEO') { media.autoplay = true; media.loop = true; media.muted = true; media.playsInline = true; }
            mediaRoot.append(media);
            document.body.prepend(mediaRoot);
          }

          if (config.layers.badge) {
            const badge = add(document.createElement('div'));
            badge.id = 'theme-studio-badge';
            badge.dataset.style = config.badge.style || 'icon';
            const position = config.badge.position || 'top-left';
            badge.style[position.includes('top') ? 'top' : 'bottom'] = `${config.badge.offsetY}px`;
            badge.style[position.includes('left') ? 'left' : 'right'] = `${config.badge.offsetX}px`;
            if (config.badge.url) { const img = document.createElement('img'); img.src = config.badge.url; badge.append(img); }
            else badge.textContent = (config.badge.text || 'TS').slice(0, 4);
            document.body.append(badge);
          }

          const dispose = () => {
            for (const element of document.querySelectorAll('[data-theme-studio-owned="true"]')) element.remove();
            for (const url of config.objectUrls || []) { try { URL.revokeObjectURL(url); } catch {} }
            for (const key of Object.keys(properties)) root.style.removeProperty(key);
            for (const key of ['--ts-media-opacity','--ts-media-blur','--ts-media-fit','--ts-media-position','--ts-badge-size','--ts-badge-opacity','--ts-badge-radius','--ts-badge-background-opacity','--ts-badge-border-opacity','--ts-color-scheme','--ts-surface-opacity','--ts-sidebar-opacity','--ts-composer-opacity','--ts-bubble-opacity','--ts-surface-blur','--ts-surface-radius']) root.style.removeProperty(key);
            delete root.dataset.themeStudio;
            delete root.dataset.themeStudioDeep;
            delete root.dataset.themeStudioSurfaces;
            delete window.__themeStudioRuntime;
          };
          window.__themeStudioRuntime = { version: config.version, themeId: config.themeId, dispose };
          return JSON.stringify({ ok: true, themeId: config.themeId });
        })()
        """;

    private static bool IsLight(string color)
    {
        if (color.Length < 7 || !int.TryParse(color.AsSpan(1, 6), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return false;
        var red = ((rgb >> 16) & 255) / 255d;
        var green = ((rgb >> 8) & 255) / 255d;
        var blue = (rgb & 255) / 255d;
        static double Linear(double value) => value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue) > 0.48;
    }
}
