import Foundation

public enum SourceURL {
    /// 사용자가 붙여 넣은 텍스트를 yt-dlp에 넘길 주소로 바꿉니다. 주소로 볼 수 없으면 nil.
    /// 11자 영상 ID만 넣으면 watch 주소로 만듭니다. 채널 주소는 그대로 넘겨 yt-dlp가 동영상·Shorts·라이브 탭을 모두 모으게 합니다.
    public static func normalize(_ raw: String) -> String? {
        var text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty, !text.contains(where: \.isWhitespace) else { return nil }

        if text.count == 11, text.allSatisfy({ $0.isASCII && ($0.isLetter || $0.isNumber || $0 == "-" || $0 == "_") }) {
            return "https://www.youtube.com/watch?v=\(text)"
        }
        if !text.lowercased().hasPrefix("http://") && !text.lowercased().hasPrefix("https://") {
            text = "https://" + text
        }
        guard let components = URLComponents(string: text),
              components.host?.contains(".") == true,
              components.scheme?.lowercased() == "https" || components.scheme?.lowercased() == "http"
        else { return nil }
        return components.string
    }

    /// 일괄 다운로드 입력: 줄마다 하나씩, 빈 줄은 건너뜁니다.
    public static func lines(_ text: String) -> [String] {
        text.split(whereSeparator: \.isNewline)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
    }
}
