#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET_PATH="${DOTNET_PATH:-dotnet}"
VERSION="${1:-}"
MAC_PROJECT="$ROOT/src/ThemeStudio.Mac"
BRIDGE_PROJECT="$ROOT/src/ThemeStudio.MacBridge/ThemeStudio.MacBridge.csproj"
RELEASE_DIR="$ROOT/artifacts/release"
STAGING_DIR="$ROOT/artifacts/macos"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "macOS packages must be built on a Mac with Xcode command-line tools." >&2
  exit 1
fi

if [[ -z "$VERSION" ]]; then
  VERSION="$(node -p "require('$MAC_PROJECT/package.json').version")"
fi
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Release version must use MAJOR.MINOR.PATCH format: $VERSION" >&2
  exit 1
fi

PACKAGE_VERSION="$(node -p "require('$MAC_PROJECT/package.json').version")"
if [[ "$PACKAGE_VERSION" != "$VERSION" ]]; then
  echo "package.json version $PACKAGE_VERSION does not match $VERSION" >&2
  exit 1
fi

mkdir -p "$RELEASE_DIR" "$STAGING_DIR"
find "$RELEASE_DIR" -maxdepth 1 -type f -name '*-macos-*' -delete

pushd "$MAC_PROJECT" >/dev/null
npm ci
npm test
popd >/dev/null

ICONSET="$STAGING_DIR/x-zhiyuan.iconset"
ICON="$STAGING_DIR/x-zhiyuan.icns"
SOURCE_ICON="$ROOT/src/ThemeStudio.App/SeedAssets/x-zhiyuan-emblem.png"
rm -rf "$ICONSET" "$ICON"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" "$SOURCE_ICON" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" "$SOURCE_ICON" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$ICON"

RID="osx-arm64"
BACKEND_DIR="$STAGING_DIR/backend-arm64"
rm -rf "$BACKEND_DIR"
"$DOTNET_PATH" restore "$ROOT/src/ThemeStudio.Core/ThemeStudio.Core.csproj" --locked-mode
"$DOTNET_PATH" restore "$BRIDGE_PROJECT" --runtime "$RID" --locked-mode --no-dependencies
"$DOTNET_PATH" publish "$BRIDGE_PROJECT" \
  --configuration Release \
  --runtime "$RID" \
  --self-contained true \
  --no-restore \
  --output "$BACKEND_DIR" \
  "/p:Version=$VERSION"
chmod +x "$BACKEND_DIR/ThemeStudio.MacBridge"

export XZHIYUAN_MAC_BACKEND_DIR="$BACKEND_DIR"
export XZHIYUAN_UI_DIR="$ROOT/src/ThemeStudio.App/ui"
export XZHIYUAN_SEED_DIR="$ROOT/src/ThemeStudio.App/SeedAssets"
export XZHIYUAN_MAC_OUTPUT_DIR="$RELEASE_DIR"
export XZHIYUAN_MAC_ICON="$ICON"
export CSC_IDENTITY_AUTO_DISCOVERY=false

pushd "$MAC_PROJECT" >/dev/null
npx electron-builder --config electron-builder.config.cjs --mac dmg zip --arm64 --publish never
popd >/dev/null

pushd "$RELEASE_DIR" >/dev/null
shasum -a 256 XZhiYuan-Setup-"$VERSION"-macos-arm64.dmg XZhiYuan-"$VERSION"-macos-arm64.zip > XZhiYuan-macos-arm64-SHA256SUMS.txt
popd >/dev/null

echo "macOS release artifacts: $RELEASE_DIR"
