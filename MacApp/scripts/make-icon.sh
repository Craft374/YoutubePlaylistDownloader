#!/bin/bash
# Resources/AppIcon.svg → Resources/AppIcon.icns
# 로고를 바꿨을 때만 실행합니다. 결과(.icns)는 저장소에 들어 있어 평소 빌드에는 필요 없습니다.
# 필요: rsvg-convert (brew install librsvg)
set -euo pipefail
cd "$(dirname "$0")/.."

command -v rsvg-convert >/dev/null || { echo "rsvg-convert가 필요합니다: brew install librsvg" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
iconset="$work/AppIcon.iconset"
mkdir "$iconset"
for size in 16 32 128 256 512; do
  rsvg-convert -w "$size" -h "$size" Resources/AppIcon.svg -o "$iconset/icon_${size}x${size}.png"
  rsvg-convert -w $((size * 2)) -h $((size * 2)) Resources/AppIcon.svg -o "$iconset/icon_${size}x${size}@2x.png"
done
iconutil -c icns "$iconset" -o Resources/AppIcon.icns
echo "Resources/AppIcon.icns"
