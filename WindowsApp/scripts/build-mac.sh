#!/bin/bash
# Avalonia 앱의 맥 번들(.app)을 만듭니다: WindowsApp/bin/mac/<rid>/YouTube Playlist Downloader.app
#   WindowsApp/scripts/build-mac.sh                  이 맥의 아키텍처(arm64 또는 x86_64)로 빌드
#   ARCH=x86_64 WindowsApp/scripts/build-mac.sh      인텔 맥용으로 빌드
#   DOTNET=/path/to/dotnet WindowsApp/scripts/build-mac.sh   .NET 10 SDK 위치를 직접 지정
# yt-dlp·ffmpeg·ffprobe·deno는 MacApp/scripts/build-app.sh와 같은 곳에서 받아 Contents/Resources/tools에 넣습니다(ToolManager가 찾는 자리).
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet="${DOTNET:-dotnet}"
arch="${ARCH:-$(uname -m)}"
case "$arch" in
  arm64)  rid=osx-arm64; deno_arch=aarch64; ffmpeg_arch=arm64 ;;
  x86_64) rid=osx-x64;   deno_arch=x86_64;  ffmpeg_arch=amd64 ;;
  *) echo "지원하지 않는 아키텍처: $arch" >&2; exit 1 ;;
esac

cache="AvaloniaApp/tools-cache/$rid"
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
  tag="${latest##*/}"
  [[ "$tag" =~ ^[0-9]+(\.[0-9]+)+$ ]] || { echo "yt-dlp 최신 태그를 읽지 못했습니다: $tag" >&2; exit 1; }
  echo "$tag" > "$cache/yt-dlp.VERSION"
  rm -f "$cache/yt-dlp_macos.zip"
fi
fetch "https://github.com/yt-dlp/yt-dlp/releases/download/$(cat "$cache/yt-dlp.VERSION")/yt-dlp_macos.zip" yt-dlp_macos.zip
fetch "https://github.com/denoland/deno/releases/latest/download/deno-$deno_arch-apple-darwin.zip" deno.zip
fetch "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$ffmpeg_arch/release/ffmpeg.zip" ffmpeg.zip
fetch "https://ffmpeg.martin-riedl.de/redirect/latest/macos/$ffmpeg_arch/release/ffprobe.zip" ffprobe.zip

out="bin/mac/$rid"
rm -rf "$out"
"$dotnet" publish AvaloniaApp/AvaloniaApp.csproj -c Release -r "$rid" -p:BundleTools=false -o "$out/publish"

app="$out/YouTube Playlist Downloader.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/tools"
cp -R "$out/publish/." "$app/Contents/MacOS/"
# SwiftUI 맥 앱(MacApp)의 Info.plist·아이콘을 쓰되, 두 앱이 서로 다른 앱으로 보이게 번들 ID만 바꿉니다.
cp ../MacApp/Resources/Info.plist "$app/Contents/Info.plist"
plutil -replace CFBundleIdentifier -string io.github.youtubeplaylistdownloader.desktop "$app/Contents/Info.plist"
cp ../MacApp/Resources/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"

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
chmod +x "$app/Contents/MacOS/YoutubePlaylistDownloader" "$tools/yt-dlp/yt-dlp_macos" "$tools/deno" "$tools/ffmpeg" "$tools/ffprobe"

# 로컬에서 쓰는 앱이라 임시(ad-hoc) 서명만 합니다. 배포하려면 Developer ID로 서명·공증해야 합니다.
xattr -cr "$app" 2>/dev/null || true
codesign --force --deep --sign - "$app"

echo "완료: $app"
