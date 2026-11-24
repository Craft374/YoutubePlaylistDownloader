using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YPD.Core;

public static partial class FilenameBuilder
{
    public static readonly IReadOnlyList<string> Tokens = ["$title", "$index", "$artist", "$songtitle", "$channel", "$videoid", "$playlist", "$genre"];

    // "$songtitle"이 "$title"보다 길어서 먼저 맞춰 봅니다(긴 토큰 우선).
    private static readonly string[] TokensLongestFirst = [.. Tokens.OrderByDescending(t => t.Length)];

    /// <summary>
    /// 파일 이름 패턴을 채운 뒤 Windows에서 쓸 수 있는 이름으로 다듬습니다. 확장자는 붙이지 않습니다.
    /// 토큰은 한 번에 치환하므로, 제목 안에 "$index" 같은 글자가 있어도 다시 바뀌지 않습니다.
    /// </summary>
    public static string BaseName(string pattern, MediaEntry entry, string? collectionTitle, Func<string, bool>? isEnvironmentVariable = null)
    {
        var parsed = TitleParser.Parse(entry.Title);
        var values = new Dictionary<string, string>
        {
            ["$title"] = parsed.Title,
            ["$index"] = entry.Index.ToString(CultureInfo.InvariantCulture),
            ["$artist"] = parsed.Artist,
            ["$songtitle"] = parsed.SongTitle ?? "",
            ["$channel"] = entry.Channel ?? "",
            ["$videoid"] = entry.VideoId,
            ["$playlist"] = collectionTitle ?? "",
            ["$genre"] = parsed.Genre,
        };

        var output = new StringBuilder();
        int i = 0;
        while (i < pattern.Length)
        {
            int dollar = pattern.IndexOf('$', i);
            if (dollar < 0) break;
            output.Append(pattern, i, dollar - i);
            string? token = Array.Find(TokensLongestFirst, t => string.CompareOrdinal(pattern, dollar, t, 0, t.Length) == 0);
            if (token != null)
            {
                output.Append(values[token]);
                i = dollar + token.Length;
            }
            else
            {
                output.Append('$');
                i = dollar + 1;
            }
        }
        output.Append(pattern, i, pattern.Length - i);

        string name = Sanitize(output.ToString(), isEnvironmentVariable: isEnvironmentVariable);
        // "$artist - $songtitle"처럼 토큰이 비어 "-"만 남는 경우도 이름이 없는 것으로 봅니다.
        if (!HasAlphanumeric(name)) name = Sanitize(parsed.Title, isEnvironmentVariable: isEnvironmentVariable);
        if (name.Length == 0) name = Sanitize(entry.VideoId, isEnvironmentVariable: isEnvironmentVariable);
        return name.Length == 0 ? "video" : name;
    }

