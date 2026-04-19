#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"

# 1. Generate version from latest git commit hash
GIT_HASH=$(git rev-parse --short HEAD)
NEW_VER="v2.1.0-$GIT_HASH"

echo "=== Syncing Version to Code: $NEW_VER ==="

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

# 5. Push source changes to GitHub
echo "=== Pushing Source to GitHub Master ==="
git add .
git commit -m "Automated Build: $NEW_VER"
git push origin master

# 6. Create GitHub Release and Upload Asset
echo "=== Creating GitHub Release: $NEW_VER ==="
if command -v gh >/dev/null 2>&1; then
    gh release create "$NEW_VER" "$OUT/XOSC.zip" --title "Release $NEW_VER" --notes "Automated build from commit $GIT_HASH"
else
    echo "⚠️ Warning: GitHub CLI (gh) not found. Skipping Release creation."
    echo "Please install 'gh' to automate releases."
fi

echo "=== Done: Published $NEW_VER and created GitHub Release ==="