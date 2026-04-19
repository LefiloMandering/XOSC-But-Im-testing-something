#!/usr/bin/env bash
# publish.sh — builds self-contained single-file binaries for Windows and Linux
set -e

OUT="./publish"
PROJ="XOSC.csproj"

echo "=== Building Linux x64 ==="
dotnet publish "$PROJ" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT/linux-x64"

# Copy icon next to the linux binary (for .desktop install + tray)
[ -f icon.png ] && cp icon.png "$OUT/linux-x64/"
[ -f icon.ico ] && cp icon.ico "$OUT/linux-x64/"

echo ""
echo "=== Building Windows x64 ==="
dotnet publish "$PROJ" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT/win-x64"

[ -f icon.png ] && cp icon.png "$OUT/win-x64/"
[ -f icon.ico ] && cp icon.ico "$OUT/win-x64/"

echo ""
echo "=== Done ==="
echo "  Linux:   $OUT/linux-x64/XOSC"
echo "  Windows: $OUT/win-x64/XOSC.exe"

exit
