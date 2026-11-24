import Foundation

public struct ToolPaths: Equatable, Sendable {
    public var ytdlp: URL
    public var ffmpeg: URL?
    /// yt-dlp가 유튜브 서명 문제를 풀 때 쓰는 JavaScript 런타임. 이름은 "deno" 또는 "node".
    public var jsRuntime: (name: String, path: URL)?

    public init(ytdlp: URL, ffmpeg: URL?, jsRuntime: (name: String, path: URL)?) {
        self.ytdlp = ytdlp
        self.ffmpeg = ffmpeg
        self.jsRuntime = jsRuntime
    }

    public static func == (a: ToolPaths, b: ToolPaths) -> Bool {
        a.ytdlp == b.ytdlp && a.ffmpeg == b.ffmpeg && a.jsRuntime?.name == b.jsRuntime?.name && a.jsRuntime?.path == b.jsRuntime?.path
    }
}

/// 오디오 파일에 넣을 태그. nil인 값은 yt-dlp가 채운 기본값을 그대로 둡니다.
public struct AudioTags: Equatable, Sendable {
    public var title: String?
    public var artist: String?
    public var genre: String?
    public var album: String?
    public var albumArtist: String?
    public var track: Int?

    public init(title: String? = nil, artist: String? = nil, genre: String? = nil, album: String? = nil, albumArtist: String? = nil, track: Int? = nil) {
        self.title = title
        self.artist = artist
        self.genre = genre
        self.album = album
        self.albumArtist = albumArtist
        self.track = track
    }

    /// 제목이 "아티스트 - 곡" 형식이면 제목·아티스트·장르와 재생목록 정보(앨범, 트랙 번호)를 채웁니다.
    /// 형식이 아니면 트랙 번호만 넣고 나머지는 yt-dlp에 맡깁니다. 유튜브 뮤직 자동 생성 영상은 yt-dlp가
    /// 설명란에서 아티스트·앨범을 읽어 오므로 덮어쓰지 않습니다.
    public static func make(entry: MediaEntry, collectionTitle: String?, collectionUploader: String?) -> AudioTags {
        let parsed = TitleParser.parse(entry.title)
        var tags = AudioTags(track: collectionTitle == nil ? nil : entry.index)
        // 유튜브는 장르를 주지 않으므로 제목의 [장르]는 항상 넣습니다.
        tags.genre = parsed.genre.isEmpty ? nil : parsed.genre
        // shortcut: "아티스트 - 곡" 형식이 아니면 앨범(재생목록 이름)을 넣지 않습니다. 유튜브 뮤직 자동 생성 곡의
        // 진짜 앨범을 덮어쓰지 않기 위해서입니다. 일반 영상에도 앨범이 필요해지면 yt-dlp album 값이 없을 때만 넣도록 바꿉니다.
        if let song = parsed.songTitle {
            tags.title = song
            tags.artist = parsed.artist
            tags.album = collectionTitle
            tags.albumArtist = collectionTitle == nil ? nil : collectionUploader
        }
        return tags
    }
}

public struct DownloadRequest: Sendable {
    public var url: String
    public var options: DownloadOptions
    public var outputDirectory: URL
    /// 확장자 없는 최종 파일 이름. 이미 정리·중복 처리가 끝난 값입니다.
    public var baseName: String
    public var tempDirectory: URL
    public var tags: AudioTags?

    public init(url: String, options: DownloadOptions, outputDirectory: URL, baseName: String, tempDirectory: URL, tags: AudioTags?) {
        self.url = url
        self.options = options
        self.outputDirectory = outputDirectory
        self.baseName = baseName
        self.tempDirectory = tempDirectory
        self.tags = tags
    }
}

public enum YTDLPArguments {
    public static let downloadMarker = "[ypd-dl]"
    public static let postprocessMarker = "[ypd-pp]"
    public static let fileMarker = "[ypd-file]"

    static func common(_ tools: ToolPaths) -> [String] {
        var args = ["--ignore-config", "--no-warnings", "--color", "never"]
        if let js = tools.jsRuntime {
            if js.name != "deno" { args += ["--no-js-runtimes"] }
            args += ["--js-runtimes", "\(js.name):\(js.path.path)"]
        }
        if let ffmpeg = tools.ffmpeg {
            args += ["--ffmpeg-location", ffmpeg.path]
        }
        return args
    }

    /// 미리보기용: 재생목록 항목은 펼치지 않고 목록만 받습니다.
    public static func resolve(url: String, tools: ToolPaths) -> [String] {
        // 비공개·삭제된 영상은 목록에서 뺍니다(받으면 항상 실패합니다).
        common(tools) + ["-J", "--flat-playlist", "--compat-options", "no-youtube-unavailable-videos", "--", url]
    }

    /// 최종 파일 확장자를 미리 알 수 있으면 그 값. 원본 오디오는 받은 뒤에야 알 수 있어 후보 목록을 줍니다.
    public static func expectedExtensions(_ options: DownloadOptions) -> [String] {
        switch options.mode {
        case .video: return [options.videoContainer.rawValue]
        case .audio: return [options.audioFormat.fileExtension]
        case .audioOriginal: return ["m4a", "webm", "opus", "mp4"]
        }
    }

