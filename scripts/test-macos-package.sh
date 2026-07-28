#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "The packaged macOS smoke test requires a Mac." >&2
  exit 1
fi

DMG="${1:?Pass the ARM64 DMG path as the first argument.}"
DMG="$(cd "$(dirname "$DMG")" && pwd)/$(basename "$DMG")"
MOUNT_POINT="$(mktemp -d /tmp/xzhiyuan-dmg.XXXXXX)"
TEST_ROOT="$(mktemp -d /tmp/xzhiyuan-app.XXXXXX)"

cleanup() {
  pkill -f 'ThemeStudio.MacBridge' >/dev/null 2>&1 || true
  pkill -f '/x纸鸢.app/Contents/MacOS/x纸鸢' >/dev/null 2>&1 || true
  hdiutil detach "$MOUNT_POINT" -quiet >/dev/null 2>&1 || true
  rm -rf "$MOUNT_POINT" "$TEST_ROOT"
}
trap cleanup EXIT

hdiutil attach "$DMG" -mountpoint "$MOUNT_POINT" -nobrowse -quiet
APP_PATH="$(find "$MOUNT_POINT" -maxdepth 1 -type d -name '*.app' -print -quit)"
if [[ -z "$APP_PATH" ]]; then
  echo "DMG does not contain an application bundle." >&2
  exit 1
fi

cp -R "$APP_PATH" "$TEST_ROOT/x纸鸢.app"
APP_PATH="$TEST_ROOT/x纸鸢.app"
PLIST="$APP_PATH/Contents/Info.plist"
BACKEND="$APP_PATH/Contents/Resources/backend/ThemeStudio.MacBridge"
ELECTRON_FRAMEWORK="$APP_PATH/Contents/Frameworks/Electron Framework.framework/Versions/A/Electron Framework"

test -f "$PLIST"
test -x "$BACKEND"
test -f "$APP_PATH/Contents/Resources/ui/index.html"
test -f "$APP_PATH/Contents/Resources/SeedAssets/x-zhiyuan-emblem.png"
file "$BACKEND" | grep -q 'arm64'
lipo -archs "$ELECTRON_FRAMEWORK" | grep -q 'arm64'

open "$APP_PATH"
for _ in {1..20}; do
  if pgrep -f 'ThemeStudio.MacBridge' >/dev/null; then
    echo "macOS package smoke test passed."
    exit 0
  fi
  sleep 0.5
done

echo "The application launched but its backend did not start." >&2
exit 1
