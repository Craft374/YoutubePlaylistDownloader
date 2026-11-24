import Foundation

public struct MediaEntry: Identifiable, Hashable, Sendable {
    /// 원본 목록 안의 위치(1부터). 같은 영상이 두 번 들어 있어도 구분되도록 id에 포함합니다.
    public var id: String { "\(index)-\(videoID)" }
    public let videoID: String
    public let url: String
    public let title: String
    public let channel: String?
    public let duration: Double?
    public let thumbnail: URL?
    public let index: Int

    public init(videoID: String, url: String, title: String, channel: String?, duration: Double?, thumbnail: URL?, index: Int) {
        self.videoID = videoID
        self.url = url
        self.title = title
        self.channel = channel
        self.duration = duration
        self.thumbnail = thumbnail
        self.index = index
    }
}

public struct MediaSource: Sendable {
    public enum Kind: String, Sendable { case video, playlist, channel }

    public let kind: Kind
    public let title: String
    public let uploader: String?
    public let thumbnail: URL?
    public let viewCount: Int?
    public let entries: [MediaEntry]

    public init(kind: Kind, title: String, uploader: String?, thumbnail: URL?, viewCount: Int?, entries: [MediaEntry]) {
        self.kind = kind
        self.title = title
        self.uploader = uploader
        self.thumbnail = thumbnail
        self.viewCount = viewCount
        self.entries = entries
    }

    /// 재생목록·채널이면 그 이름, 단일 영상이면 nil. 하위 폴더 이름과 `$playlist`에 씁니다.
    public var collectionTitle: String? { kind == .video ? nil : title }

    public enum ParseError: Error { case invalidJSON }

    /// `yt-dlp -J --flat-playlist` 출력.
    public static func parse(_ data: Data) throws -> MediaSource {
        guard let root = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw ParseError.invalidJSON
        }
        let type = root["_type"] as? String
        guard type == "playlist" || type == "multi_video" else {
            let entry = makeEntry(root, index: 1)
            return MediaSource(kind: .video,
                               title: entry.title,
                               uploader: entry.channel,
                               thumbnail: bestThumbnail(root, videoID: entry.videoID, large: true),
                               viewCount: root["view_count"] as? Int,
                               entries: [entry])
        }

        // 채널 첫 화면 주소는 탭(동영상, Shorts, 라이브)마다 재생목록이 하나씩 들어옵니다. 한 단계 펼칩니다.
        let rawEntries = ((root["entries"] as? [Any]) ?? []).flatMap { raw -> [Any] in
            if let dict = raw as? [String: Any], dict["_type"] as? String == "playlist", let inner = dict["entries"] as? [Any] {
                return inner
            }
            return [raw]
        }
        var entries: [MediaEntry] = []
        entries.reserveCapacity(rawEntries.count)
        for (offset, raw) in rawEntries.enumerated() {
            // 비공개·삭제된 항목은 조회 옵션(no-youtube-unavailable-videos)으로 빠집니다. null 검사는 안전장치입니다.
            guard let dict = raw as? [String: Any], !(string(dict, "id") ?? "").isEmpty else { continue }
            entries.append(makeEntry(dict, index: offset + 1))
        }

        let id = string(root, "id") ?? ""
        let channelID = string(root, "channel_id")
        // 채널 주소는 UC… 아이디로, Topic 채널은 업로드 재생목록 UU…로 옵니다.
        let isChannel = (id.hasPrefix("UC") && (channelID == nil || channelID == id))
            || (channelID.map { $0.hasPrefix("UC") && id == "UU" + $0.dropFirst(2) } ?? false)
        let channelName = string(root, "channel") ?? string(root, "uploader")
        let title = isChannel ? (channelName ?? string(root, "title") ?? id) : (string(root, "title") ?? id)
        return MediaSource(kind: isChannel ? .channel : .playlist,
                           title: title,
                           uploader: channelName,
                           thumbnail: entries.first.flatMap { youtubeThumbnail($0.videoID, large: true) } ?? bestThumbnail(root, videoID: nil, large: true),
                           viewCount: root["view_count"] as? Int,
                           entries: entries)
    }

    private static func makeEntry(_ dict: [String: Any], index: Int) -> MediaEntry {
        let id = string(dict, "id") ?? ""
        let url = string(dict, "webpage_url") ?? string(dict, "url") ?? id
        return MediaEntry(videoID: id,
                          url: url,
                          title: string(dict, "title") ?? id,
                          channel: string(dict, "channel") ?? string(dict, "uploader"),
                          duration: (dict["duration"] as? NSNumber)?.doubleValue,
                          thumbnail: bestThumbnail(dict, videoID: id, large: false),
                          index: index)
    }

    private static func string(_ dict: [String: Any], _ key: String) -> String? {
        guard let value = dict[key] as? String, !value.isEmpty else { return nil }
        return value
    }

    private static func isYouTubeID(_ id: String) -> Bool {
        id.count == 11 && id.allSatisfy { $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "-" || $0 == "_") }
    }

    private static func youtubeThumbnail(_ id: String, large: Bool) -> URL? {
        guard isYouTubeID(id) else { return nil }
        return URL(string: "https://i.ytimg.com/vi/\(id)/\(large ? "hqdefault" : "mqdefault").jpg")
    }

    private static func bestThumbnail(_ dict: [String: Any], videoID: String?, large: Bool) -> URL? {
        // 유튜브는 크기가 정해진 썸네일 주소가 있어 목록용 작은 이미지를 바로 씁니다.
        if let videoID, let url = youtubeThumbnail(videoID, large: large) { return url }
        if let list = dict["thumbnails"] as? [[String: Any]],
           let last = list.last(where: { $0["url"] is String }),
           let url = URL(string: last["url"] as! String) {
            return url
        }
        return string(dict, "thumbnail").flatMap(URL.init(string:))
    }
}
