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
#   ./make-app-bundle.sh <publish-dir> <output-app-path> <version>
#     publish-dir      Folder produced by `dotnet publish ... -o <publish-dir>` (holds the
#                      `LocalDictation` executable + native dylibs sitting beside it).
#     output-app-path  Destination bundle, e.g. dist/LocalDictation.app
#     version          Marketing version string, e.g. 1.0.6 (no leading v).
#
# Notes:
#   - MULTI-FILE publish only: whisper.net probes for its native libwhisper/ggml dylibs next
#     to the executable, so everything is copied into Contents/MacOS (never single-file).
#   - The AppIcon.icns is optional: it is copied into Contents/Resources only if it exists,
#     otherwise the bundle is built without a custom icon (guarded, does not fail the build).
#
set -euo pipefail

PUBLISH_DIR="${1:?publish-dir required}"
APP_PATH="${2:?output-app-path required}"
VERSION="${3:?version required}"

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
