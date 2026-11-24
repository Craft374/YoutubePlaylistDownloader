using System.Text;

namespace YPD.Core;

/// <param name="Ffmpeg">ffmpeg.exe 경로.</param>
/// <param name="JsRuntime">yt-dlp가 유튜브 서명 문제를 풀 때 쓰는 JavaScript 런타임. 이름은 "deno" 또는 "node".</param>
public sealed record ToolPaths(string Ytdlp, string? Ffmpeg, (string Name, string Path)? JsRuntime);

/// <summary>오디오 파일에 넣을 태그. null인 값은 yt-dlp가 채운 기본값을 그대로 둡니다.</summary>
public sealed record AudioTags
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Genre { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public int? Track { get; init; }

    /// <summary>
    /// 제목이 "아티스트 - 곡" 형식이면 제목·아티스트·장르와 재생목록 정보(앨범, 트랙 번호)를 채웁니다.
    /// 형식이 아니면 트랙 번호만 넣고 나머지는 yt-dlp에 맡깁니다. 유튜브 뮤직 자동 생성 영상은 yt-dlp가
    /// 설명란에서 아티스트·앨범을 읽어 오므로 덮어쓰지 않습니다.
    /// </summary>
    public static AudioTags Make(MediaEntry entry, string? collectionTitle, string? collectionUploader)
    {
        var parsed = TitleParser.Parse(entry.Title);
        var tags = new AudioTags
        {
            Track = collectionTitle == null ? null : entry.Index,
            // 유튜브는 장르를 주지 않으므로 제목의 [장르]는 항상 넣습니다.
            Genre = parsed.Genre.Length == 0 ? null : parsed.Genre,
        };
        // shortcut: "아티스트 - 곡" 형식이 아니면 앨범(재생목록 이름)을 넣지 않습니다. 유튜브 뮤직 자동 생성 곡의
        // 진짜 앨범을 덮어쓰지 않기 위해서입니다. 일반 영상에도 앨범이 필요해지면 yt-dlp album 값이 없을 때만 넣도록 바꿉니다.
        if (parsed.SongTitle is { } song)
        {
            tags = tags with
            {
                Title = song,
                Artist = parsed.Artist,
                Album = collectionTitle,
                AlbumArtist = collectionTitle == null ? null : collectionUploader,
            };
        }
        return tags;
    }
}

/// <param name="BaseName">확장자 없는 최종 파일 이름. 이미 정리·중복 처리가 끝난 값입니다.</param>
/// <param name="HardwareAV1">
/// AV1을 하드웨어로 디코딩할 수 있는지(Mac 전용 판단). Windows 앱은 false를 넘겨 MP4를 H.264/AAC(최대 1080p)로 받습니다.
/// Windows는 AV1 확장 설치 여부가 PC마다 달라 어디서나 열리는 H.264를 고르고, 더 높은 해상도는 MKV로 받게 합니다.
/// </param>
public sealed record DownloadRequest(string Url, DownloadOptions Options, string OutputDirectory, string BaseName,
                                     string TempDirectory, AudioTags? Tags, bool HardwareAV1 = false);

public static class YTDLPArguments
{
    public const string DownloadMarker = "[ypd-dl]";
    public const string PostprocessMarker = "[ypd-pp]";
    public const string FileMarker = "[ypd-file]";

    private static List<string> Common(ToolPaths tools)
    {
        // PyInstaller로 만든 yt-dlp.exe는 PYTHONIOENCODING·PYTHONUTF8을 무시하고 파이프에 ANSI 코드 페이지(cp949 등)로 씁니다.
        // 그러면 [ypd-file] 경로와 ERROR 줄의 한글이 깨지므로 출력 인코딩을 직접 정합니다.
        List<string> args = ["--ignore-config", "--no-warnings", "--color", "never", "--encoding", "utf-8"];
        if (tools.JsRuntime is { } js)
        {
            if (js.Name != "deno") args.Add("--no-js-runtimes");
            // yt-dlp는 첫 ':'에서만 나누므로 "deno:C:\…"의 드라이브 콜론은 문제가 없습니다.
            args.AddRange(["--js-runtimes", $"{js.Name}:{js.Path}"]);
        }
        if (tools.Ffmpeg is { } ffmpeg) args.AddRange(["--ffmpeg-location", ffmpeg]);
        return args;
    }

