# Architecture

x纸鸢 is a single-process .NET application with four independently
replaceable layers.

```text
Workbench UI (WebView2)
        |
Typed JSON bridge
        |
Theme repository -- compiler -- compatibility contract
        |
Codex locator -- launcher -- CDP runtime -- optional broker
```

## Workbench

The workbench owns theme CRUD, media import, offline previews, runtime status,
and explicit launch/apply commands. Its HTML is bundled with the executable and
does not require a web server or Node.js.

## Theme kernel

Themes are stored under `%LocalAppData%/ThemeStudioForCodex`. The legacy
directory name is retained for upgrade compatibility. Writes use a
temporary file followed by an atomic replace. Imported media is copied into the
repository so a theme never depends on a removable source path.

The compiler emits one idempotent runtime module. Every owned node and style has
a `theme-studio` identifier and can be removed without touching Codex state.

## Compatibility adapter

Stable palette and media work is the standard layer. Deep layers are mapped by
small selector contracts. A Codex update can invalidate an individual contract;
the kernel suspends only that layer for the current session and reports why.
The saved theme mode and other layers remain unchanged.

## Runtime and broker

CDP injection is reversible and never modifies installed Codex files. The
workbench can launch Codex with a local debugging port. The optional broker
watches direct launches and applies the selected default theme when CDP is
available. Restarting an unmanaged Codex launch is disabled by default and has
a hard one-restart budget when explicitly enabled.

Any launch or verification failure ends in a native, still-running Codex window
plus a diagnostic. Theme failure is never a reason to terminate Codex.
