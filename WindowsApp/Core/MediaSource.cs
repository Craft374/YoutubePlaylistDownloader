using System.Text.Json;

namespace YPD.Core;

/// <param name="Index">원본 목록 안의 위치(1부터). 같은 영상이 두 번 들어 있어도 구분되도록 Id에 포함합니다.</param>
public sealed record MediaEntry(string VideoId, string Url, string Title, string? Channel, double? Duration, Uri? Thumbnail, int Index)
{
    public string Id => $"{Index}-{VideoId}";
}

public enum MediaKind { Video, Playlist, Channel }

public sealed record MediaSource(MediaKind Kind, string Title, string? Uploader, Uri? Thumbnail, long? ViewCount, IReadOnlyList<MediaEntry> Entries)
{
    /// <summary>재생목록·채널이면 그 이름, 단일 영상이면 null. 하위 폴더 이름과 <c>$playlist</c>에 씁니다.</summary>
    public string? CollectionTitle => Kind == MediaKind.Video ? null : Title;

    /// <summary><c>yt-dlp -J --flat-playlist</c> 출력. JSON이 아니거나 최상위가 객체가 아니면 <see cref="JsonException"/>.</summary>
    public static MediaSource Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("The top-level value is not an object.");

        string? type = Text(root, "_type");
        if (type != "playlist" && type != "multi_video")
        {
            var entry = MakeEntry(root, 1);
            return new MediaSource(MediaKind.Video, entry.Title, entry.Channel,
                                   BestThumbnail(root, entry.VideoId, large: true), Integer(root, "view_count"), [entry]);
        }

        // 채널 첫 화면 주소는 탭(동영상, Shorts, 라이브)마다 재생목록이 하나씩 들어옵니다. 한 단계 펼칩니다.
        var rawEntries = new List<JsonElement>();
        if (Get(root, "entries") is { ValueKind: JsonValueKind.Array } list)
        {
            foreach (var raw in list.EnumerateArray())
            {
                if (raw.ValueKind == JsonValueKind.Object && Text(raw, "_type") == "playlist"
                    && Get(raw, "entries") is { ValueKind: JsonValueKind.Array } inner)
                    rawEntries.AddRange(inner.EnumerateArray());
                else
                    rawEntries.Add(raw);
            }
        }
        var entries = new List<MediaEntry>(rawEntries.Count);
        for (int offset = 0; offset < rawEntries.Count; offset++)
        {
            // 비공개·삭제된 항목은 조회 옵션(no-youtube-unavailable-videos)으로 빠집니다. null 검사는 안전장치입니다.
            var raw = rawEntries[offset];
            if (raw.ValueKind != JsonValueKind.Object || Text(raw, "id") == null) continue;
            entries.Add(MakeEntry(raw, offset + 1));
        }

        string id = Text(root, "id") ?? "";
        string? channelID = Text(root, "channel_id");
        // 채널 주소는 UC… 아이디로, Topic 채널은 업로드 재생목록 UU…로 옵니다.
        bool isChannel = (id.StartsWith("UC", StringComparison.Ordinal) && (channelID == null || channelID == id))
            || (channelID != null && channelID.StartsWith("UC", StringComparison.Ordinal) && id == "UU" + channelID[2..]);
        string? channelName = Text(root, "channel") ?? Text(root, "uploader");
        string title = isChannel ? (channelName ?? Text(root, "title") ?? id) : (Text(root, "title") ?? id);
        var thumbnail = (entries.Count > 0 ? YouTubeThumbnail(entries[0].VideoId, large: true) : null)
            ?? BestThumbnail(root, null, large: true);
        return new MediaSource(isChannel ? MediaKind.Channel : MediaKind.Playlist, title, channelName, thumbnail,
                               Integer(root, "view_count"), entries);
    }

    private static MediaEntry MakeEntry(JsonElement dict, int index)
    {
        string id = Text(dict, "id") ?? "";
        return new MediaEntry(
            VideoId: id,
            Url: Text(dict, "webpage_url") ?? Text(dict, "url") ?? id,
            Title: Text(dict, "title") ?? id,
            Channel: Text(dict, "channel") ?? Text(dict, "uploader"),
            Duration: Get(dict, "duration") is { ValueKind: JsonValueKind.Number } d ? d.GetDouble() : null,
            Thumbnail: BestThumbnail(dict, id, large: false),
            Index: index);
    }

    private static JsonElement? Get(JsonElement dict, string key) =>
        dict.TryGetProperty(key, out var value) ? value : null;

    /// <summary>문자열 값. 빈 문자열은 없는 것으로 봅니다.</summary>
    private static string? Text(JsonElement dict, string key) =>
        Get(dict, key) is { ValueKind: JsonValueKind.String } v && v.GetString() is { Length: > 0 } s ? s : null;

    /// <summary>정수 값. Swift의 <c>as? Int</c>처럼 42.0은 받고 42.5는 받지 않습니다.</summary>
    private static long? Integer(JsonElement dict, string key)
    {
        if (Get(dict, key) is not { ValueKind: JsonValueKind.Number } v) return null;
        if (v.TryGetInt64(out long n)) return n;
        double d = v.GetDouble();
        return d == Math.Floor(d) && d >= long.MinValue && d < long.MaxValue ? (long)d : null;
    }

    internal static bool IsYouTubeIDChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_';

    private static bool IsYouTubeID(string id) => id.Length == 11 && id.All(IsYouTubeIDChar);

    private static Uri? YouTubeThumbnail(string id, bool large) =>
        IsYouTubeID(id) ? new Uri($"https://i.ytimg.com/vi/{id}/{(large ? "hqdefault" : "mqdefault")}.jpg") : null;

    private static Uri? BestThumbnail(JsonElement dict, string? videoID, bool large)
    {
        // 유튜브는 크기가 정해진 썸네일 주소가 있어 목록용 작은 이미지를 바로 씁니다.
        if (videoID != null && YouTubeThumbnail(videoID, large) is { } youtube) return youtube;
        // Swift처럼 항목이 모두 객체인 목록만 봅니다.
        if (Get(dict, "thumbnails") is { ValueKind: JsonValueKind.Array } list
            && list.EnumerateArray().All(t => t.ValueKind == JsonValueKind.Object)
            && list.EnumerateArray().LastOrDefault(t => Get(t, "url")?.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.Object } last
            && Uri.TryCreate(last.GetProperty("url").GetString(), UriKind.Absolute, out var url))
            return url;
        return Text(dict, "thumbnail") is { } s && Uri.TryCreate(s, UriKind.Absolute, out var thumbnail) ? thumbnail : null;
    }
}
