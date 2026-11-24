import Foundation

public enum FilenameBuilder {
    public static let tokens = ["$title", "$index", "$artist", "$songtitle", "$channel", "$videoid", "$playlist", "$genre"]

    /// 파일 이름 패턴을 채운 뒤 macOS에서 쓸 수 있는 이름으로 다듬습니다. 확장자는 붙이지 않습니다.
    /// 토큰은 한 번에 치환하므로, 제목 안에 "$index" 같은 글자가 있어도 다시 바뀌지 않습니다.
    public static func baseName(pattern: String, entry: MediaEntry, collectionTitle: String?) -> String {
        let parsed = TitleParser.parse(entry.title)
        let values: [String: String] = [
            "$title": parsed.title,
            "$index": String(entry.index),
            "$artist": parsed.artist,
            "$songtitle": parsed.songTitle ?? "",
            "$channel": entry.channel ?? "",
            "$videoid": entry.videoID,
            "$playlist": collectionTitle ?? "",
            "$genre": parsed.genre,
        ]

        var output = ""
        var rest = Substring(pattern)
        while let dollar = rest.firstIndex(of: "$") {
            output += rest[..<dollar]
            let tail = rest[dollar...]
            // "$songtitle"이 "$title"보다 길어서 먼저 맞춰 봅니다(긴 토큰 우선).
            if let token = tokens.sorted(by: { $0.count > $1.count }).first(where: { tail.hasPrefix($0) }) {
                output += values[token] ?? ""
                rest = tail.dropFirst(token.count)
            } else {
                output += "$"
                rest = tail.dropFirst()
            }
        }
        output += rest

        var name = sanitize(output)
        // "$artist - $songtitle"처럼 토큰이 비어 "-"만 남는 경우도 이름이 없는 것으로 봅니다.
        if !name.unicodeScalars.contains(where: CharacterSet.alphanumerics.contains) { name = sanitize(parsed.title) }
        if name.isEmpty { name = sanitize(entry.videoID) }
        return name.isEmpty ? "video" : name
    }

    /// `/`, `:`, 제어 문자를 `_`로 바꾸고, 앞쪽 점(숨김 파일)을 없애고, 너무 긴 이름을 자릅니다.
    /// yt-dlp가 임시 접미사를 붙이므로 255바이트 한도보다 넉넉히 짧게 자릅니다.
    public static func sanitize(_ raw: String, maxBytes: Int = 180) -> String {
        var result = ""
        var lastWasReplacement = false
        for scalar in raw.unicodeScalars {
            let invalid = scalar == "/" || scalar == ":" || scalar.properties.generalCategory == .control
            if invalid {
                if !lastWasReplacement { result.unicodeScalars.append("_") }
                lastWasReplacement = true
            } else {
                result.unicodeScalars.append(scalar)
                lastWasReplacement = false
            }
        }
        // yt-dlp는 출력 템플릿의 글자에서 $HOME 같은 환경 변수를 펼칩니다. 실제로 있는 변수 이름 앞의 $만 전각 ＄로 바꿔
        // "Ke$ha", "A$AP" 같은 이름은 그대로 둡니다. 목록은 ProcessRunner.environment가 넘기는 변수와 같아야 합니다.
        result = result.replacingOccurrences(
            of: #"\$(?=\{|(HOME|USER|LOGNAME|TMPDIR|SHELL|PATH|LANG|LC_ALL|PYTHONIOENCODING|PYTHONUTF8)(?![A-Za-z0-9_]))"#,
            with: "＄", options: .regularExpression)
        result = result.trimmingCharacters(in: .whitespacesAndNewlines)
        while result.hasPrefix(".") { result.removeFirst() }
        result = result.trimmingCharacters(in: .whitespaces)

        if result.utf8.count > maxBytes {
            var cut = ""
            for character in result {
                if cut.utf8.count + String(character).utf8.count > maxBytes { break }
                cut.append(character)
            }
            result = cut.trimmingCharacters(in: .whitespaces)
        }
        return result
    }
}

public enum OutputName {
    /// 겹치지 않는 파일 이름을 고릅니다. 건너뛰어야 하면 nil.
    /// - expected: 이번 다운로드가 만들 확장자(건너뛰기 판정용)
    /// - blocking: 같은 이름이 있으면 피해야 하는 확장자(expected 포함)
    /// - isReserved: 지금 다른 다운로드가 쓰기로 한 이름인지
    /// 겹치면 기존 동작대로 "이름-1", "이름-2" … 를 씁니다.
    public static func choose(base: String, expected: [String], blocking: [String], skipExisting: Bool,
                              isReserved: (String) -> Bool, exists: (String, String) -> Bool) -> String? {
        func existsAny(_ name: String, _ extensions: [String]) -> Bool { extensions.contains { exists(name, $0) } }
        func taken(_ name: String) -> Bool { isReserved(name) || existsAny(name, blocking) }

        guard taken(base) else { return base }
        if skipExisting, !isReserved(base), existsAny(base, expected) { return nil }
        var n = 1
        while taken("\(base)-\(n)") { n += 1 }
        return "\(base)-\(n)"
    }
}
