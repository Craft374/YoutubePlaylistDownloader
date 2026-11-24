namespace YPD.Core;

public static class SourceURL
{
    /// <summary>
    /// 사용자가 붙여 넣은 텍스트를 yt-dlp에 넘길 주소로 바꿉니다. 주소로 볼 수 없으면 null.
    /// 11자 영상 ID만 넣으면 watch 주소로 만듭니다. 채널 주소는 그대로 넘겨 yt-dlp가 동영상·Shorts·라이브 탭을 모두 모으게 합니다.
    /// </summary>
    public static string? Normalize(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace)) return null;

        if (text.Length == 11 && text.All(MediaSource.IsYouTubeIDChar))
            return "https://www.youtube.com/watch?v=" + text;
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = "https://" + text;
        // Uri는 주소를 정규화(호스트 소문자, 끝 '/')하므로 확인에만 쓰고 입력한 글자를 그대로 넘깁니다.
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Host.Contains('.')
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return null;
        return text;
    }

    /// <summary>일괄 다운로드 입력: 줄마다 하나씩, 빈 줄은 건너뜁니다.</summary>
    public static List<string> Lines(string text) =>
        text.Split(['\n', '\r', '\v', '\f', '\u0085', '\u2028', '\u2029'])
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
}