    public static func download(_ request: DownloadRequest, tools: ToolPaths) -> [String] {
        let o = request.options
        var args = common(tools)
        args += [
            "--newline", "--progress", "--progress-delta", "0.2",
            "--progress-template",
            "download:\(downloadMarker)%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s",
            "--progress-template", "postprocess:\(postprocessMarker)%(progress.postprocessor)s|%(progress.status)s",
            "--print", "after_move:\(fileMarker)%(filepath)s",
            "--no-playlist", "--no-mtime",
            "-P", "home:\(request.outputDirectory.path)",
            "-P", "temp:\(request.tempDirectory.path)",
            // 출력 템플릿에서 %는 특수 문자라 이름 안의 %는 %%로 바꿉니다.
            "-o", request.baseName.replacingOccurrences(of: "%", with: "%%") + ".%(ext)s",
        ]

        let language = o.audioLanguage.trimmingCharacters(in: .whitespaces)
        let langFilter = language.isEmpty ? "" : "[language^=\(language)]"

        switch o.mode {
        case .video:
            let format = langFilter.isEmpty ? "bv*+ba/b" : "bv*+ba\(langFilter)/bv*+ba/b"
            // 해상도 제한이 먼저, 같은 해상도 안에서 프레임 레이트를 봅니다.
            var sort = o.preferHighestFPS ? ["res:\(o.maxHeight)", "fps"] : ["res:\(o.maxHeight)"]
            // QuickTime에서 재생되도록 mp4는 H.264/AAC를 우선합니다(해상도가 더 중요).
            if o.videoContainer == .mp4 { sort += ["vcodec:h264", "acodec:aac"] }
            args += ["-f", format, "-S", sort.joined(separator: ","),
                     "--merge-output-format", o.videoContainer.rawValue,
                     "--remux-video", o.videoContainer.rawValue]
            if o.downloadSubtitles {
                let lang = o.subtitleLanguage.trimmingCharacters(in: .whitespaces)
                // "-orig"는 같은 자동 자막의 중복이라 뺍니다. 넣은 뒤 자막 파일은 지우고(no-keep-subs),
                // 자막만 실패했을 때 영상까지 실패하지 않게 합니다(단일 영상이라 실제 다운로드 오류는 그대로 실패 처리됩니다).
                args += ["--write-subs", "--write-auto-subs",
                         "--sub-langs", "\(NSRegularExpression.escapedPattern(for: lang.isEmpty ? "en" : lang))(-[A-Za-z]+)?,-.*-orig",
                         "--embed-subs", "--compat-options", "no-keep-subs", "--ignore-errors"]
            }

        case .audio:
            let format = langFilter.isEmpty ? "ba/b" : "ba\(langFilter)/ba/b"
            args += ["-f", format, "-x", "--audio-format", o.audioFormat.ytdlpName]
            if o.setBitrate, !o.audioFormat.isLossless, o.bitrateKbps > 0 {
                args += ["--audio-quality", "\(o.bitrateKbps)K"]
            }
            if o.tagAudio {
                args += tagging(request.tags, coverArt: o.audioFormat.supportsCoverArt, mp3: o.audioFormat == .mp3)
            }

        case .audioOriginal:
            // 커버를 넣을 수 있는 m4a를 우선합니다.
            let format = langFilter.isEmpty ? "ba[ext=m4a]/ba/b" : "ba\(langFilter)[ext=m4a]/ba\(langFilter)/ba[ext=m4a]/ba/b"
            args += ["-f", format]
            if o.tagAudio {
                // webm에는 커버를 넣을 수 없어 opus(같은 코덱)로 옮겨 담습니다.
                args += ["--remux-video", "webm>opus"]
                args += tagging(request.tags, coverArt: true, mp3: false)
            }
        }

        args += ["--", request.url]
        return args
    }

    static func tagging(_ tags: AudioTags?, coverArt: Bool, mp3: Bool) -> [String] {
        var args = ["--embed-metadata"]
        if coverArt {
            // 썸네일을 정사각형으로 잘라 앨범 커버로 넣습니다. jpg로 맞추면 원래 jpg인 썸네일은 변환(=자르기)을
            // 건너뛰므로, 항상 변환이 일어나는 png로 맞춥니다.
            args += ["--embed-thumbnail", "--convert-thumbnails", "png",
                     "--ppa", "ThumbnailsConvertor+ffmpeg_o:-vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\""]
        }
        var metadata: [String] = []
        if mp3 { metadata += ["-id3v2_version", "3"] }
        if let tags {
            let pairs: [(String, String?)] = [
                ("title", tags.title), ("artist", tags.artist), ("genre", tags.genre),
                ("album", tags.album), ("album_artist", tags.albumArtist), ("track", tags.track.map(String.init)),
            ]
            for (key, value) in pairs {
                if let value, !value.isEmpty { metadata += ["-metadata", "\(key)=\(value)"] }
            }
        }
        // yt-dlp는 이 문자열을 셸 규칙(shlex)으로 나눕니다. 값마다 작은따옴표로 감싸 공백·따옴표를 보존합니다.
        // 이 인자는 yt-dlp 자체 -metadata 뒤에 붙어 같은 키를 덮어씁니다.
        if !metadata.isEmpty {
            args += ["--ppa", "Metadata+ffmpeg_o:" + metadata.map(shellQuote).joined(separator: " ")]
        }
        return args
    }

    public static func shellQuote(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\"'\"'") + "'"
    }
}