    /// <summary>미리보기용: 재생목록 항목은 펼치지 않고 목록만 받습니다.</summary>
    public static List<string> Resolve(string url, ToolPaths tools)
    {
        var args = Common(tools);
        // 비공개·삭제된 영상은 목록에서 뺍니다(받으면 항상 실패합니다).
        args.AddRange(["-J", "--flat-playlist", "--compat-options", "no-youtube-unavailable-videos", "--", url]);
        return args;
    }

    /// <summary>최종 파일 확장자를 미리 알 수 있으면 그 값. 원본 오디오는 받은 뒤에야 알 수 있어 후보 목록을 줍니다.</summary>
    public static List<string> ExpectedExtensions(DownloadOptions options) => options.Mode switch
    {
        MediaMode.Video => [options.VideoContainer.Name()],
        MediaMode.Audio => [options.AudioFormat.FileExtension()],
        _ => ["m4a", "webm", "opus", "mp4"],
    };

    public static List<string> Download(DownloadRequest request, ToolPaths tools)
    {
        var o = request.Options;
        var args = Common(tools);
        args.AddRange([
            "--newline", "--progress", "--progress-delta", "0.2",
            "--progress-template",
            $"download:{DownloadMarker}%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s",
            "--progress-template", $"postprocess:{PostprocessMarker}%(progress.postprocessor)s|%(progress.status)s",
            "--print", $"after_move:{FileMarker}%(filepath)s",
            "--no-playlist", "--no-mtime",
            // -P도 첫 ':'에서만 나눕니다("home:C:\…").
            "-P", $"home:{request.OutputDirectory}",
            "-P", $"temp:{request.TempDirectory}",
            // 출력 템플릿에서 %는 특수 문자라 이름 안의 %는 %%로 바꿉니다.
            "-o", request.BaseName.Replace("%", "%%", StringComparison.Ordinal) + ".%(ext)s",
        ]);

        string language = o.AudioLanguage.Trim();
        string langFilter = language.Length == 0 ? "" : $"[language^={language}]";

        switch (o.Mode)
        {
            case MediaMode.Video:
            {
                string format = langFilter.Length == 0 ? "bv*+ba/b" : $"bv*+ba{langFilter}/bv*+ba/b";
                if (o.VideoContainer == VideoContainer.Mp4)
                {
                    // MP4는 어디서나 열려야 하므로 VP9는 빼고 H.264(AV1 하드웨어 디코딩이 확실할 때만 AV1)만 고릅니다.
                    // YouTube의 H.264는 1080p까지라 MP4는 최대 1080p가 됩니다. 맞는 형식이 없는 영상만 원래 선택으로 돌아갑니다.
                    string codec = request.HardwareAV1 ? "[vcodec~='^(avc1|av01)']" : "[vcodec^=avc1]";
                    format = $"bv*{codec}+ba{langFilter}[acodec^=mp4a]/bv*{codec}+ba[acodec^=mp4a]/bv*{codec}+ba/" + format;
                }
                // 해상도 제한이 먼저, 같은 해상도 안에서 프레임 레이트를 봅니다.
                List<string> sort = o.PreferHighestFPS ? [$"res:{o.MaxHeight}", "fps"] : [$"res:{o.MaxHeight}"];
                // 기본 플레이어에서 재생되도록 mp4는 H.264/AAC를 우선합니다(해상도가 더 중요).
                if (o.VideoContainer == VideoContainer.Mp4) sort.AddRange(["vcodec:h264", "acodec:aac"]);
                string container = o.VideoContainer.Name();
                args.AddRange(["-f", format, "-S", string.Join(",", sort),
                               "--merge-output-format", container, "--remux-video", container]);
                if (o.DownloadSubtitles)
                {
                    string lang = o.SubtitleLanguage.Trim();
                    // "-orig"는 같은 자동 자막의 중복이라 뺍니다. 넣은 뒤 자막 파일은 지우고(no-keep-subs),
                    // 자막만 실패했을 때 영상까지 실패하지 않게 합니다(단일 영상이라 실제 다운로드 오류는 그대로 실패 처리됩니다).
                    args.AddRange(["--write-subs", "--write-auto-subs",
                                   "--sub-langs", $"{PythonRegexEscape(lang.Length == 0 ? "en" : lang)}(-[A-Za-z]+)?,-.*-orig",
                                   "--embed-subs", "--compat-options", "no-keep-subs", "--ignore-errors"]);
                }
                break;
            }
            case MediaMode.Audio:
            {
                string format = langFilter.Length == 0 ? "ba/b" : $"ba{langFilter}/ba/b";
                args.AddRange(["-f", format, "-x", "--audio-format", o.AudioFormat.YtdlpName()]);
                if (o.SetBitrate && !o.AudioFormat.IsLossless() && o.BitrateKbps > 0)
                    args.AddRange(["--audio-quality", $"{o.BitrateKbps}K"]);
                if (o.TagAudio)
                    args.AddRange(Tagging(request.Tags, coverArt: o.AudioFormat.SupportsCoverArt(), mp3: o.AudioFormat == AudioFormat.Mp3));
                break;
            }
            case MediaMode.AudioOriginal:
            {
                // 커버를 넣을 수 있는 m4a를 우선합니다.
                string format = langFilter.Length == 0 ? "ba[ext=m4a]/ba/b" : $"ba{langFilter}[ext=m4a]/ba{langFilter}/ba[ext=m4a]/ba/b";
                args.AddRange(["-f", format]);
                if (o.TagAudio)
                {
                    // webm에는 커버를 넣을 수 없어 opus(같은 코덱)로 옮겨 담습니다.
                    args.AddRange(["--remux-video", "webm>opus"]);
                    args.AddRange(Tagging(request.Tags, coverArt: true, mp3: false));
                }
                break;
            }
        }

        args.AddRange(["--", request.Url]);
        return args;
    }

