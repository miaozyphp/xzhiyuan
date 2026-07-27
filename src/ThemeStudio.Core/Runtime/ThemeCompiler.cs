using System.Text.Json;
using ThemeStudio.Core.Models;

namespace ThemeStudio.Core.Runtime;

public static class ThemeCompiler
{
    private const string RuntimeVersion = "1.0.7";

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
            scheme = UsesLightColorScheme(theme.Palette.Canvas) ? "light" : "dark",
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
          const tonedElements = [...document.querySelectorAll('[data-theme-studio-tone]')];
          const active = Boolean(window.__themeStudioRuntime || document.documentElement.dataset.themeStudio || ownedElements.length || tonedElements.length);
          if (!active) return true;
          try { window.__themeStudioRuntime?.dispose?.(); } catch {}
          for (const element of ownedElements) element.remove();
          for (const element of tonedElements) {
            element.removeAttribute('data-theme-studio-tone');
            element.style.removeProperty('--ts-node-text');
            element.style.removeProperty('--ts-node-shadow');
            element.style.removeProperty('--ts-node-outline');
          }
          const root = document.documentElement;
          for (const key of [
            '--ts-canvas','--ts-surface','--ts-elevated','--ts-text','--ts-muted','--ts-border','--ts-accent',
            '--ts-accent-text','--ts-success','--ts-warning','--ts-danger','--background','--foreground','--card',
            '--card-foreground','--popover','--popover-foreground','--primary','--primary-foreground','--secondary',
            '--secondary-foreground','--muted','--muted-foreground','--accent','--accent-foreground','--border',
            '--input','--ring','--destructive','--ts-media-opacity','--ts-media-blur','--ts-media-fit',
            '--ts-media-position','--ts-badge-size','--ts-badge-opacity','--ts-color-scheme','--ts-surface-opacity',
            '--ts-badge-radius','--ts-badge-background-opacity','--ts-badge-border-opacity','--ts-sidebar-opacity',
            '--ts-composer-opacity','--ts-bubble-opacity','--ts-surface-blur','--ts-surface-radius','--ts-text-shadow',
            '--ts-text-outline','--ts-readable-muted',
            '--color-text-primary','--color-text-secondary','--color-text-tertiary','--text-primary','--text-secondary',
            '--token-text-primary','--token-text-secondary','--token-text-tertiary','--sidebar-foreground',
            '--sidebar-accent-foreground','--input-foreground'
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
            '--destructive': p.danger,
            '--color-text-primary': p.text, '--color-text-secondary': p.mutedText,
            '--color-text-tertiary': p.mutedText, '--text-primary': p.text,
            '--text-secondary': p.mutedText, '--token-text-primary': p.text,
            '--token-text-secondary': p.mutedText, '--token-text-tertiary': p.mutedText,
            '--sidebar-foreground': p.text, '--sidebar-accent-foreground': p.text,
            '--input-foreground': p.text
          };
          for (const [key, value] of Object.entries(properties)) root.style.setProperty(key, value);
          root.dataset.themeStudio = config.themeId;

          const css = `
            :root[data-theme-studio] { color-scheme: var(--ts-color-scheme); }
            :root[data-theme-studio], :root[data-theme-studio] body { color: var(--ts-text) !important; background-color: var(--ts-canvas) !important; }
            :root[data-theme-studio] body { position: relative; }
            :root[data-theme-studio] [data-theme-studio-tone='primary'] { color: var(--ts-text) !important; text-shadow: var(--ts-text-shadow); -webkit-text-stroke: .12px var(--ts-text-outline); }
            :root[data-theme-studio] [data-theme-studio-tone='muted'] { color: var(--ts-readable-muted) !important; text-shadow: var(--ts-text-shadow); -webkit-text-stroke: .12px var(--ts-text-outline); }
            :root[data-theme-studio] [data-theme-studio-tone='adaptive'] { color: var(--ts-node-text) !important; text-shadow: var(--ts-node-shadow); -webkit-text-stroke: .12px var(--ts-node-outline); }
            :root[data-theme-studio] [data-theme-studio-tone] { text-rendering: optimizeLegibility; }
            :root[data-theme-studio] [data-theme-studio-tone]:where(button, [role='button'], [role='menuitem'], [role='tab']):not(:disabled):not([aria-disabled='true']) { opacity: 1 !important; }
            :root[data-theme-studio] [data-theme-studio-tone] > svg { color: inherit !important; }
            :root[data-theme-studio] :where(input, textarea, [contenteditable='true'])::placeholder { color: inherit !important; opacity: .72; }
            :root[data-theme-studio] :where([data-placeholder], [aria-placeholder], .placeholder)::before,
            :root[data-theme-studio] :where([data-placeholder], [aria-placeholder], .placeholder)::after { color: var(--ts-readable-muted) !important; opacity: .82 !important; text-shadow: var(--ts-text-shadow); -webkit-text-stroke: .12px var(--ts-text-outline); }
            #theme-studio-media { position: fixed; inset: 0; z-index: 0; pointer-events: none; overflow: hidden; background: var(--ts-canvas); }
            #theme-studio-media::after { content: ''; position: absolute; inset: 0; background: color-mix(in srgb, var(--ts-canvas) 14%, transparent); }
            #theme-studio-media > img, #theme-studio-media > video { width: 100%; height: 100%; object-fit: var(--ts-media-fit); object-position: var(--ts-media-position); opacity: var(--ts-media-opacity); filter: blur(var(--ts-media-blur)); transform: scale(1.02); }
            #theme-studio-window-controls-backdrop { position: fixed; top: 0; right: 0; z-index: 2147482900; width: 144px; height: 32px; pointer-events: none; background: rgb(128 128 128 / 94%); }
            :root[data-theme-studio] body > #root { position: relative; z-index: 1; }
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
          root.style.setProperty('--ts-readable-muted', `color-mix(in srgb, ${p.mutedText} 62%, ${p.text})`);
          root.style.setProperty('--ts-text-outline', config.scheme === 'dark' ? 'rgb(0 0 0 / 92%)' : 'rgb(255 255 255 / 94%)');
          root.style.setProperty('--ts-text-shadow', config.scheme === 'dark'
            ? '0 1px 2px rgb(0 0 0 / 96%), 1px 0 1px rgb(0 0 0 / 76%), -1px 0 1px rgb(0 0 0 / 76%), 0 -1px 1px rgb(0 0 0 / 72%), 0 0 10px rgb(0 0 0 / 58%)'
            : '0 1px 2px rgb(255 255 255 / 98%), 1px 0 1px rgb(255 255 255 / 82%), -1px 0 1px rgb(255 255 255 / 82%), 0 -1px 1px rgb(255 255 255 / 78%), 0 0 9px rgb(255 255 255 / 62%)');
          if (config.layers.surfaces) root.dataset.themeStudioSurfaces = 'true';
          if (config.mode === 'deep' && (config.layers.hero || config.layers.suggestions || config.layers.homeLayout)) root.dataset.themeStudioDeep = 'true';

          const toneAttribute = 'data-theme-studio-tone';
          const textControlSelector = "input, textarea, select, [contenteditable='true']";
          const interactiveControlSelector = "button, a[href], [role='button'], [role='menuitem'], [role='tab'], [role='option'], [aria-label]";
          const ignoredTextSelector = "#theme-studio-media, #theme-studio-badge, [data-theme-studio-owned='true'], script, style, noscript, svg, canvas, pre, code, kbd, samp, .monaco-editor, .cm-editor, [class*='syntax'], [class*='terminal']";
          const preservedTonePattern = /(?:^|[\s:_-])(?:success|warning|danger|destructive|error)(?:$|[\s:_-])|text-(?:red|green|emerald|yellow|amber|orange|blue|cyan|teal|violet|purple|pink|rose)-/i;
          const mutedTonePattern = /muted|secondary|tertiary|subtle|description|caption|placeholder|timestamp|metadata|hint|disabled/i;
          const themeText = parseColor(p.text);
          const themeMuted = mixColors(parseColor(p.mutedText), themeText, .38);
          const accentText = parseColor(p.accentText);
          const canvasText = parseColor(p.canvas);
          const lightFallback = parseColor('#F8FAFB');
          const darkFallback = parseColor('#0A0F12');

          function parseColor(value) {
            if (!value) return null;
            const hex = value.match(/^#([0-9a-f]{6})([0-9a-f]{2})?$/i);
            if (hex) {
              const packed = Number.parseInt(hex[1], 16);
              return { r: (packed >> 16) & 255, g: (packed >> 8) & 255, b: packed & 255, a: hex[2] ? Number.parseInt(hex[2], 16) / 255 : 1 };
            }
            const rgb = value.match(/^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:\s*[,\/]\s*([\d.]+)(%)?)?\s*\)$/i);
            if (rgb) return { r: Number(rgb[1]), g: Number(rgb[2]), b: Number(rgb[3]), a: rgb[4] === undefined ? 1 : Number(rgb[4]) / (rgb[5] ? 100 : 1) };
            const srgb = value.match(/^color\(srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)(?:\s*\/\s*([\d.]+))?\)$/i);
            return srgb ? { r: Number(srgb[1]) * 255, g: Number(srgb[2]) * 255, b: Number(srgb[3]) * 255, a: srgb[4] === undefined ? 1 : Number(srgb[4]) } : null;
          }

          function luminance(color) {
            const channel = value => { value /= 255; return value <= .03928 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4; };
            return .2126 * channel(color.r) + .7152 * channel(color.g) + .0722 * channel(color.b);
          }

          function contrast(left, right) {
            if (!left || !right) return 0;
            const high = Math.max(luminance(left), luminance(right));
            const low = Math.min(luminance(left), luminance(right));
            return (high + .05) / (low + .05);
          }

          function mixColors(left, right, rightAmount) {
            if (!left) return right;
            if (!right) return left;
            return {
              r: left.r * (1 - rightAmount) + right.r * rightAmount,
              g: left.g * (1 - rightAmount) + right.g * rightAmount,
              b: left.b * (1 - rightAmount) + right.b * rightAmount,
              a: 1
            };
          }

          function composite(foreground, background) {
            if (!foreground) return background;
            if (!background || foreground.a >= .999) return { ...foreground, a: 1 };
            const alpha = foreground.a + background.a * (1 - foreground.a);
            return {
              r: (foreground.r * foreground.a + background.r * background.a * (1 - foreground.a)) / alpha,
              g: (foreground.g * foreground.a + background.g * background.a * (1 - foreground.a)) / alpha,
              b: (foreground.b * foreground.a + background.b * background.a * (1 - foreground.a)) / alpha,
              a: alpha
            };
          }

          function nearestBackground(element) {
            const layers = [];
            for (let current = element; current; current = current.parentElement) {
              const color = parseColor(getComputedStyle(current).backgroundColor);
              if (color?.a > .01) layers.push(color);
            }
            let background = canvasText;
            for (let index = layers.length - 1; index >= 0; index--) background = composite(layers[index], background);
            return background;
          }

          function formatColor(color) {
            return `rgb(${Math.round(color.r)} ${Math.round(color.g)} ${Math.round(color.b)})`;
          }

          function bestReadableColor(background, preferred) {
            return [preferred, accentText, canvasText, lightFallback, darkFallback]
              .filter(Boolean)
              .map(color => ({ color, score: contrast(color, background) }))
              .sort((left, right) => right.score - left.score)[0];
          }

          function clearTextTheme(element) {
            element.removeAttribute(toneAttribute);
            element.style.removeProperty('--ts-node-text');
            element.style.removeProperty('--ts-node-shadow');
            element.style.removeProperty('--ts-node-outline');
          }

          function signature(element) {
            return [element.getAttribute('class'), element.getAttribute('data-slot'), element.getAttribute('data-testid'), element.getAttribute('data-variant'), element.getAttribute('role')].filter(Boolean).join(' ');
          }

          function hasPatternInAncestors(element, pattern, depth = 3) {
            for (let current = element, level = 0; current && level < depth; current = current.parentElement, level++) {
              if (pattern.test(signature(current))) return true;
            }
            return false;
          }

          function hasOwnText(element) {
            if (element.matches(textControlSelector) || element.matches(interactiveControlSelector)) return true;
            return [...element.childNodes].some(node => node.nodeType === Node.TEXT_NODE && node.textContent?.trim());
          }

          function classifyText(element) {
            if (!(element instanceof HTMLElement)) return;
            if (element.matches(ignoredTextSelector) || element.closest(ignoredTextSelector)) {
              clearTextTheme(element);
              return;
            }
            if (!hasOwnText(element)) {
              clearTextTheme(element);
              return;
            }
            if (element.closest("[role='alert'], [aria-invalid='true'], [data-theme-studio-preserve-color='true']") || hasPatternInAncestors(element, preservedTonePattern)) {
              clearTextTheme(element);
              return;
            }

            const muted = hasPatternInAncestors(element, mutedTonePattern) || Number.parseFloat(getComputedStyle(element).opacity) < .8;
            const background = nearestBackground(element);
            const preferred = muted ? themeMuted : themeText;
            const preferredContrast = contrast(preferred, background);
            const best = bestReadableColor(background, preferred);
            if (preferredContrast < 4.5 && best?.score >= preferredContrast + .75) {
              const lightText = luminance(best.color) > .48;
              element.style.setProperty('--ts-node-text', formatColor(best.color));
              element.style.setProperty('--ts-node-outline', lightText ? 'rgb(0 0 0 / 94%)' : 'rgb(255 255 255 / 96%)');
              element.style.setProperty('--ts-node-shadow', lightText
                ? '0 1px 2px rgb(0 0 0 / 92%), 0 0 8px rgb(0 0 0 / 52%)'
                : '0 1px 2px rgb(255 255 255 / 96%), 0 0 7px rgb(255 255 255 / 64%)');
              element.setAttribute(toneAttribute, 'adaptive');
              return;
            }

            element.style.removeProperty('--ts-node-text');
            element.style.removeProperty('--ts-node-shadow');
            element.style.removeProperty('--ts-node-outline');
            element.setAttribute(toneAttribute, muted ? 'muted' : 'primary');
          }

          function scanText(rootNode) {
            if (!(rootNode instanceof Element)) return;
            classifyText(rootNode);
            for (const element of rootNode.querySelectorAll('*')) classifyText(element);
          }

          let scanFrame = 0;
          const pendingTextRoots = new Set();
          function queueTextScan(node) {
            const element = node?.nodeType === Node.ELEMENT_NODE ? node : node?.parentElement;
            if (element) {
              for (const pendingRoot of pendingTextRoots) {
                if (pendingRoot.contains(element)) return;
                if (element.contains(pendingRoot)) pendingTextRoots.delete(pendingRoot);
              }
              pendingTextRoots.add(element);
            }
            if (scanFrame) return;
            scanFrame = requestAnimationFrame(() => {
              scanFrame = 0;
              const roots = [...pendingTextRoots];
              pendingTextRoots.clear();
              for (const pendingRoot of roots) scanText(pendingRoot);
            });
          }

          const textObserver = new MutationObserver(mutations => {
            for (const mutation of mutations) {
              if (mutation.type === 'characterData') queueTextScan(mutation.target);
              else if (mutation.type === 'attributes') queueTextScan(mutation.target);
              else {
                const changedElement = mutation.target?.nodeType === Node.ELEMENT_NODE ? mutation.target : mutation.target?.parentElement;
                if (changedElement) classifyText(changedElement);
                for (const node of mutation.addedNodes) queueTextScan(node);
              }
            }
          });
          scanText(document.body);
          textObserver.observe(document.body, {
            subtree: true,
            childList: true,
            characterData: true,
            attributes: true,
            attributeFilter: ['class', 'data-placeholder', 'aria-placeholder', 'aria-label', 'role', 'disabled', 'aria-disabled']
          });

          const windowControlsBackdrop = add(document.createElement('div'));
          windowControlsBackdrop.id = 'theme-studio-window-controls-backdrop';
          windowControlsBackdrop.setAttribute('aria-hidden', 'true');
          document.body.append(windowControlsBackdrop);

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
            textObserver.disconnect();
            if (scanFrame) cancelAnimationFrame(scanFrame);
            for (const element of document.querySelectorAll(`[${toneAttribute}]`)) clearTextTheme(element);
            for (const element of document.querySelectorAll('[data-theme-studio-owned="true"]')) element.remove();
            for (const url of config.objectUrls || []) { try { URL.revokeObjectURL(url); } catch {} }
            for (const key of Object.keys(properties)) root.style.removeProperty(key);
            for (const key of ['--ts-media-opacity','--ts-media-blur','--ts-media-fit','--ts-media-position','--ts-badge-size','--ts-badge-opacity','--ts-badge-radius','--ts-badge-background-opacity','--ts-badge-border-opacity','--ts-color-scheme','--ts-surface-opacity','--ts-sidebar-opacity','--ts-composer-opacity','--ts-bubble-opacity','--ts-surface-blur','--ts-surface-radius','--ts-text-shadow','--ts-text-outline','--ts-readable-muted']) root.style.removeProperty(key);
            delete root.dataset.themeStudio;
            delete root.dataset.themeStudioDeep;
            delete root.dataset.themeStudioSurfaces;
            delete window.__themeStudioRuntime;
          };
          window.__themeStudioRuntime = { version: config.version, themeId: config.themeId, dispose, refreshText: () => scanText(document.body) };
          return JSON.stringify({ ok: true, themeId: config.themeId });
        })()
        """;

    public static bool UsesLightColorScheme(string color)
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
