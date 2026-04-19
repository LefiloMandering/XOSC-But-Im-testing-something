#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"
VERSION=$(grep -oP '(?<=Version = ")[^"]+' Program.cs)

echo "=== Building v$VERSION ==="
rm -rf "$OUT"

dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/linux-x64"

dotnet publish "$PROJ" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/win-x64"

echo "=== Creating XOSC.zip ==="
cd "$OUT"
zip -r XOSC.zip linux-x64 win-x64
cd ..

echo "=== Pushing to GitHub Master ==="
git add .
git commit -m "Build v$VERSION"
git push origin master