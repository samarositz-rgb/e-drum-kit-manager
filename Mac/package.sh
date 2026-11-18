#!/bin/bash
# Builds the Mac app (Release) outside iCloud-synced folders and wraps it in a disk image.
# Usage: Mac/package.sh   ->  ~/edrum-build/EDrumKitManager-<version>.dmg
set -e
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1
OUT="$HOME/edrum-build"
HERE="$(cd "$(dirname "$0")" && pwd)"
VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' "$HERE/EDrumKitManager.Mac.csproj")"

# Release configuration builds crash at launch on macOS 27 ("Failed to initialize the VM"), so use the Debug
# configuration with optimisation on and no symbols. Always build clean: a stale obj folder breaks the signature.
rm -rf "$OUT/release" "$OUT/dmg-stage" "$HERE/obj" "$HERE/bin"
dotnet build "$HERE/EDrumKitManager.Mac.csproj" -c Debug -p:Optimize=true -p:DebugType=none -p:OutputPath="$OUT/release/"
mkdir -p "$OUT/dmg-stage"
cp -R "$OUT/release/EDrumKitManager.app" "$OUT/dmg-stage/"
# Prove the app starts before it goes in the disk image.
(cd /tmp; timeout 6 "$OUT/release/EDrumKitManager.app/Contents/MacOS/EDrumKitManager" >/dev/null 2>&1; [ $? -eq 124 ]) || { echo "App failed to stay running"; exit 1; }
ln -s /Applications "$OUT/dmg-stage/Applications"
hdiutil create -volname "E-Drum Kit Manager" -srcfolder "$OUT/dmg-stage" -ov -format UDZO "$OUT/EDrumKitManager-$VERSION.dmg"
rm -rf "$OUT/dmg-stage"
echo "Built $OUT/EDrumKitManager-$VERSION.dmg"
