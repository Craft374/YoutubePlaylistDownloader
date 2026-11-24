import Foundation

/// 무엇을 받을지: 동영상, 변환한 오디오, 변환하지 않은 원본 오디오.
public enum MediaMode: String, Codable, CaseIterable, Sendable {
    case video
    case audio
    case audioOriginal
}

public enum VideoContainer: String, Codable, CaseIterable, Sendable {
    case mp4
    case mkv
}

public enum AudioFormat: String, Codable, CaseIterable, Sendable {
    case mp3, m4a, opus, ogg, flac, wav, alac

    /// yt-dlp `--audio-format` 값.
    public var ytdlpName: String { self == .ogg ? "vorbis" : rawValue }

    public var fileExtension: String {
        switch self {
        case .alac: return "m4a"
        default: return rawValue
        }
    }

    public var isLossless: Bool { self == .flac || self == .wav || self == .alac }

    /// yt-dlp가 커버 이미지를 넣을 수 있는 형식인지. wav는 지원하지 않아 넣으면 후처리 오류가 납니다.
    public var supportsCoverArt: Bool { self != .wav }
}

public enum Resolution {
    public static let heights = [144, 240, 360, 480, 720, 1080, 1440, 2160, 4320]
}

public struct DownloadOptions: Codable, Equatable, Sendable {
    public var mode: MediaMode = .video

    // 동영상
    public var videoContainer: VideoContainer = .mp4
    public var maxHeight: Int = 1080
    public var preferHighestFPS = false
    public var downloadSubtitles = false
    public var subtitleLanguage = "en"
    /// 빈 문자열이면 기본 오디오 트랙.
    public var audioLanguage = ""

    // 오디오
    public var audioFormat: AudioFormat = .mp3
    public var setBitrate = false
    public var bitrateKbps = 192
    public var tagAudio = true

    // 공통
    public var savePlaylistsInSubfolders = false
    public var openFolderWhenDone = false
    public var filenamePattern = DownloadOptions.defaultPattern
    public var skipExisting = false

    // 재생목록 범위와 길이 필터
    public var useRange = false
    public var rangeStart = 1
    /// 0이면 끝까지.
    public var rangeEnd = 0
    public var filterByLength = false
    public var filterLonger = true
    public var filterMinutes: Double = 4

    public static let defaultPattern = "$title"

    public init() {}

    // 저장된 JSON에 없는 키는 기본값을 씁니다. 새 옵션을 추가해도 기존 설정이 지워지지 않습니다.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        let d = DownloadOptions()
        func v<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T {
            (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback
        }
        mode = v(.mode, d.mode)
        videoContainer = v(.videoContainer, d.videoContainer)
        maxHeight = v(.maxHeight, d.maxHeight)
        preferHighestFPS = v(.preferHighestFPS, d.preferHighestFPS)
        downloadSubtitles = v(.downloadSubtitles, d.downloadSubtitles)
        subtitleLanguage = v(.subtitleLanguage, d.subtitleLanguage)
        audioLanguage = v(.audioLanguage, d.audioLanguage)
        audioFormat = v(.audioFormat, d.audioFormat)
        setBitrate = v(.setBitrate, d.setBitrate)
        bitrateKbps = v(.bitrateKbps, d.bitrateKbps)
        tagAudio = v(.tagAudio, d.tagAudio)
        savePlaylistsInSubfolders = v(.savePlaylistsInSubfolders, d.savePlaylistsInSubfolders)
        openFolderWhenDone = v(.openFolderWhenDone, d.openFolderWhenDone)
        filenamePattern = v(.filenamePattern, d.filenamePattern)
        skipExisting = v(.skipExisting, d.skipExisting)
        useRange = v(.useRange, d.useRange)
        rangeStart = v(.rangeStart, d.rangeStart)
        rangeEnd = v(.rangeEnd, d.rangeEnd)
        filterByLength = v(.filterByLength, d.filterByLength)
        filterLonger = v(.filterLonger, d.filterLonger)
        filterMinutes = v(.filterMinutes, d.filterMinutes)
    }

    /// 받을 항목. 범위와 길이 필터는 재생목록·채널에만 적용합니다(단일 영상에는 적용하지 않음).
    public func selectEntries(from source: MediaSource) -> [MediaEntry] {
        source.kind == .video ? source.entries : selectEntries(source.entries)
    }

    /// 재생목록 범위(1부터 시작, 끝 포함)와 길이 필터를 적용한 항목.
    /// 길이를 모르는 항목(라이브 등)은 필터에서 제외하지 않습니다.
    public func selectEntries(_ entries: [MediaEntry]) -> [MediaEntry] {
        var result = entries
        if useRange {
            let start = max(1, rangeStart)
            let end = rangeEnd <= 0 ? Int.max : rangeEnd
            result = result.filter { $0.index >= start && $0.index <= end }
        }
        if filterByLength {
            let limit = filterMinutes * 60
            result = result.filter { entry in
                guard let duration = entry.duration else { return true }
                return filterLonger ? duration > limit : duration < limit
            }
        }
        return result
    }
}
