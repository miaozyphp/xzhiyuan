# Changelog

## 0.1.20 - 2026-07-29

- 禁止后台代理自动关闭或强制结束普通启动的 Codex。
- 使用打包应用激活方式启动 Codex，主题模式失败时恢复普通 Codex。
- 增加安全模式、媒体大小策略、分片 Blob 传输、失败清理和诊断包导出。
- 隐藏工作台时暂停 WebView 与视频，Codex 渲染进程异常时自动恢复。
- 将动态文字扫描改为空闲分片执行，并补充 39 个核心测试。

All notable changes to x纸鸢 are documented here.

## Unreleased

- Added an Apple Silicon macOS candidate application and DMG build.
- Reused the existing theme library, preview, image/video import, standard/deep modes, and Codex CDP runtime across platforms.
- Added macOS Codex bundle discovery, managed restart, menu-bar residency, login auto-apply, native file selection, and DMG updates.
- Added isolated Electron IPC, platform-specific window-control handling, and macOS package tests.

## 0.1.19 - 2026-07-28

- Fixed verified update downloads remaining stuck at 100% instead of enabling installation.
- Ordered progress events before the completed download response on Windows and macOS hosts.
- Ignored delayed progress events after an update has reached the ready-to-install state.

## 0.1.18 - 2026-07-28

- Added always-visible quick deletion for custom themes and checkbox-based batch management.
- Added batch deletion with independent failure handling, safe default-theme recovery, and clear confirmation feedback.
- Added SHA-256 media deduplication so identical images or videos are skipped even when their names or extensions differ.
- Kept batch imports running when duplicate, unreadable, unsupported, or oversized files are encountered.

## 0.1.17 - 2026-07-27

- Replaced the title-bar styling workaround with a simple neutral contrast backdrop verified against a real Codex window capture.
- Added background GitHub Releases checks and an in-app version management dialog.
- Added verified update downloads with progress, strict package naming, trusted release URLs, and SHA-256 enforcement before installation.
- Added silent update handoff that closes the workbench during installation and reopens it after a successful upgrade.

## 0.1.16 - 2026-07-27

- Synchronized the native Windows title-bar symbol color with each theme's light or dark canvas.
- Added a non-interactive, theme-matched contrast backdrop beneath the minimize, maximize, and close controls.
- Restored the original DWM title-bar mode when a theme is removed during the same workbench session.

## 0.1.15 - 2026-07-27

- Added background-aware text colors for light popovers and stronger outlines over bright image and video regions.
- Increased secondary-text clarity and themed icon-only controls, generated editor placeholders, menus, and floating panels.
- Restored Codex image enlargement, notifications, and portal overlays by preserving their native fixed positioning and z-index.
- Limited live text observation to relevant DOM and attribute changes to retain responsive scrolling and streaming output.

## 0.1.14 - 2026-07-27

- Fixed Codex updates and light native color schemes forcing black text over dark image and video themes.
- Applied configurable primary and secondary text colors to dynamically rendered navigation, cards, dialogs, menus, and composer content.
- Preserved syntax highlighting and semantic status colors while adding automatic inverse text on opaque high-contrast surfaces.
- Added complete runtime cleanup and live DOM observation so theme changes remain effective after navigation without restarting Codex.

## 0.1.13 - 2026-07-27

- Unified public branding and source asset names under x纸鸢 while retaining legacy internal identifiers for upgrade compatibility.
- Added contribution, conduct, support, privacy, security, issue, and pull-request documentation for public collaboration.
- Added NuGet lock files, pinned GitHub Actions, Dependabot configuration, and a reproducible .NET SDK selection.
- Made release builds read the application version automatically and package privacy and security disclosures.
- Added optional Authenticode signing support, unsigned GitHub prereleases, SHA-256 verification guidance, and build-provenance attestations.
- Removed tracked local progress notes and machine-specific paths from the public source tree.

## 0.1.12 - 2026-07-27

- Fixed first launch from downloaded installers failing when LocalAppData is redirected through a directory junction.
- Resolved the app-owned data root to its physical directory before reading themes, without moving or replacing user data.
- Launched the post-install workbench with the original user context instead of inheriting installer redirection-trust restrictions.

## 0.1.11 - 2026-07-27

