#!/bin/sh
# Mac에서 Parallels Windows VM 안에 Windows 앱을 빌드합니다. 결과는 VM의 %USERPROFILE%\YoutubePlaylistDownloader입니다.
# 필요: VM이 켜져 있고 Parallels Tools가 설치돼 있으며, Mac 홈 폴더가 공유돼 있어야 합니다(기본 설정).
# 사용: WindowsApp/scripts/build-parallels.sh ["VM 이름"]   VM 이름을 빼면 실행 중인 첫 VM을 씁니다.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
app_dir=$(dirname "$here")
case "$app_dir" in
    "$HOME"/*) ;;
    *) echo "저장소가 홈 폴더 밖에 있어 VM에서 보이지 않습니다: $app_dir" >&2; exit 1 ;;
esac

vm=${1:-$(prlctl list -o status,name --no-header | awk '$1 == "running" { $1 = ""; sub(/^ /, ""); print; exit }')}
[ -n "$vm" ] || { echo "실행 중인 Parallels VM이 없습니다. VM을 켜고 다시 실행하십시오." >&2; exit 1; }

# Mac 경로를 VM에서 보이는 공유 폴더 경로(\\Mac\Home\...)로 바꿉니다.
rel=$(printf '%s' "${app_dir#"$HOME"/}" | tr '/' '\\')
share="\\\\Mac\\Home\\$rel"
# PowerShell 작은따옴표 문자열 안에서는 '를 ''로 씁니다.
share=$(printf '%s' "$share" | sed "s/'/''/g")

# 따옴표가 prlctl을 거치며 사라지므로 PowerShell 명령은 base64(UTF-16LE)로 넘깁니다.
# 출력이 파이프일 때 Windows PowerShell은 Write-Host 등을 CLIXML로 내보내므로, 모든 출력을 글자로 바꿔 stdout으로 보냅니다.
cmd="\$ProgressPreference = 'SilentlyContinue'; try { & '$share\\scripts\\build-parallels.ps1' -Source '$share' *>&1 | ForEach-Object { \"\$_\" } } catch { \"오류: \$_\"; exit 1 }"
encoded=$(printf '%s' "$cmd" | iconv -f UTF-8 -t UTF-16LE | base64 | tr -d '\n')
echo "VM '$vm'에서 빌드합니다..."
exec prlctl exec "$vm" --current-user powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "$encoded"
