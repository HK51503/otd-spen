#!/usr/bin/env bash
# Builds release artifacts into dist/:
#   SPen.OpenTabletDriver-<version>.zip    plugin (dlls + LICENSE at the top level, as OTD's plugin repo requires)
#   otd-spen-companion-<version>-debug.apk companion app (debug-signed; see README)
#   plugin-repository/Repository/...json   metadata for a PR to OpenTabletDriver/Plugin-Repository
#
# Upload the zip to the GitHub release tagged <version> exactly as built: the metadata pins its SHA-256.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

OWNER="HK51503"
REPO="otd-spen"
NAME="S Pen over adb"
SUPPORTED_DRIVER_VERSION="0.6.6.2" # the OpenTabletDriver.Plugin API we build against

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"
[ -n "$VERSION" ] || { echo "No <Version> in Directory.Build.props" >&2; exit 1; }

DIST="$ROOT/dist"
STAGE="$DIST/stage"
ZIP_NAME="SPen.OpenTabletDriver-$VERSION.zip"
rm -rf "$DIST"
mkdir -p "$STAGE"

echo "== Plugin $VERSION"
dotnet build -c Release src/SPen.OpenTabletDriver -o "$STAGE/build" -nologo -v quiet
cp "$STAGE/build/SPen.OpenTabletDriver.dll" "$STAGE/build/SPen.Evdev.dll" LICENSE "$STAGE/"
# Fixed timestamps so rebuilding the same source gives the same zip (and SHA-256).
touch -t 202601010000 "$STAGE"/SPen.*.dll "$STAGE/LICENSE"
(cd "$STAGE" && zip -X -q "$DIST/$ZIP_NAME" SPen.OpenTabletDriver.dll SPen.Evdev.dll LICENSE)
SHA256="$(shasum -a 256 "$DIST/$ZIP_NAME" | cut -d' ' -f1)"

echo "== Companion app"
(cd android && ./gradlew assembleDebug -q)
cp android/app/build/outputs/apk/debug/app-debug.apk "$DIST/otd-spen-companion-$VERSION-debug.apk"

echo "== Plugin repository metadata"
META_DIR="$DIST/plugin-repository/Repository/$SUPPORTED_DRIVER_VERSION/$OWNER/$REPO"
mkdir -p "$META_DIR"
cat > "$META_DIR/$REPO.json" <<EOF
{
    "Name": "$NAME",
    "Owner": "$OWNER",
    "Description": "Use a Samsung Galaxy phone's S Pen as a pen tablet over USB (adb). Companion app and setup: see the wiki link.",
    "PluginVersion": "$VERSION",
    "SupportedDriverVersion": "$SUPPORTED_DRIVER_VERSION",
    "RepositoryUrl": "https://github.com/$OWNER/$REPO",
    "DownloadUrl": "https://github.com/$OWNER/$REPO/releases/download/$VERSION/$ZIP_NAME",
    "CompressionFormat": "zip",
    "SHA256": "$SHA256",
    "WikiUrl": "https://github.com/$OWNER/$REPO/blob/main/README.md",
    "LicenseIdentifier": "GPL-3.0-only"
}
EOF

rm -rf "$STAGE"
echo
echo "dist/$ZIP_NAME"
echo "  SHA-256 $SHA256"
unzip -l "$DIST/$ZIP_NAME" | sed 's/^/  /'
echo "dist/otd-spen-companion-$VERSION-debug.apk"
echo "dist/plugin-repository/Repository/$SUPPORTED_DRIVER_VERSION/$OWNER/$REPO/$REPO.json"