    private static List<string> Tagging(AudioTags? tags, bool coverArt, bool mp3)
    {
        List<string> args = ["--embed-metadata"];
        if (coverArt)
        {
            // 썸네일을 정사각형으로 잘라 앨범 커버로 넣습니다. jpg로 맞추면 원래 jpg인 썸네일은 변환(=자르기)을
            // 건너뛰므로, 항상 변환이 일어나는 png로 맞춥니다.
            args.AddRange(["--embed-thumbnail", "--convert-thumbnails", "png",
                           "--ppa", "ThumbnailsConvertor+ffmpeg_o:-vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\""]);
        }
        List<string> metadata = [];
        if (mp3) metadata.AddRange(["-id3v2_version", "3"]);
        if (tags != null)
        {
            (string Key, string? Value)[] pairs =
            [
                ("title", tags.Title), ("artist", tags.Artist), ("genre", tags.Genre),
                ("album", tags.Album), ("album_artist", tags.AlbumArtist), ("track", tags.Track?.ToString()),
            ];
            foreach (var (key, value) in pairs)
                if (!string.IsNullOrEmpty(value)) metadata.AddRange(["-metadata", $"{key}={value}"]);
        }
        // yt-dlp는 이 문자열을 셸 규칙(shlex)으로 나눕니다. 값마다 작은따옴표로 감싸 공백·따옴표를 보존합니다.
        // 이 인자는 yt-dlp 자체 -metadata 뒤에 붙어 같은 키를 덮어씁니다.
        if (metadata.Count > 0)
            args.AddRange(["--ppa", "Metadata+ffmpeg_o:" + string.Join(" ", metadata.Select(ShellQuote))]);
        return args;
    }

    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    /// <summary>
    /// yt-dlp는 --sub-langs 항목을 Python 정규식으로 봅니다. 정규식 특수 문자만 백슬래시로 막고 글자·숫자·'-'는 그대로 둡니다.
    /// .NET Regex.Escape는 공백과 '#'까지 바꿔 Python에서 뜻이 달라질 수 있어 쓰지 않습니다.
    /// </summary>
    public static string PythonRegexEscape(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (@"\.^$*+?{}[]|()".Contains(c)) result.Append('\\');
            result.Append(c);
        }
        return result.ToString();
    }
}
