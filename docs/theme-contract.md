# Theme Contract v1

Each theme is a JSON document with owned media stored beside the local theme
repository. Unknown properties must be preserved by future migrations.

## Modes

- `standard`: media, palette, surface variables, component styling, and badge.
- `deep`: standard layers plus Hero, suggestion cards, and home composition.

Mode is a user choice. Runtime compatibility never silently rewrites it.

## Layers

```json
{
  "layers": {
    "media": true,
    "surfaces": true,
    "components": true,
    "badge": true,
    "hero": true,
    "suggestions": true,
    "homeLayout": true
  }
}
```

Deep layers have separate compatibility results. A failed selector or geometry
rule suspends only its corresponding layer for that application session.

## Media

Backgrounds accept `none`, `image`, or `video`. Files are imported into the
repository; URLs are served from a loopback-only server with a random session
token. Video is muted, looped, and non-interactive.

## Badge

The corner badge supports an imported image, short fallback text, four corner
positions, icon/glass/outline styles, size, edge offsets, opacity, background,
border, and radius controls. It belongs to x纸鸢 and is removed with the
theme.

## Safety invariants

1. Never patch the Codex install directory.
2. Never close Codex because a style failed.
3. Never persist a runtime compatibility downgrade as a theme edit.
4. Every injected node, style, observer, and timer must have a dispose path.
5. Theme deletion must not delete media referenced by another theme.
