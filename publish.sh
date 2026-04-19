#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"

# 1. Extract version from Program.cs
VERSION=$(grep -oP '(?<=AppVersion = ")[^"]+' Program.cs)

echo "=== Detected Version: $VERSION ==="

# 2. Clean
rm -rf "$OUT"

# 3. Build Linux
echo "=== Building Linux x64 ==="
dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/linux-x64"

# 4. Build Windows
echo "=== Building Windows x64 ==="
dotnet publish "$PROJ" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/win-x64"

# 5. Create ZIP for Updater
echo "=== Creating XOSC.zip ==="
cd "$OUT"
zip -r XOSC.zip linux-x64 win-x64
cd ..

# 6. Git Push
echo "=== Pushing to GitHub Master ==="
git add .
git commit -m "Build v$VERSION: Automated release push"
git push origin master

echo "=== Done ==="