- Added batch image and video import by dragging multiple files into the theme library.
- Added per-file progress, partial-failure reporting, duplicate-name numbering, and filename length validation.
- Kept valid files importing when another dropped file has an unsupported format or exceeds the 80 MB limit.
- Made release builds verify Inno Setup before cleaning existing artifacts, and discover standard installation paths automatically.

## 0.1.10 - 2026-07-27

- Unified media themes into a continuous translucent workbench instead of stacking opaque black panels.
- Added paused first-frame video covers to both theme thumbnails and the background asset picker.
- Changed the window close action to keep x纸鸢 in the notification area, with restore, launch, and explicit exit commands on the tray menu.

## 0.1.9 - 2026-07-27

- Renamed the user-facing application, installer, shortcuts, window, and tray presence to x纸鸢.
- Applied the original X纸鸢 emblem across the application, installer, tray, and built-in theme badge.
- Added a theme-driven image and video backdrop to the workbench with live opacity, blur, fit, and position updates.
- Made the installer destination page always visible so every install and update can choose its location.
- Refreshed the built-in emblem during upgrades while preserving existing themes, settings, and media.

## 0.1.8 - 2026-07-26

- Fixed the published desktop application omitting the Lucide runtime and rendering icon buttons as empty dark squares.
- Added complete external UI resource verification to release builds so missing or truncated assets fail before packaging.
- Added recognizable fallback symbols for core commands if the icon runtime is ever unavailable.

## 0.1.7 - 2026-07-26

- Fixed deleted custom themes remaining visible in the library after their files were removed.
- Selected the next visible custom theme after deletion, or returned to the built-in default when none remain.
- Verified consecutive deletion of multiple custom themes through the complete confirmation and list-update flow.

## 0.1.6 - 2026-07-25

- Replaced ambiguous theme action icons with visible "Set default", "Create copy", and "Delete" labels on the selected theme.
- Marked the active default theme explicitly and disabled duplicate default-setting requests.
- Kept the labeled action bar readable without horizontal overflow at the minimum supported window width.

## 0.1.5 - 2026-07-25

- Increased theme action icon size, stroke weight, contrast, and button separation.
- Made the delete action visibly red before hover so it no longer appears as an empty square.

## 0.1.4 - 2026-07-25

- Reworked theme saving into clear "Save as" and "Save changes" paths for built-in and custom themes.
- Fixed newly saved and duplicated themes not appearing in the library until a manual refresh.
- Added button-level loading, confirmation feedback, stronger hover and press motion, and clearer command hierarchy.
- Kept the save dialog open after failures and added persistent replacement-media coverage for custom themes.

## 0.1.3 - 2026-07-25

- Added icon, glass, and outline corner-badge styles with live preview.
- Added badge edge offsets, background, border, radius, size, and visibility controls.
- Changed the built-in badge to a clean 24px icon aligned with the native window toolbar.
- Restored Codex application-menu buttons to their intended transparent borders.

## 0.1.2 - 2026-07-25

- Removed large-surface backdrop blur so wallpapers remain sharp across the Codex window.
- Added wallpaper-first Rain Archive defaults with 92% media visibility and light panel masks.
- Renamed opacity controls to distinguish background visibility from panel mask strength.
- Aligned theme validation with the new 5% minimum for large-surface masks.

## 0.1.1 - 2026-07-25

- Fixed opaque Codex main and composer surfaces hiding image and video backgrounds.
- Wired sidebar, main-surface, and composer opacity controls to the current Codex DOM.
- Added versioned built-in theme migration so existing installations receive visual fixes.
- Tuned Rain Archive for a visible continuous background without sacrificing text contrast.

## 0.1.0 - 2026-07-25

- Independent .NET 8 and WebView2 workbench with no external theme-tool or Node runtime dependency.
- Local theme library with built-in light, warm, and rain-night themes.
- Standard and deep theme modes with per-layer compatibility suspension.
- Image and video backgrounds transported into Codex as CSP-compatible blob assets.
- Custom corner badge, palette, surface, component, and media controls.
- Theme copy, update, delete, default-theme, and opt-in automatic-apply workflows.
- Manifest-validated Codex discovery and bounded managed restart behavior.
- Self-contained Windows x64 release and per-user installer.
