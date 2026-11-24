import Foundation

public enum YTDLPEvent: Equatable, Sendable {
    public enum StreamKind: Sendable { case video, audio, muxed }

    case download(kind: StreamKind, fraction: Double?, speed: Double?)
    case postprocess(name: String, started: Bool)
    case file(String)
    case error(String)

    /// 한 줄을 해석합니다. 관심 없는 줄이면 nil.
    public static func parse(_ line: String) -> YTDLPEvent? {
        if let body = line.dropPrefix(YTDLPArguments.downloadMarker) {
            let f = body.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
            guard f.count >= 8 else { return nil }
            let vcodec = f[6], acodec = f[7]
            let hasVideo = isRealCodec(vcodec), hasAudio = isRealCodec(acodec)
            // 자막 파일은 진행률 계산에서 뺍니다. 코덱 정보를 주지 않는 사이트의 영상은 한 덩어리로 봅니다.
            if !hasVideo && !hasAudio && (f.count < 9 || subtitleExtensions.contains(f[8].lowercased())) { return nil }
            let kind: StreamKind = hasVideo == hasAudio ? .muxed : (hasVideo ? .video : .audio)

            let downloaded = number(f[0])
            let total = number(f[1]) ?? number(f[2])
            var fraction: Double?
            if let downloaded, let total, total > 0 {
                fraction = downloaded / total
            } else if let index = number(f[4]), let count = number(f[5]), count > 0 {
                fraction = index / count
            }
            return .download(kind: kind, fraction: fraction.map { min(max($0, 0), 1) }, speed: number(f[3]))
        }
        if let body = line.dropPrefix(YTDLPArguments.postprocessMarker) {
            let f = body.split(separator: "|", omittingEmptySubsequences: false).map(String.init)
            guard f.count >= 2 else { return nil }
            return .postprocess(name: f[0], started: f[1] == "started" || f[1] == "processing")
        }
        if let body = line.dropPrefix(YTDLPArguments.fileMarker) {
            return body.isEmpty ? nil : .file(String(body))
        }
        if let body = line.dropPrefix("ERROR:") {
            return .error(body.trimmingCharacters(in: .whitespaces))
        }
        return nil
    }

    static let subtitleExtensions: Set = ["vtt", "srt", "ass", "ssa", "ttml", "srv1", "srv2", "srv3", "json3", "lrc"]

    static func isRealCodec(_ value: String) -> Bool {
        !value.isEmpty && value != "NA" && value != "none" && value != "None"
    }

    static func number(_ value: String) -> Double? {
        guard value != "NA", value != "None", let n = Double(value), n.isFinite else { return nil }
        return n
    }
}

/// 영상·오디오 스트림을 따로 받는 경우에도 한 개의 진행률(0~1)로 합칩니다. 값은 줄어들지 않습니다.
public struct ProgressTracker: Sendable {
    private var videoFraction = 0.0
    private var audioFraction = 0.0
    private var sawVideo = false
    private var overall = 0.0

    public init() {}

    public mutating func update(kind: YTDLPEvent.StreamKind, fraction: Double) -> Double {
        switch kind {
        case .muxed:
            videoFraction = fraction
            audioFraction = fraction
        case .video:
            sawVideo = true
            videoFraction = fraction
        case .audio:
            audioFraction = fraction
        }
        // 영상 스트림이 대부분의 용량을 차지합니다.
        let value = sawVideo ? videoFraction * 0.85 + audioFraction * 0.15 : (kind == .muxed ? videoFraction : audioFraction)
        overall = max(overall, min(value, 1))
        return overall
    }
}

extension String {
    func dropPrefix(_ prefix: String) -> Substring? {
        hasPrefix(prefix) ? dropFirst(prefix.count) : nil
    }
}
