#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"

# 1. Generate version from latest git commit hash ONLY
NEW_VER=$(git rev-parse --short HEAD)

echo "=== Syncing Git Hash Version to Code: $NEW_VER ==="

# 2. Inject version into Program.cs
sed -i "s/public string Version = \".*\";/public string Version = \"$NEW_VER\";/" Program.cs

# 3. Clean and Build
rm -rf "$OUT"

echo "=== Building Linux x64 ==="
dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/linux-x64"

echo "=== Building Windows x64 ==="
dotnet publish "$PROJ" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/win-x64"

# 4. Create ZIP for Updater
echo "=== Creating XOSC.zip ==="
cd "$OUT"
zip -r XOSC.zip linux-x64 win-x64
cd ..

# 5. Push to GitHub master
echo "=== Pushing to GitHub master ==="
git add .
git commit -m "Build: $NEW_VER"
git push origin master

echo "=== Done: Published hash $NEW_VER ==="