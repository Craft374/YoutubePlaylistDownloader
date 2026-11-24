// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "YoutubePlaylistDownloader",
    platforms: [.macOS(.v14)],
    targets: [
        // UI 없는 로직(옵션, 파일 이름, yt-dlp 인자, 진행률 파싱)은 테스트할 수 있도록 분리합니다.
        .target(name: "YPDCore"),
        .executableTarget(name: "YoutubePlaylistDownloader", dependencies: ["YPDCore"]),
        .testTarget(name: "YPDCoreTests", dependencies: ["YPDCore"]),
    ],
    swiftLanguageModes: [.v5]
)
