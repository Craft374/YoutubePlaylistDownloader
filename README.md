# YouTube Playlist Downloader for macOS

[shaked6540/YoutubePlaylistDownloader](https://github.com/shaked6540/YoutubePlaylistDownloader)(Windows, WPF)를 포크해 macOS 전용 네이티브 앱(SwiftUI)으로 다시 만든 버전입니다.
다운로드는 [yt-dlp](https://github.com/yt-dlp/yt-dlp)가, 병합·변환은 FFmpeg가 맡습니다. 앱 코드는 `MacApp/`에 있습니다.

## 기능

- 영상, 재생목록, 채널 링크를 넣으면 정보를 미리 보여 주고 다운로드합니다.
- 일괄 다운로드: 링크를 한 줄에 하나씩 넣습니다.
- 동영상(MP4/MKV, 최대 해상도, 높은 프레임 우선, 자막, 오디오 언어)을 받을 수 있습니다.
- 오디오(MP3/M4A/Opus/Ogg/FLAC/WAV/ALAC, 비트레이트, 태그와 커버 이미지)도 받을 수 있습니다.
- 파일 이름 패턴: `$title $index $artist $songtitle $channel $videoid $playlist $genre`
- 재생목록 범위와 영상 길이 필터를 지원합니다. 이미 있는 파일은 건너뛸 수 있고, 재생목록마다 폴더를 따로 만들 수 있습니다.
- 다크 모드를 지원하고, 영어·한국어 화면을 제공합니다.

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
- 로직 테스트는 `swift test`로 실행합니다.
- 로고를 바꿨다면 `./scripts/make-icon.sh`를 실행합니다(`Resources/AppIcon.svg` → `AppIcon.icns`, rsvg-convert 필요).

## 구조

| 경로 | 내용 |
| --- | --- |
| `MacApp/Sources/YPDCore` | UI와 무관한 로직: 옵션, 링크 정리, 파일 이름 패턴, 제목 분석(태그), yt-dlp 인자, 진행률 해석 |
| `MacApp/Sources/YoutubePlaylistDownloader/Services` | 프로세스 실행, 도구 찾기·업데이트, 다운로드 대기열, 설정 저장 |
| `MacApp/Sources/YoutubePlaylistDownloader/Views` | SwiftUI 화면 |
| `MacApp/Resources` | Info.plist, 아이콘(SVG/ICNS), 번역 문자열 |
| `MacApp/scripts` | 앱 번들 빌드, 아이콘 생성 |
