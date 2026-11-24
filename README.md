# YouTube Playlist Downloader for macOS & Windows

[shaked6540/YoutubePlaylistDownloader](https://github.com/shaked6540/YoutubePlaylistDownloader)(Windows, WPF)를 포크해 네이티브 앱으로 다시 만든 버전입니다.
macOS 앱(SwiftUI)은 `MacApp/`에, Windows 앱(WinUI 3, C#)은 `WindowsApp/`에 있습니다. 두 앱의 기능은 같습니다.
다운로드는 [yt-dlp](https://github.com/yt-dlp/yt-dlp)가, 병합·변환은 FFmpeg가 맡습니다.

## 기능

- 영상, 재생목록, 채널 링크를 넣으면 정보를 미리 보여 주고 다운로드합니다. 채널은 동영상, Shorts, 지난 라이브를 모두 가져옵니다.
- 일괄 다운로드: 링크를 한 줄에 하나씩 넣습니다. 추가하지 못한 링크는 이유와 함께 보여 주고, 다시 시도·복사·저장할 수 있습니다.
- 다운로드 목록에서 작업마다 진행률과 속도를 보여 줍니다. 항목을 펼쳐 영상별 상태와 오류를 볼 수 있고, 중지·다시 시도·Finder(Windows는 탐색기)에서 보기를 할 수 있습니다.
- 동영상: MP4/MKV, 최대 해상도, 높은 프레임 레이트 우선, 자막(영상에 넣음), 오디오 언어를 고를 수 있습니다.
  - MP4는 QuickTime·Finder 미리보기·iPhone에서 열리도록 H.264를 받습니다. AV1을 하드웨어로 디코딩하는 Mac(M3 이상)에서는 AV1도 받습니다.
  - 그래서 AV1을 쓸 수 없는 Mac에서는 MP4가 최대 1080p입니다. 그보다 높은 해상도는 MKV로 받으십시오.
  - Windows에서는 MP4가 항상 H.264/AAC입니다(어디서나 재생되도록). 그래서 MP4는 최대 1080p이고, 그보다 높은 해상도는 MKV로 받으십시오.
- 오디오: MP3/M4A/Opus/Ogg/FLAC/WAV/ALAC로 변환하거나 원본 그대로 받을 수 있습니다. 비트레이트를 고를 수 있고, 태그와 정사각형 커버 이미지를 넣습니다.
  - "아티스트 - 곡" 형식의 제목에서 아티스트·곡 제목·장르를 읽어 태그로 넣습니다.
- 파일 이름 패턴: `$title $index $artist $songtitle $channel $videoid $playlist $genre`
- 재생목록 범위와 영상 길이 필터를 지원합니다(단일 영상에는 적용하지 않습니다).
- 이미 있는 파일을 건너뛸 수 있고, 재생목록마다 폴더를 따로 만들 수 있으며, 끝나면 폴더를 열 수 있습니다. 이름이 겹치면 `이름-1`, `이름-2`로 저장합니다.
- 다크 모드를 지원합니다(시스템 / 라이트 / 다크).
- 영어·한국어 화면을 제공합니다. 시스템 언어를 따르며, 설정 → 일반 → 언어에서 바꿀 수 있습니다(다음 실행부터 적용).

## 사용법

1. **새 다운로드**에 링크를 붙여 넣으면 정보가 나타납니다. **다운로드** 또는 **대기열에 추가**를 누릅니다.
2. 형식, 해상도, 파일 이름 같은 옵션은 툴바 오른쪽의 옵션 버튼(Windows는 화면 오른쪽 위의 **다운로드 옵션**)으로 엽니다. 바꾼 옵션은 그다음에 추가하는 다운로드부터 적용됩니다.
3. 저장 폴더(기본 macOS `~/Movies`, Windows `동영상` 폴더), 동시 다운로드 수, 화면 모드, 언어는 설정(⌘, / Ctrl+,)에서 바꿉니다.

## macOS

### 빌드

macOS 14 이상과 Xcode(16 이상)가 필요합니다.

```bash
cd MacApp
./scripts/build-app.sh
open "build/YouTube Playlist Downloader.app"
```

- 처음 빌드할 때 yt-dlp, deno, ffmpeg, ffprobe를 받아 `.build/tools-cache`에 저장하고 앱 번들에 넣습니다.
  도구를 새로 받으려면 `--refresh-tools`를 붙여 실행합니다.
- yt-dlp는 앱이 하루 한 번 자동으로 업데이트합니다(설정 → 구성 요소). 업데이트한 파일은 `~/Library/Application Support/YoutubePlaylistDownloader`에 저장됩니다.
- 로직 테스트는 `swift test`로 실행합니다. Command Line Tools의 SwiftPM이 동작하지 않으면 Xcode 툴체인을 지정합니다.
  ```bash
  DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer swift test
  ```
- 로고를 바꿨다면 `./scripts/make-icon.sh`를 실행합니다(`Resources/AppIcon.svg` → `AppIcon.icns`, rsvg-convert 필요).

### 알아 둘 점

- 빌드한 Mac의 아키텍처(Apple Silicon 또는 Intel)용 번들만 만듭니다.
- 앱 크기는 약 330MB이고, 대부분 함께 넣은 yt-dlp·ffmpeg·deno입니다.
- 내 Mac에서 쓰는 임시(ad-hoc) 서명입니다. 다른 사람에게 배포하려면 Developer ID 서명과 공증이 필요합니다.
- 원본 WPF 앱과 다른 점(Windows 앱도 같습니다):
  - 화면 언어는 영어·한국어만 제공합니다.
  - 강조 색 테마와 Windows 설치형 업데이트는 없습니다.
  - YouTube Music 자동 생성 곡의 파일 이름을 "아티스트 - 제목"으로 바꾸는 기능이 없습니다.

## Windows

Windows 10 1809 이상 또는 Windows 11에서 실행됩니다. 기본은 x64이고, Arm64용으로도 빌드할 수 있습니다.
.NET이나 Windows App SDK를 따로 설치할 필요가 없습니다(실행 파일 폴더에 함께 들어 있습니다).

### 받기

Windows에서 직접 빌드하지 않아도 GitHub Actions가 빌드한 결과물을 받을 수 있습니다.

1. 저장소의 **Actions** → **Windows** 워크플로를 엽니다. `master`에 `WindowsApp/`이 바뀐 커밋을 푸시하면 자동으로 실행되고, **Run workflow**로 직접 실행할 수도 있습니다.
2. 끝난 실행 페이지 아래 **Artifacts**에서 `YoutubePlaylistDownloader-win-x64`를 받아 압축을 풉니다.
3. `YoutubePlaylistDownloader\YoutubePlaylistDownloader.exe`를 실행합니다. 폴더째로 원하는 곳에 두면 됩니다(설치 과정 없음).

### 빌드

.NET 10 SDK가 필요합니다. Visual Studio는 필요하지 않습니다.

```powershell
dotnet publish WindowsApp/App/App.csproj -c Release -r win-x64 -o out/YoutubePlaylistDownloader
```

- 결과는 `out/YoutubePlaylistDownloader`에 생깁니다. 이 폴더 전체가 앱입니다.
- 게시할 때 yt-dlp, ffmpeg, ffprobe, deno를 받아 `tools\`에 함께 넣습니다. 받은 파일은 `WindowsApp/App/tools-cache`에 저장해 다음 빌드에 다시 씁니다.
  도구를 새로 받으려면 이 폴더를 지웁니다. 도구 없이 게시하려면 `-p:BundleTools=false`를 붙입니다.
- Arm64용은 `-r win-arm64`로 게시합니다.
- 로직 테스트는 `dotnet test WindowsApp/Core.Tests`로 실행합니다(macOS에서도 됩니다).
- macOS에서도 `dotnet build`·`dotnet publish`로 컴파일은 확인할 수 있지만, 실행할 수 있는 앱은 Windows(또는 GitHub Actions)에서 빌드해야 합니다.

### 알아 둘 점

- 설정, 업데이트한 yt-dlp, 오류 기록(`log.txt`)은 `%LOCALAPPDATA%\YoutubePlaylistDownloader`에 저장됩니다.
- yt-dlp는 앱이 하루 한 번 자동으로 업데이트합니다(설정 → 구성 요소).
- 앱 폴더 크기는 600MB가 넘고, 대부분 함께 넣은 ffmpeg·ffprobe·deno·yt-dlp입니다.
- 서명하지 않은 실행 파일이라 처음 실행할 때 SmartScreen이 "Windows의 PC 보호" 창을 띄울 수 있습니다. **추가 정보** → **실행**을 누릅니다.
- 한 번에 하나만 실행됩니다. 다시 실행하면 이미 열린 창이 앞으로 나옵니다.
- macOS 앱과 다른 점:
  - 설정은 따로 창을 띄우지 않고 왼쪽 목록의 한 화면으로 엽니다.
  - 다운로드 목록에서 작업을 펼치면 항목들이 작업 아래의 작은 목록 안에서 따로 스크롤됩니다.
  - 로고와 제목 영역이 없습니다(화면 제목만 보여 줍니다).
  - 화면이 바뀔 때 애니메이션이 없고, 끝난 작업의 진행 막대는 초록색이 아니라 강조 색입니다.
  - MP4는 항상 H.264라 최대 1080p입니다(위 참고).
  - 중지하면 yt-dlp에 중단 신호를 보내는 대신 yt-dlp와 ffmpeg 프로세스를 바로 끝냅니다(Windows에는 같은 신호가 없습니다).
  - 파일 이름은 Windows 규칙에 맞게 정리합니다(`< > : " / \ | ? *` 등을 `_`로 바꾸고, `CON`·`NUL` 같은 예약된 이름을 피하며, 경로 길이 제한 때문에 100자까지만 씁니다).

## 구조

| 경로 | 내용 |
| --- | --- |
| `MacApp/Sources/YPDCore` | UI와 무관한 로직: 옵션, 링크 정리, 파일 이름 패턴, 제목 분석(태그), yt-dlp 인자, 진행률 해석 |
| `MacApp/Sources/YoutubePlaylistDownloader/Services` | 프로세스 실행, 도구 찾기·업데이트, 다운로드 대기열, 설정 저장 |
| `MacApp/Sources/YoutubePlaylistDownloader/Views` | SwiftUI 화면 |
| `MacApp/Resources` | Info.plist, 아이콘(SVG/ICNS), 번역 문자열 |
| `MacApp/scripts` | 앱 번들 빌드, 아이콘 생성 |
| `WindowsApp/Core` | Windows 앱의 UI와 무관한 로직과 서비스(`YPDCore`와 Services를 옮긴 것, 번역 문자열 포함) |
| `WindowsApp/Core.Tests` | `Core` 테스트(xunit) |
| `WindowsApp/App` | WinUI 3 화면(XAML 없이 C# 코드로 작성), 아이콘, 도구 동봉(`App.csproj`의 `BundleTools`) |
| `.github/workflows/windows.yml` | Windows 앱 테스트·빌드(GitHub Actions) |

## 라이선스

원본 프로젝트와 같은 Apache License 2.0입니다([LICENSE](LICENSE)).
