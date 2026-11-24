#!/bin/bash
# 맥 앱 번들(.app)을 만듭니다: build/YouTube Playlist Downloader.app
#   ./scripts/build-app.sh                  릴리스 빌드
#   ./scripts/build-app.sh --refresh-tools  yt-dlp·ffmpeg·deno를 새로 받아서 빌드
# 필요: Xcode(또는 SwiftPM이 동작하는 Command Line Tools), 인터넷(처음 한 번 도구 다운로드)
set -euo pipefail
cd "$(dirname "$0")/.."

# Command Line Tools의 SwiftPM이 깨져 있는 환경이 있어 Xcode가 있으면 Xcode 툴체인을 씁니다.
if [ -z "${DEVELOPER_DIR:-}" ] && [ -d /Applications/Xcode.app/Contents/Developer ]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi

arch="$(uname -m)"   # arm64 | x86_64
case "$arch" in
  arm64)  deno_arch=aarch64; ffmpeg_arch=arm64 ;;
  x86_64) deno_arch=x86_64;  ffmpeg_arch=amd64 ;;
  *) echo "지원하지 않는 아키텍처: $arch" >&2; exit 1 ;;
esac

cache=".build/tools-cache/$arch"
[ "${1:-}" = "--refresh-tools" ] && rm -rf "$cache"
mkdir -p "$cache"

fetch() { # url file
  if [ ! -s "$cache/$2" ]; then
    echo "다운로드: $1"
    curl -fL --retry 3 --progress-bar -o "$cache/$2.part" "$1"
    mv "$cache/$2.part" "$cache/$2"
  fi
}
# 받은 yt-dlp 버전을 기록해 둡니다. 앱은 이 값과 직접 업데이트한 버전을 비교해 더 새로운 쪽을 씁니다.
if [ ! -s "$cache/yt-dlp.VERSION" ]; then
  latest="$(curl -fsIL -o /dev/null -w '%{url_effective}' https://github.com/yt-dlp/yt-dlp/releases/latest)"
  echo "${latest##*/}" > "$cache/yt-dlp.VERSION"
  rm -f "$cache/yt-dlp_macos.zip"
fi
fetch "https://github.com/yt-dlp/yt-dlp/releases/download/$(cat "$cache/yt-dlp.VERSION")/yt-dlp_macos.zip" yt-dlp_macos.zip
fetch "https://github.com/denoland/deno/releases/latest/download/deno-$deno_arch-apple-darwin.zip" deno.zip
fetch "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$ffmpeg_arch/release/ffmpeg.zip" ffmpeg.zip
fetch "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$ffmpeg_arch/release/ffprobe.zip" ffprobe.zip

echo "Swift 빌드(release)…"
swift build -c release --product YoutubePlaylistDownloader
bin="$(swift build -c release --show-bin-path)/YoutubePlaylistDownloader"

app="build/YouTube Playlist Downloader.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/tools"
cp "$bin" "$app/Contents/MacOS/YoutubePlaylistDownloader"
cp Resources/Info.plist "$app/Contents/Info.plist"
cp Resources/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"
for lproj in Resources/*.lproj; do
  [ -d "$lproj" ] && cp -R "$lproj" "$app/Contents/Resources/"
done

tools="$app/Contents/Resources/tools"
ditto -x -k "$cache/yt-dlp_macos.zip" "$tools/yt-dlp"
cp "$cache/yt-dlp.VERSION" "$tools/yt-dlp/VERSION"
for zip in deno ffmpeg ffprobe; do
  ditto -x -k "$cache/$zip.zip" "$tools/.unzip-$zip"
  found="$(find "$tools/.unzip-$zip" -type f -name "$zip" | head -1)"
  [ -n "$found" ] || { echo "$zip.zip 안에서 $zip 실행 파일을 찾지 못했습니다" >&2; exit 1; }
  mv "$found" "$tools/$zip"
  rm -rf "$tools/.unzip-$zip"
done
chmod +x "$tools/yt-dlp/yt-dlp_macos" "$tools/deno" "$tools/ffmpeg" "$tools/ffprobe"

# 로컬에서 쓰는 앱이라 임시(ad-hoc) 서명만 합니다. 배포하려면 Developer ID로 서명·공증해야 합니다.
xattr -cr "$app" 2>/dev/null || true
codesign --force --sign - "$app"

echo "완료: $app"
