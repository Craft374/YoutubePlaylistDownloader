namespace YPD.Core;

/// <summary>
/// 영상 제목에서 장르, 아티스트, 곡 제목을 뽑은 결과.
/// 예: "[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]"
///   → Title "Infected Mushroom - Spitfire", Genre "Dubstep", Artists ["Infected Mushroom"], SongTitle "Spitfire"
/// </summary>
public sealed record ParsedTitle(string Title, string Genre, IReadOnlyList<string> Artists, string? SongTitle)
{
    public string Artist => string.Join(", ", Artists);
}

/// <summary>기존 Windows 버전의 규칙을 옮긴 것입니다.</summary>
public static class TitleParser
{
    private static readonly string[] IgnoredGenres = ["download", "out now", "monstercat", "video", "lyric", "release", "ncs", "records", "official", "audio", "mv"];
    private static readonly string[] ArtistSeparators = ["feat.", "Feat.", "feat", "ft.", " ft ", "&", " x ", " X "];

    public static ParsedTitle Parse(string rawTitle)
    {
        string title = rawTitle.Replace("—", "-", StringComparison.Ordinal);

        // 첫 번째 [ ] 안의 글자를 장르로 봅니다.
        string[] pieces = title.Split('[', ']');
        string genre = pieces.Length > 1 ? pieces[1] : "";
        if (genre.Length >= title.Length) genre = "";

        if (genre.Trim().Length > 0)
        {
            title = title.Replace($"[{genre}]", "", StringComparison.Ordinal);
            // 남은 대괄호 묶음(보통 "[레이블 Release]")도 하나 지웁니다.
            string[] rest = title.Split('[', ']', '【', '】');
            if (rest.Length > 1)
            {
                string extra = rest[1];
                if (extra.Trim().Length > 0)
                {
                    title = title.Replace($"[{extra}]", "", StringComparison.Ordinal);
                    title = title.Replace($"【{extra}】", "", StringComparison.Ordinal);
                }
            }
        }

        title = title.TrimStart(' ', '-', '[', ']').Trim();

        string lowerGenre = genre.ToLowerInvariant();
        if (IgnoredGenres.Any(g => lowerGenre.Contains(g, StringComparison.Ordinal))) genre = "";
        genre = genre.Trim();

        return SplitArtistAndSong(title) is (var artists, var song)
            ? new ParsedTitle(title, genre, artists, song)
            : new ParsedTitle(title, genre, [], null);
    }

    /// <summary>
    /// "아티스트 - 곡" 형식이면 아티스트 목록과 곡 제목, 아니면 null.
    /// 띄어 쓴 대시만 구분자로 봅니다. "Jay-Z", "Re-mix" 같은 단어 속 대시에서 자르지 않기 위해서입니다.
    /// </summary>
    internal static (List<string> Artists, string Song)? SplitArtistAndSong(string title)
    {
        char[] trimSet = [' ', '-', '–'];
        int dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash < 0) dash = title.IndexOf(" – ", StringComparison.Ordinal);
        if (dash < 0) return null;
        string song = title[(dash + 3)..].Trim(trimSet);
        string artistPart = title[..dash].Trim(trimSet);
        if (song.Length == 0 || artistPart.Length == 0) return null;

        var artists = new List<string> { artistPart };
        foreach (string separator in ArtistSeparators)
            artists = artists.SelectMany(a => a.Split(separator)).ToList();
        artists = artists.Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        if (artists.Count == 0) return null;
        return (artists, song);
    }
}
