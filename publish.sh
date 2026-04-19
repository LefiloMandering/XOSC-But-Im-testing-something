#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"

# 1. Generate version from latest git commit hash
GIT_HASH=$(git rev-parse --short HEAD)

echo "=== Syncing Version to Code: $GIT_HASH ==="

# 2. Inject version into Program.cs (Targets the hardcoded AppVersion constant)
sed -i "s/public const string AppVersion = \".*\";/public const string AppVersion = \"$GIT_HASH\";/" Program.cs

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

# 5. Push Source Changes
echo "=== Pushing Source to GitHub ==="
git add .
git commit -m "Release $GIT_HASH"
git push origin master

# 6. Create GitHub Release
echo "=== Creating GitHub Release: $GIT_HASH ==="
if command -v gh >/dev/null 2>&1; then
    gh release create "$GIT_HASH" "$OUT/XOSC.zip" --title "$GIT_HASH" --notes "Automated build from commit $GIT_HASH"
else
    echo "gh cli not found, skipping release creation"
fi

echo "=== Done ==="