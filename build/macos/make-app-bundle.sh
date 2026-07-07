#!/usr/bin/env bash
#
# make-app-bundle.sh — assemble a macOS LocalDictation.app bundle from a published output.
#
# Summary:
#   Takes a `dotnet publish` output folder (multi-file self-contained) and wraps it in a
#   standard macOS .app bundle, generating a proper Info.plist. The app runs as a menu-bar
#   agent (LSUIElement) and declares microphone usage, matching the Windows tray behaviour.
#
# Usage:
#   ./make-app-bundle.sh <publish-dir> <output-app-path> <version> [arch]
#     publish-dir      Folder produced by `dotnet publish ... -o <publish-dir>` (holds the
#                      `LocalDictation` executable + native dylibs sitting beside it).
#     output-app-path  Destination bundle, e.g. dist/LocalDictation.app
#     version          Marketing version string, e.g. 1.0.6 (no leading v).
#     arch             arm64 (default) or x64 — selects the whisper macOS natives to stage.
#
# Notes:
#   - MULTI-FILE publish only: whisper.net probes for its native libwhisper/ggml dylibs next
#     to the executable, so everything is copied into Contents/MacOS (never single-file).
#   - CRITICAL: Whisper.net.Runtime ships its macOS dylibs under the NON-standard RID folder
#     runtimes/macos-<arch>/, which a RID-specific `dotnet publish -r osx-<arch>` prunes (that
#     folder name isn't in the .NET RID graph). So the dylibs are staged EXPLICITLY here from the
#     restored NuGet package into Contents/MacOS/runtimes/macos-<arch>/ — otherwise the installed
#     app fails warm-up with "Native Library not found" and every dictation returns "No speech
#     detected" (the macOS twin of the Windows single-file gotcha). ggml-metal.metal already
#     publishes to the output root, so it rides along with the copy above.
#   - The AppIcon.icns is optional: it is copied into Contents/Resources only if it exists,
#     otherwise the bundle is built without a custom icon (guarded, does not fail the build).
#
set -euo pipefail

PUBLISH_DIR="${1:?publish-dir required}"
APP_PATH="${2:?output-app-path required}"
VERSION="${3:?version required}"
ARCH="${4:-arm64}"                        # arm64 (default) or x64

# Bundle identity constants (keep in sync with the workflow comments).
BUNDLE_ID="com.localdictation.app"
BUNDLE_NAME="LocalDictation"
BUNDLE_EXECUTABLE="LocalDictation"
ICON_FILE="AppIcon"                       # base name, no extension, per CFBundleIconFile
MIN_MACOS="12.0"
MIC_USAGE="LocalDictation needs microphone access to transcribe your speech locally on this device."

# Source icon: shipped alongside this script (optional). Adjust if the asset moves.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ICON_SRC="${SCRIPT_DIR}/AppIcon.icns"

echo "Assembling ${APP_PATH} (version ${VERSION}) from ${PUBLISH_DIR}"

# Fresh bundle skeleton.
rm -rf "${APP_PATH}"
mkdir -p "${APP_PATH}/Contents/MacOS"
mkdir -p "${APP_PATH}/Contents/Resources"

# Copy the entire published output (exe + native dylibs must stay siblings).
cp -R "${PUBLISH_DIR}/." "${APP_PATH}/Contents/MacOS/"
chmod +x "${APP_PATH}/Contents/MacOS/${BUNDLE_EXECUTABLE}" || true

# --- Stage the whisper macOS natives (the RID-prune workaround, see header note) --------------
MACOS_RID="macos-${ARCH}"
WHISPER_NATIVE_DEST="${APP_PATH}/Contents/MacOS/runtimes/${MACOS_RID}"
if ls "${WHISPER_NATIVE_DEST}"/libwhisper.dylib >/dev/null 2>&1; then
  echo "Whisper ${MACOS_RID} natives already present in publish output — leaving as-is."
else
  # Locate the highest restored Whisper.net.Runtime package in the NuGet cache.
  NUGET_ROOT="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
  WHISPER_PKG_DIR="$(ls -d "${NUGET_ROOT}"/whisper.net.runtime/* 2>/dev/null | sort -V | tail -1)"
  WHISPER_SRC="${WHISPER_PKG_DIR}/build/${MACOS_RID}"
  if [[ -d "${WHISPER_SRC}" ]]; then
    mkdir -p "${WHISPER_NATIVE_DEST}"
    cp "${WHISPER_SRC}"/*.dylib "${WHISPER_NATIVE_DEST}/"
    # Also place them beside the executable — whisper.net probes both the runtimes/<rid> path
    # and the app base directory, so covering both makes native discovery robust.
    cp "${WHISPER_SRC}"/*.dylib "${APP_PATH}/Contents/MacOS/"
    # The Metal shader must sit at the app root (where whisper.net looks); copy if not already there.
    [[ -f "${WHISPER_PKG_DIR}/build/ggml-metal.metal" && ! -f "${APP_PATH}/Contents/MacOS/ggml-metal.metal" ]] \
      && cp "${WHISPER_PKG_DIR}/build/ggml-metal.metal" "${APP_PATH}/Contents/MacOS/"
    echo "Staged whisper ${MACOS_RID} natives from ${WHISPER_SRC}"
  else
    echo "ERROR: whisper ${MACOS_RID} natives not found at ${WHISPER_SRC}. Dictation will fail on device." >&2
    exit 1
  fi
fi

# Slim the bundle: drop native runtimes for other OSes that the multi-file publish dragged in.
find "${APP_PATH}/Contents/MacOS/runtimes" -maxdepth 1 -mindepth 1 -type d \
  ! -name "macos-*" ! -name "osx*" ! -name "unix" -exec rm -rf {} + 2>/dev/null || true

# Optional app icon — copy only if present, otherwise skip without failing.
if [[ -f "${ICON_SRC}" ]]; then
  cp "${ICON_SRC}" "${APP_PATH}/Contents/Resources/${ICON_FILE}.icns"
  echo "Bundled icon ${ICON_FILE}.icns"
else
  echo "No AppIcon.icns found at ${ICON_SRC} — building bundle without a custom icon."
fi

# Info.plist. LSUIElement=true → menu-bar agent, no Dock icon.
cat > "${APP_PATH}/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key>
    <string>${BUNDLE_ID}</string>
    <key>CFBundleName</key>
    <string>${BUNDLE_NAME}</string>
    <key>CFBundleDisplayName</key>
    <string>${BUNDLE_NAME}</string>
    <key>CFBundleExecutable</key>
    <string>${BUNDLE_EXECUTABLE}</string>
    <key>CFBundleIconFile</key>
    <string>${ICON_FILE}</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>${VERSION}</string>
    <key>CFBundleVersion</key>
    <string>${VERSION}</string>
    <key>LSMinimumSystemVersion</key>
    <string>${MIN_MACOS}</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSMicrophoneUsageDescription</key>
    <string>${MIC_USAGE}</string>
</dict>
</plist>
PLIST

echo "Bundle assembled: ${APP_PATH}"