    // Swift CharacterSet.alphanumerics와 같은 범위: 글자(L*), 결합 문자(M*), 숫자(N*). UnicodeCategory 순서상 0~10입니다.
    private static bool HasAlphanumeric(string name) =>
        name.EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) <= UnicodeCategory.OtherNumber);

    /// <summary>
    /// Windows 파일 이름으로 쓸 수 있게 다듬습니다. 결과는 yt-dlp가 Windows에서 출력 템플릿을 처리할 때(환경 변수·~ 펼치기,
    /// sanitize_path) 바뀌지 않는 이름입니다.
    /// - &lt; &gt; : " / \ | ? * 와 제어 문자는 이어진 묶음마다 '_' 하나로 바꿉니다.
    /// - 앞쪽 점(숨김 파일처럼 보임)과 뒤쪽 점·공백(Windows가 지움)을 없앱니다.
    /// - MAX_PATH(260)를 넘지 않도록 UTF-16 단위로 <paramref name="maxLength"/>까지 자릅니다(글자 단위로, 이모지를 쪼개지 않음).
    /// - 실제로 있는 환경 변수 이름 앞의 $, 이어진 $의 앞쪽, 맨 앞 ~를 전각으로 바꿉니다.
    /// - CON, NUL, COM1 같은 장치 이름에는 '_'를 붙입니다.
    /// </summary>
    /// <param name="isEnvironmentVariable">yt-dlp 프로세스에 그 이름의 환경 변수가 있는지. 기본값은 <see cref="IsEnvironmentVariable"/>.</param>
    public static string Sanitize(string raw, int maxLength = 100, Func<string, bool>? isEnvironmentVariable = null)
    {
        var builder = new StringBuilder(raw.Length);
        bool lastWasReplacement = false;
        foreach (var rune in raw.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || (rune.IsAscii && "<>:\"/\\|?*".Contains((char)rune.Value)))
            {
                if (!lastWasReplacement) builder.Append('_');
                lastWasReplacement = true;
            }
            else
            {
                builder.Append(rune.ToString());
                lastWasReplacement = false;
            }
        }
        string result = TrimEnds(builder.ToString());

        // CON, PRN, AUX, NUL, COM0~9, LPT0~9(¹²³ 포함)는 확장자가 붙어도 장치 이름이라 파일을 만들 수 없습니다.
        // 자르기 전에 붙여야 '_'까지 길이 제한 안에 들어갑니다.
        int dot = result.IndexOf('.');
        string stem = dot < 0 ? result : result[..dot];
        if (ReservedName().IsMatch(stem)) result = stem + "_" + result[stem.Length..];

        if (result.Length > maxLength)
        {
            var cut = new StringBuilder(maxLength);
            var elements = StringInfo.GetTextElementEnumerator(result);
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                if (cut.Length + element.Length > maxLength) break;
                cut.Append(element);
            }
            result = TrimEnds(cut.ToString());
        }

        // yt-dlp는 Windows에서 출력 템플릿을 ntpath.expandvars로 먼저 펼칩니다(%%·$$는 yt-dlp가 보호합니다).
        // 실제로 있는 변수 이름 앞의 $와 ${ 의 $만 전각 ＄로 바꿔 "Ke$ha", "A$AP" 같은 이름은 그대로 둡니다.
        // 자른 뒤에 해야 "$HOMES"가 잘려 "$HOME"이 되는 경우도 막습니다.
        var isVariable = isEnvironmentVariable ?? IsEnvironmentVariable;
        result = DollarName().Replace(result, m =>
            m.Value[1] == '{' || isVariable(m.Value[1..]) ? "＄" + m.Value[1..] : m.Value);
        // yt-dlp의 $$ 보호는 짝으로만 동작해 "$$$"는 "$$"로 줄어듭니다. 이어진 $는 마지막 하나만 남기고 바꿉니다.
        result = DoubleDollar().Replace(result, "＄");
        // ntpath.expanduser는 맨 앞 ~를 (다른) 사용자 폴더 경로로 바꿉니다.
        if (result.StartsWith('~')) result = "～" + result[1..];
        return result;
    }

    /// <summary>
    /// 재생목록 하위 폴더 이름. 폴더는 -P 경로로 넘기는데, yt-dlp는 경로에서는 %%를 보호하지 않고
    /// "%%"→"%"로 줄이고 "%이름%"을 펼칩니다. 그래서 %가 두 개 이상이면 모두 전각으로 바꿉니다.
    /// </summary>
    public static string FolderName(string raw, Func<string, bool>? isEnvironmentVariable = null)
    {
        string name = Sanitize(raw, maxLength: 60, isEnvironmentVariable);
        return name.Count(c => c == '%') >= 2 ? name.Replace('%', '％') : name;
    }

    /// <summary>
    /// yt-dlp 자식 프로세스에 이 이름의 환경 변수가 있는지. 자식은 부모 환경 전체에 PYTHONIOENCODING·PYTHONUTF8을 더해 받고,
    /// PyInstaller로 만든 yt-dlp.exe는 실행 중에 _PYI_… 변수를 더할 수 있어 있는 것으로 봅니다. Windows 환경 변수 이름은 대소문자를 구분하지 않습니다.
    /// </summary>
    public static bool IsEnvironmentVariable(string name) =>
        name.Equals("PYTHONIOENCODING", StringComparison.OrdinalIgnoreCase)
        || name.Equals("PYTHONUTF8", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("_PYI_", StringComparison.OrdinalIgnoreCase)
        || Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>앞뒤 공백, 앞쪽 점, 뒤쪽 점·공백을 없앱니다.</summary>
    private static string TrimEnds(string value)
    {
        value = value.Trim().TrimStart('.').Trim();
        int end = value.Length;
        while (end > 0 && (value[end - 1] == '.' || char.IsWhiteSpace(value[end - 1]))) end--;
        return value[..end];
    }

    // ntpath.expandvars의 변수 이름 규칙: $ 뒤의 [-\w]+ (ASCII, 가장 길게) 또는 ${.
    [GeneratedRegex(@"\$(\{|[-A-Za-z0-9_]+)")]
    private static partial Regex DollarName();

    [GeneratedRegex(@"\$(?=\$)")]
    private static partial Regex DoubleDollar();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
}

public static class OutputName
{
    /// <summary>
    /// 겹치지 않는 파일 이름을 고릅니다. 건너뛰어야 하면 null. 겹치면 기존 동작대로 "이름-1", "이름-2" … 를 씁니다.
    /// </summary>
    /// <param name="expected">이번 다운로드가 만들 확장자(건너뛰기 판정용)</param>
    /// <param name="blocking">같은 이름이 있으면 피해야 하는 확장자(expected 포함)</param>
    /// <param name="isReserved">지금 다른 다운로드가 쓰기로 한 이름인지</param>
    /// <param name="exists">(이름, 확장자) 파일이 있는지</param>
    public static string? Choose(string baseName, IReadOnlyList<string> expected, IReadOnlyList<string> blocking, bool skipExisting,
                                 Func<string, bool> isReserved, Func<string, string, bool> exists)
    {
        bool ExistsAny(string name, IReadOnlyList<string> extensions) => extensions.Any(ext => exists(name, ext));
        bool Taken(string name) => isReserved(name) || ExistsAny(name, blocking);

        if (!Taken(baseName)) return baseName;
        if (skipExisting && !isReserved(baseName) && ExistsAny(baseName, expected)) return null;
        int n = 1;
        while (Taken($"{baseName}-{n}")) n++;
        return $"{baseName}-{n}";
    }
}
