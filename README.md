# YouTube Playlist Downloader for macOS

[shaked6540/YoutubePlaylistDownloader](https://github.com/shaked6540/YoutubePlaylistDownloader)(Windows, WPF)를 포크해 macOS 전용 네이티브 앱(SwiftUI)으로 다시 만든 버전입니다.
다운로드는 [yt-dlp](https://github.com/yt-dlp/yt-dlp)가, 병합·변환은 FFmpeg가 맡습니다. 앱 코드는 `MacApp/`에 있습니다.

## 기능

- 영상, 재생목록, 채널 링크를 넣으면 정보를 미리 보여 주고 다운로드합니다. 채널은 동영상, Shorts, 지난 라이브를 모두 가져옵니다.
- 일괄 다운로드: 링크를 한 줄에 하나씩 넣습니다. 추가하지 못한 링크는 이유와 함께 보여 주고, 다시 시도·복사·저장할 수 있습니다.
- 다운로드 목록에서 작업마다 진행률과 속도를 보여 줍니다. 항목을 펼쳐 영상별 상태와 오류를 볼 수 있고, 중지·다시 시도·Finder에서 보기를 할 수 있습니다.
- 동영상: MP4/MKV, 최대 해상도, 높은 프레임 레이트 우선, 자막(영상에 넣음), 오디오 언어를 고를 수 있습니다.
  - MP4는 QuickTime·Finder 미리보기·iPhone에서 열리도록 H.264를 받습니다. AV1을 하드웨어로 디코딩하는 Mac(M3 이상)에서는 AV1도 받습니다.
  - 그래서 AV1을 쓸 수 없는 Mac에서는 MP4가 최대 1080p입니다. 그보다 높은 해상도는 MKV로 받으십시오.
- 오디오: MP3/M4A/Opus/Ogg/FLAC/WAV/ALAC로 변환하거나 원본 그대로 받을 수 있습니다. 비트레이트를 고를 수 있고, 태그와 정사각형 커버 이미지를 넣습니다.
  - "아티스트 - 곡" 형식의 제목에서 아티스트·곡 제목·장르를 읽어 태그로 넣습니다.
- 파일 이름 패턴: `$title $index $artist $songtitle $channel $videoid $playlist $genre`
- 재생목록 범위와 영상 길이 필터를 지원합니다(단일 영상에는 적용하지 않습니다).
- 이미 있는 파일을 건너뛸 수 있고, 재생목록마다 폴더를 따로 만들 수 있으며, 끝나면 폴더를 열 수 있습니다. 이름이 겹치면 `이름-1`, `이름-2`로 저장합니다.
- 다크 모드를 지원합니다(시스템 / 라이트 / 다크).
- 영어·한국어 화면을 제공합니다. 시스템 언어를 따르며, 설정 → 일반 → 언어에서 바꿀 수 있습니다(다음 실행부터 적용).

## 사용법

1. **새 다운로드**에 링크를 붙여 넣으면 정보가 나타납니다. **다운로드** 또는 **대기열에 추가**를 누릅니다.
2. 형식, 해상도, 파일 이름 같은 옵션은 툴바 오른쪽의 옵션 버튼으로 엽니다. 바꾼 옵션은 그다음에 추가하는 다운로드부터 적용됩니다.
3. 저장 폴더(기본 `~/Movies`), 동시 다운로드 수, 화면 모드, 언어는 설정(⌘,)에서 바꿉니다.

## 빌드

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

## 알아 둘 점

- 빌드한 Mac의 아키텍처(Apple Silicon 또는 Intel)용 번들만 만듭니다.
- 앱 크기는 약 330MB이고, 대부분 함께 넣은 yt-dlp·ffmpeg·deno입니다.
- 내 Mac에서 쓰는 임시(ad-hoc) 서명입니다. 다른 사람에게 배포하려면 Developer ID 서명과 공증이 필요합니다.
- 기존 Windows 앱과 다른 점:
  - 화면 언어는 영어·한국어만 제공합니다.
  - 강조 색 테마와 Windows 설치형 업데이트는 없습니다.
  - YouTube Music 자동 생성 곡의 파일 이름을 "아티스트 - 제목"으로 바꾸는 기능이 없습니다.

## 구조

| 경로 | 내용 |
| --- | --- |
| `MacApp/Sources/YPDCore` | UI와 무관한 로직: 옵션, 링크 정리, 파일 이름 패턴, 제목 분석(태그), yt-dlp 인자, 진행률 해석 |
| `MacApp/Sources/YoutubePlaylistDownloader/Services` | 프로세스 실행, 도구 찾기·업데이트, 다운로드 대기열, 설정 저장 |
| `MacApp/Sources/YoutubePlaylistDownloader/Views` | SwiftUI 화면 |
| `MacApp/Resources` | Info.plist, 아이콘(SVG/ICNS), 번역 문자열 |
| `MacApp/scripts` | 앱 번들 빌드, 아이콘 생성 |

## 라이선스

원본 프로젝트와 같은 Apache License 2.0입니다([LICENSE](LICENSE)).
