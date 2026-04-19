#!/usr/bin/env bash
set -e

OUT="./publish"
PROJ="XOSC.csproj"

GIT_HASH=$(git rev-parse --short HEAD)
NEW_VER="$GIT_HASH"

echo "=== Syncing Version to Code: $NEW_VER ==="

sed -i "s/public string Version = \".*\";/public string Version = \"$NEW_VER\";/" Program.cs

rm -rf "$OUT"

echo "=== Building Linux x64 ==="
dotnet publish "$PROJ" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/linux-x64"

echo "=== Building Windows x64 ==="
dotnet publish "$PROJ" -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o "$OUT/win-x64"

echo "=== Creating XOSC.zip ==="
cd "$OUT"
zip -r XOSC.zip linux-x64 win-x64
cd ..

echo "=== Pushing Source to GitHub ==="
git add .
git commit -m "Release $NEW_VER"
git push origin master

echo "=== Creating GitHub Release: $NEW_VER ==="
if command -v gh >/dev/null 2>&1; then
    gh release create "$NEW_VER" "$OUT/XOSC.zip" --title "$NEW_VER" --notes "Automated build from commit $GIT_HASH"
else
    echo "gh cli not found, skipping release creation"
fi

echo "=== Done ==="