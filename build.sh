#!/bin/sh
# Builds the plugin in a .NET 10 container (no SDK to install) and prepares the folder to copy
# into Jellyfin's plugins directory, plus the installable .zip.
set -e
cd "$(dirname "$0")"
VERSION=$(sed -n 's/.*"version": "\(.*\)".*/\1/p' meta.json)
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0-alpine \
  dotnet build Jellyfin.Plugin.LibraryCovers/Jellyfin.Plugin.LibraryCovers.csproj -c Release -nologo -v q \
  -p:Version="$VERSION" -p:AssemblyVersion="$VERSION" -p:FileVersion="$VERSION"
DIR="out/Library Covers_$VERSION"
rm -rf out && mkdir -p "$DIR"
cp Jellyfin.Plugin.LibraryCovers/bin/Release/net10.0/Jellyfin.Plugin.LibraryCovers.dll meta.json "$DIR/"
(cd "$DIR" && zip -X -q "../../out/library-covers_$VERSION.zip" Jellyfin.Plugin.LibraryCovers.dll meta.json) 2>/dev/null || true
ls -la "$DIR" out
