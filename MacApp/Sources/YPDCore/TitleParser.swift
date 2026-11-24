import Foundation

/// 영상 제목에서 장르, 아티스트, 곡 제목을 뽑습니다. 기존 Windows 버전의 규칙을 옮긴 것입니다.
/// 예: "[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]"
///   → title "Infected Mushroom - Spitfire", genre "Dubstep", artists ["Infected Mushroom"], songTitle "Spitfire"
public struct ParsedTitle: Equatable, Sendable {
    public var title: String
    public var genre: String
    public var artists: [String]
    public var songTitle: String?

    public var artist: String { artists.joined(separator: ", ") }
}

public enum TitleParser {
    static let ignoredGenres = ["download", "out now", "monstercat", "video", "lyric", "release", "ncs", "records", "official", "audio", "mv"]
    static let artistSeparators = ["feat.", "Feat.", "feat", "ft.", " ft ", "&", " x ", " X "]

    public static func parse(_ rawTitle: String) -> ParsedTitle {
        var title = rawTitle.replacingOccurrences(of: "—", with: "-")

        // 첫 번째 [ ] 안의 글자를 장르로 봅니다.
        let pieces = title.components(separatedBy: CharacterSet(charactersIn: "[]"))
        var genre = pieces.count > 1 ? pieces[1] : ""
        if genre.count >= title.count { genre = "" }

        if !genre.trimmingCharacters(in: .whitespaces).isEmpty {
            title = title.replacingOccurrences(of: "[\(genre)]", with: "")
            // 남은 대괄호 묶음(보통 "[레이블 Release]")도 하나 지웁니다.
            let rest = title.components(separatedBy: CharacterSet(charactersIn: "[]【】"))
            if rest.count > 1 {
                let extra = rest[1]
                if !extra.trimmingCharacters(in: .whitespaces).isEmpty {
                    title = title.replacingOccurrences(of: "[\(extra)]", with: "")
                    title = title.replacingOccurrences(of: "【\(extra)】", with: "")
                }
            }
        }

        title = String(title.drop(while: { " -[]".contains($0) }))
            .trimmingCharacters(in: .whitespaces)

        let lowerGenre = genre.lowercased()
        if ignoredGenres.contains(where: lowerGenre.contains) { genre = "" }
        genre = genre.trimmingCharacters(in: .whitespaces)

        var result = ParsedTitle(title: title, genre: genre, artists: [], songTitle: nil)
        if let (artists, song) = splitArtistAndSong(title) {
            result.artists = artists
            result.songTitle = song
        }
        return result
    }

    /// "아티스트 - 곡" 형식이면 아티스트 목록과 곡 제목, 아니면 nil.
    /// 띄어 쓴 대시만 구분자로 봅니다. "Jay-Z", "Re-mix" 같은 단어 속 대시에서 자르지 않기 위해서입니다.
    static func splitArtistAndSong(_ title: String) -> ([String], String)? {
        let trimSet = CharacterSet(charactersIn: " -–")
        guard let dash = title.range(of: " - ") ?? title.range(of: " – ") else { return nil }
        let song = String(title[dash.upperBound...]).trimmingCharacters(in: trimSet)
        let artistPart = String(title[..<dash.lowerBound]).trimmingCharacters(in: trimSet)
        guard !song.isEmpty, !artistPart.isEmpty else { return nil }

        var artists = [artistPart]
        for separator in artistSeparators {
            artists = artists.flatMap { $0.components(separatedBy: separator) }
        }
        artists = artists.map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        guard !artists.isEmpty else { return nil }
        return (artists, song)
    }
}
