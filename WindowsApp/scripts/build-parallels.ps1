# build-parallels.sh가 Parallels Windows VM 안에서 실행합니다. 앱은 빌드만 하고 실행하지 않습니다.
# 공유 폴더(\\Mac\Home)의 소스를 VM 디스크로 복사해 빌드합니다(공유 폴더 위에서 빌드하면 느리고 잠금 문제가 생김).
param([Parameter(Mandatory)][string]$Source)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$root = Join-Path $env:LOCALAPPDATA 'YPDBuild'
$src = Join-Path $root 'src'
$dotnetDir = Join-Path $root 'dotnet'
$dotnet = Join-Path $dotnetDir 'dotnet.exe'
# 결과 폴더는 덮어쓰기만 하므로 그 안의 data(설정·로그)는 그대로 남습니다.
$out = Join-Path $env:USERPROFILE 'YoutubePlaylistDownloader'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# .NET 10 SDK는 시스템에 설치하지 않고 이 폴더에만 둡니다(지우면 사라짐).
New-Item -ItemType Directory -Force $root | Out-Null
if (!(Test-Path $dotnet) -or !(& $dotnet --list-sdks | Select-String '^10\.')) {
    Write-Host '.NET 10 SDK를 받습니다...'
    Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile (Join-Path $root 'dotnet-install.ps1') -UseBasicParsing
    & (Join-Path $root 'dotnet-install.ps1') -Channel 10.0 -InstallDir $dotnetDir
}

# bin·obj·tools-cache는 복사도 삭제도 하지 않아 다음 빌드에 다시 씁니다.
robocopy $Source $src /MIR /XD bin obj tools-cache .vs /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "소스 복사 실패(robocopy $LASTEXITCODE)" }

$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
# 앞 단계가 몇 분 걸릴 수 있어, 덮어쓰기 직전에 확인합니다.
if (Get-Process YoutubePlaylistDownloader -ErrorAction SilentlyContinue) {
    throw '앱이 실행 중이라 파일을 덮어쓸 수 없습니다. 앱을 끄고 다시 실행하십시오.'
}

# 예전(WinUI) 게시 파일이 섞이지 않게 data(설정·로그) 말고는 비우고 새로 게시합니다(tools는 캐시에서 다시 풉니다).
if (Test-Path $out) { Get-ChildItem $out | Where-Object Name -ne 'data' | Remove-Item -Recurse -Force }
& $dotnet publish (Join-Path $src 'AvaloniaApp\AvaloniaApp.csproj') -c Release -r $rid -o $out
if ($LASTEXITCODE -ne 0) { throw "빌드 실패(dotnet publish $LASTEXITCODE)" }

$exe = Join-Path $out 'YoutubePlaylistDownloader.exe'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'YouTube Playlist Downloader.lnk'))
$link.TargetPath = $exe
$link.WorkingDirectory = $out
$link.Save()
Write-Host "완료($rid): $exe (시작 메뉴에 바로 가기를 만들었습니다)"
