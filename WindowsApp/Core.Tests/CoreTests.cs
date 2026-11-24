using System.Text.Json;
using System.Text.RegularExpressions;

namespace YPD.Core.Tests;

/// <summary>MacApp/Tests/YPDCoreTests/YPDCoreTests.swift를 옮긴 테스트입니다. Windows 규칙 때문에 기대값이 다른 곳은 주석으로 밝힙니다.</summary>
// DefaultEnvironmentCheck가 프로세스 환경 변수를 잠깐 바꾸므로 ServicesTests와 동시에 돌리지 않습니다.
[Collection("ProcessEnvironment")]
public class CoreTests
{
    private static MediaEntry Entry(string title, int index = 3, double? duration = 200) =>
        new("abcdefghijk", "https://www.youtube.com/watch?v=abcdefghijk", title, "Monstercat", duration, null, index);

    // 테스트가 실행 환경의 변수에 좌우되지 않게 고정한 환경 변수 목록. Windows처럼 대소문자를 구분하지 않습니다.
    private static readonly HashSet<string> FakeEnvironment = new(StringComparer.OrdinalIgnoreCase) { "HOME", "USER", "MY", "PATH" };
    private static bool FakeIsVariable(string name) => FakeEnvironment.Contains(name);

    [Fact]
    public void TitleParserSplitsGenreArtistsAndSong()
    {
        var p = TitleParser.Parse("[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]");
        Assert.Equal("Infected Mushroom - Spitfire", p.Title);
        Assert.Equal("Dubstep", p.Genre);
        Assert.Equal(["Infected Mushroom"], p.Artists);
        Assert.Equal("Spitfire", p.SongTitle);

        var q = TitleParser.Parse("A & B feat. C - Song [Official Video]");
        Assert.Equal("", q.Genre);
        Assert.Equal(["A", "B", "C"], q.Artists);
        Assert.Equal("Song", q.SongTitle);

        Assert.Null(TitleParser.Parse("Jay-Z Re-mix").SongTitle);
        Assert.Equal(["Jay-Z"], TitleParser.Parse("Jay-Z - Song").Artists);
    }

    [Fact]
    public void FilenamePattern()
    {
        var e = Entry("[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]");
        Assert.Equal("Infected Mushroom - Spitfire", FilenameBuilder.BaseName("$title", e, null, FakeIsVariable));
        // Windows: ':'는 Mac처럼 '_'가 됩니다.
        Assert.Equal("song_ [Example] Infected Mushroom - Spitfire (Monstercat) 3",
                     FilenameBuilder.BaseName("song: [$playlist] $artist - $songtitle ($channel) $index", e, "Example", FakeIsVariable));
        Assert.Equal("abcdefghijk Dubstep $unknown", FilenameBuilder.BaseName("$videoid $genre $unknown", e, null, FakeIsVariable));
        // 제목 속 토큰은 다시 치환하지 않습니다.
        Assert.Equal("Price $index", FilenameBuilder.BaseName("$title", Entry("Price $index"), null, FakeIsVariable));
        // 비면 제목, 그래도 비면 영상 ID.
        Assert.Equal("Infected Mushroom - Spitfire", FilenameBuilder.BaseName("$playlist", e, null, FakeIsVariable));
        Assert.Equal("abcdefghijk", FilenameBuilder.BaseName("$title", Entry("..."), null, FakeIsVariable));
        // 토큰이 비어 구두점만 남으면 제목을 씁니다.
        Assert.Equal("Song (Official Video)", FilenameBuilder.BaseName("$artist - $songtitle", Entry("Song (Official Video)"), null, FakeIsVariable));
        // Windows: 장치 이름과 Windows에서 못 쓰는 글자도 BaseName에서 처리됩니다.
        Assert.Equal("CON_", FilenameBuilder.BaseName("$title", Entry("CON"), null, FakeIsVariable));
        Assert.Equal("Why_ A_B", FilenameBuilder.BaseName("$title", Entry("Why? A|B"), null, FakeIsVariable));
    }

    [Fact]
    public void SanitizeMacRules()
    {
        Assert.Equal("a_b_c_d", FilenameBuilder.Sanitize("a/b:c\u0001d"));
        Assert.Equal("a_b", FilenameBuilder.Sanitize("a//:b"));
        Assert.Equal("hidden", FilenameBuilder.Sanitize("..hidden"));
        // yt-dlp가 환경 변수로 펼치지 않도록 바꿉니다. Mac은 고정 목록이었고, Windows는 실제 환경을 봅니다(여기서는 가짜 목록).
        Assert.Equal("Welcome ＄HOME ＄{USER} $HOMES Ke$ha A$AP $5",
                     FilenameBuilder.Sanitize("Welcome $HOME ${USER} $HOMES Ke$ha A$AP $5", isEnvironmentVariable: FakeIsVariable));
        // Windows: 한도가 180바이트·60자가 아니라 UTF-16 100단위입니다(MAX_PATH 260).
        string cut = FilenameBuilder.Sanitize(new string('가', 150));
        Assert.Equal(new string('가', 100), cut);
    }

    [Fact]
    public void SanitizeWindowsInvalidCharacters()
    {
        Assert.Equal("a_b_c_d_e_f_g_h_i_j", FilenameBuilder.Sanitize("a<b>c:d\"e/f\\g|h?i*j"));
        // 이어진 묶음은 '_' 하나로 줄입니다.
        Assert.Equal("a_b", FilenameBuilder.Sanitize("a<>:\"/\\|?*b"));
        Assert.Equal("a_b_c", FilenameBuilder.Sanitize("a\u007F\u0085b\t\nc"));
        Assert.Equal("_x", FilenameBuilder.Sanitize("?x"));
    }

    [Fact]
    public void SanitizeTrimsDotsAndSpaces()
    {
        Assert.Equal("name", FilenameBuilder.Sanitize("name. . "));
        Assert.Equal("name", FilenameBuilder.Sanitize("  name...  "));
        Assert.Equal("x", FilenameBuilder.Sanitize(".. x .."));
        Assert.Equal("a.b", FilenameBuilder.Sanitize("a.b"));
        Assert.Equal("", FilenameBuilder.Sanitize(" . . "));
    }

    [Fact]
    public void SanitizeReservedDeviceNames()
    {
        Assert.Equal("CON_", FilenameBuilder.Sanitize("CON"));
        Assert.Equal("con_.txt", FilenameBuilder.Sanitize("con.txt"));
        Assert.Equal("COM1_", FilenameBuilder.Sanitize("COM1"));
        Assert.Equal("LPT9_", FilenameBuilder.Sanitize("LPT9"));
        Assert.Equal("lpt9_", FilenameBuilder.Sanitize("lpt9"));
        Assert.Equal("NUL_.tar.gz", FilenameBuilder.Sanitize("NUL.tar.gz"));
        Assert.Equal("AUX_", FilenameBuilder.Sanitize("AUX."));
        Assert.Equal("CONSOLE", FilenameBuilder.Sanitize("CONSOLE"));
        Assert.Equal("COM10", FilenameBuilder.Sanitize("COM10"));
        Assert.Equal("my CON", FilenameBuilder.Sanitize("my CON"));
    }

    [Fact]
    public void SanitizeDollarUsesEnvironmentCheck()
    {
        // 대소문자를 구분하지 않는 Windows 환경 변수처럼 동작합니다.
        Assert.Equal("＄home", FilenameBuilder.Sanitize("$home", isEnvironmentVariable: FakeIsVariable));
        Assert.Equal("＄Path x", FilenameBuilder.Sanitize("$Path x", isEnvironmentVariable: FakeIsVariable));
        // ntpath는 이름을 [-\w]+로 가장 길게 읽습니다. "MY"는 있지만 "MY-VAR"는 없으니 그대로 둡니다.
        Assert.Equal("$MY-VAR", FilenameBuilder.Sanitize("$MY-VAR", isEnvironmentVariable: FakeIsVariable));
        Assert.Equal("＄MY-", FilenameBuilder.Sanitize("$MY-", isEnvironmentVariable: n => n == "MY-"));
        Assert.Equal("＄MY.x", FilenameBuilder.Sanitize("$MY.x", isEnvironmentVariable: FakeIsVariable));
        // ${ 는 변수 이름과 상관없이 바꿉니다.
        Assert.Equal("＄{nothing}", FilenameBuilder.Sanitize("${nothing}", isEnvironmentVariable: FakeIsVariable));
        // yt-dlp의 "$$" 보호는 짝으로만 동작해 "$$$"가 "$$"로 줄어듭니다. 이어진 $는 마지막 하나만 남깁니다.
        Assert.Equal("$＄HOME", FilenameBuilder.Sanitize("$$HOME", isEnvironmentVariable: FakeIsVariable));
        Assert.Equal("＄＄$x", FilenameBuilder.Sanitize("$$$x", isEnvironmentVariable: FakeIsVariable));
        Assert.Equal("Ke$ha A$AP $", FilenameBuilder.Sanitize("Ke$ha A$AP $", isEnvironmentVariable: FakeIsVariable));
        // 자른 뒤에 검사합니다: "$HOMES"가 잘려 "$HOME"이 되어도 바꿉니다.
        string tail = FilenameBuilder.Sanitize(new string('a', 95) + "$HOMES", isEnvironmentVariable: FakeIsVariable);
        Assert.Equal(new string('a', 95) + "＄HOME", tail);
    }

    [Fact]
    public void DefaultEnvironmentCheck()
    {
        Environment.SetEnvironmentVariable("YPD_TEST_VARIABLE", "1");
        try
        {
            Assert.True(FilenameBuilder.IsEnvironmentVariable("ypd_test_variable"));
            Assert.Equal("＄Ypd_Test_Variable", FilenameBuilder.Sanitize("$Ypd_Test_Variable"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("YPD_TEST_VARIABLE", null);
        }
        // 자식 프로세스에만 더하는 변수와 PyInstaller 변수도 있는 것으로 봅니다.
        Assert.True(FilenameBuilder.IsEnvironmentVariable("PythonUtf8"));
        Assert.True(FilenameBuilder.IsEnvironmentVariable("PYTHONIOENCODING"));
        Assert.True(FilenameBuilder.IsEnvironmentVariable("_PYI_APPLICATION_HOME_DIR"));
        Assert.False(FilenameBuilder.IsEnvironmentVariable("YPD_SURELY_MISSING_42"));
    }

    [Fact]
    public void SanitizeLeadingTilde()
    {
        Assert.Equal("～user", FilenameBuilder.Sanitize("~user"));
        Assert.Equal("～x", FilenameBuilder.Sanitize(".~x"));
        Assert.Equal("～", FilenameBuilder.Sanitize(" ~ "));
        Assert.Equal("a~b", FilenameBuilder.Sanitize("a~b"));
    }

    [Fact]
    public void SanitizeLengthKeepsTextElements()
    {
        // 서로게이트 쌍을 쪼개지 않습니다.
        string emoji = FilenameBuilder.Sanitize("a" + string.Concat(Enumerable.Repeat("😀", 60)));
        Assert.Equal(99, emoji.Length);
        Assert.False(char.IsHighSurrogate(emoji[^1]));
        // ZWJ로 이은 이모지(8단위)는 통째로 넣거나 뺍니다.
        string family = "👨‍👩‍👧";
        Assert.Equal(8, family.Length);
        Assert.Equal(new string('x', 95), FilenameBuilder.Sanitize(new string('x', 95) + family));
        Assert.Equal(new string('x', 92) + family, FilenameBuilder.Sanitize(new string('x', 92) + family + "y"));
        // 결합 문자도 앞 글자와 함께 다룹니다.
        Assert.Equal(new string('x', 99), FilenameBuilder.Sanitize(new string('x', 99) + "e\u0301"));
        // 자른 뒤 끝에 남은 공백·점도 지웁니다.
        Assert.Equal(new string('x', 98), FilenameBuilder.Sanitize(new string('x', 98) + " .yyyy"));
        Assert.Equal(60, FilenameBuilder.Sanitize(new string('x', 80), maxLength: 60).Length);
    }

    [Fact]
    public void SanitizeResultIsUnchangedByYtdlpSanitizePath()
    {
        // yt-dlp utils._sanitize_path_parts(Windows)가 '#'으로 바꾸는 패턴.
        var ytdlp = new Regex(@"[/<>:""|\\?*]|[\s.]$");
        string[] inputs =
        [
            "a/b", "x:y", "q?", "end.", "end ", "end\u3000", "end\u00A0", "  ..  ", "<>", "a|b*c", "\"quoted\"",
            "CON.", "trailing dots...", new string('가', 99) + " x", "~/~", "$HOME/.", "tab\t", "x\u2028",
        ];
        foreach (string input in inputs)
        {
            string result = FilenameBuilder.Sanitize(input, isEnvironmentVariable: FakeIsVariable);
            Assert.False(ytdlp.IsMatch(result), $"{input} → {result}");
            string folder = FilenameBuilder.FolderName(input, FakeIsVariable);
            Assert.False(ytdlp.IsMatch(folder), $"{input} → {folder}");
        }
    }

    [Fact]
    public void FolderNameProtectsPercent()
    {
        // 하나뿐인 %는 펼쳐지지 않아 그대로 둡니다.
        Assert.Equal("100% Hits", FilenameBuilder.FolderName("100% Hits", FakeIsVariable));
        Assert.Equal("50％ off 20％", FilenameBuilder.FolderName("50% off 20%", FakeIsVariable));
        Assert.Equal("％USERPROFILE％", FilenameBuilder.FolderName("%USERPROFILE%", FakeIsVariable));
        Assert.Equal("A＄$B", FilenameBuilder.FolderName("A$$B", FakeIsVariable));
        Assert.Equal("＄＄$", FilenameBuilder.FolderName("$$$", FakeIsVariable));
        Assert.Equal("Ke$ha Hits", FilenameBuilder.FolderName("Ke$ha Hits", FakeIsVariable));
        Assert.Equal(60, FilenameBuilder.FolderName(new string('p', 80), FakeIsVariable).Length);
    }

    [Fact]
    public void SourceURLNormalize()
    {
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", SourceURL.Normalize("dQw4w9WgXcQ"));
        Assert.Equal("https://youtube.com/@name", SourceURL.Normalize("youtube.com/@name"));
        Assert.Equal("https://www.youtube.com/@name/shorts", SourceURL.Normalize("https://www.youtube.com/@name/shorts"));
        Assert.Equal("https://youtu.be/dQw4w9WgXcQ?si=x", SourceURL.Normalize(" https://youtu.be/dQw4w9WgXcQ?si=x "));
        Assert.Null(SourceURL.Normalize("hello world"));
        Assert.Null(SourceURL.Normalize(""));
        Assert.Null(SourceURL.Normalize("notaurl"));
        Assert.Null(SourceURL.Normalize("ftp://example.com/x"));
        Assert.Equal(["a", "b", "c"], SourceURL.Lines("a\r\n\n b \nc"));
    }

    [Fact]
    public void MediaSourceParse()
    {
        const string playlist = """
            {"_type":"playlist","id":"PL1","title":"My List","channel":"Chan","entries":[
              {"_type":"url","id":"aaaaaaaaaaa","url":"https://www.youtube.com/watch?v=aaaaaaaaaaa","title":"One","duration":61.0,"channel":"C1"},
              null,
              {"_type":"url","id":"bbbbbbbbbbb","url":"https://www.youtube.com/watch?v=bbbbbbbbbbb","title":"Two"}
            ]}
            """;
        var p = MediaSource.Parse(playlist);
        Assert.Equal(MediaKind.Playlist, p.Kind);
        Assert.Equal("My List", p.CollectionTitle);
        Assert.Equal([1, 3], p.Entries.Select(e => e.Index));
        Assert.Equal(61, p.Entries[0].Duration);
        Assert.Equal("https://i.ytimg.com/vi/bbbbbbbbbbb/mqdefault.jpg", p.Entries[1].Thumbnail?.AbsoluteUri);
        Assert.Equal("https://i.ytimg.com/vi/aaaaaaaaaaa/hqdefault.jpg", p.Thumbnail?.AbsoluteUri);
        Assert.Equal("Chan", p.Uploader);

        const string channel = """{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Videos","channel":"Chan","channel_id":"UCabcdefghijklmnopqrstuv","entries":[]}""";
        var c = MediaSource.Parse(channel);
        Assert.Equal(MediaKind.Channel, c.Kind);
        Assert.Equal("Chan", c.Title);

        // 채널 첫 화면: 탭마다 재생목록이 들어 있어 한 단계 펼칩니다.
        const string tabs = """{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan","channel":"Chan","channel_id":"UCabcdefghijklmnopqrstuv","entries":[{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Videos","entries":[{"id":"aaaaaaaaaaa","title":"A"}]},{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Shorts","entries":[{"id":"bbbbbbbbbbb","title":"B"}]}]}""";
        var t = MediaSource.Parse(tabs);
        Assert.Equal(MediaKind.Channel, t.Kind);
        Assert.Equal(["aaaaaaaaaaa", "bbbbbbbbbbb"], t.Entries.Select(e => e.VideoId));
        Assert.Equal([1, 2], t.Entries.Select(e => e.Index));

        // Topic 채널은 업로드 재생목록(UU…)으로 옵니다.
        const string topic = """{"_type":"playlist","id":"UUabcdefghijklmnopqrstuv","title":"Uploads from Artist","channel":"Artist","channel_id":"UCabcdefghijklmnopqrstuv","entries":[]}""";
        var tp = MediaSource.Parse(topic);
        Assert.Equal(MediaKind.Channel, tp.Kind);
        Assert.Equal("Artist", tp.Title);

        const string video = """{"id":"ccccccccccc","title":"Solo","channel":"Me","view_count":42,"webpage_url":"https://www.youtube.com/watch?v=ccccccccccc"}""";
        var v = MediaSource.Parse(video);
        Assert.Equal(MediaKind.Video, v.Kind);
        Assert.Null(v.CollectionTitle);
        Assert.Equal(42, v.ViewCount);
        Assert.Single(v.Entries);
        Assert.Equal("https://i.ytimg.com/vi/ccccccccccc/hqdefault.jpg", v.Thumbnail?.AbsoluteUri);

        // 범위·길이 필터는 단일 영상에 적용하지 않습니다.
        var o = new DownloadOptions { UseRange = true, RangeStart = 5 };
        Assert.Single(o.SelectEntries(v));
        Assert.Empty(o.SelectEntries(p));
    }

    [Fact]
    public void MediaSourceParseDetails()
    {
        // 정수 duration, 21억이 넘는 조회수, id 없는 항목(번호는 셉니다), 다른 사이트의 썸네일 목록.
        const string json = """
            {"_type":"playlist","id":"PL2","title":"","uploader":"Up","view_count":8000000000,"entries":[
              {"id":"x1","title":"Other site","duration":61,"uploader":"U","thumbnails":[{"url":"https://a/1.jpg"},{"id":"no-url"},{"url":"https://a/2.jpg"},{"id":"last"}]},
              {"title":"no id"},
              {"id":"","title":"empty id"},
              {"id":"x2","thumbnail":"https://b/t.jpg","duration":"long"}
            ]}
            """;
        var s = MediaSource.Parse(json);
        Assert.Equal(MediaKind.Playlist, s.Kind);
        Assert.Equal("PL2", s.Title); // 빈 제목은 없는 것으로 보고 id를 씁니다.
        Assert.Equal("Up", s.Uploader);
        Assert.Equal(8_000_000_000L, s.ViewCount);
        Assert.Equal([1, 4], s.Entries.Select(e => e.Index));
        var first = s.Entries[0];
        Assert.Equal(61, first.Duration);
        Assert.Equal("U", first.Channel);
        Assert.Equal("x1", first.Url);
        Assert.Equal("https://a/2.jpg", first.Thumbnail?.AbsoluteUri);
        Assert.Equal("1-x1", first.Id);
        Assert.Null(s.Entries[1].Duration);
        Assert.Equal("x2", s.Entries[1].Title);
        Assert.Equal("https://b/t.jpg", s.Entries[1].Thumbnail?.AbsoluteUri);
        // 첫 항목이 유튜브 ID가 아니고 최상위 썸네일도 없으면 null.
        Assert.Null(s.Thumbnail);

        // 42.0은 정수로 받고 42.5는 받지 않습니다(Swift as? Int).
        Assert.Equal(42, MediaSource.Parse("""{"id":"v","view_count":42.0}""").ViewCount);
        Assert.Null(MediaSource.Parse("""{"id":"v","view_count":42.5}""").ViewCount);

        Assert.ThrowsAny<JsonException>(() => MediaSource.Parse("[1,2]"));
        Assert.ThrowsAny<JsonException>(() => MediaSource.Parse("not json"));
    }

    [Fact]
    public void SelectEntries()
    {
        var entries = Enumerable.Range(1, 10).Select(i => Entry($"t{i}", i, i * 60.0)).ToList();
        var o = new DownloadOptions();
        Assert.Equal(10, o.SelectEntries(entries).Count);
        o.UseRange = true; o.RangeStart = 3; o.RangeEnd = 5;
        Assert.Equal([3, 4, 5], o.SelectEntries(entries).Select(e => e.Index));
        o.RangeEnd = 0;
        Assert.Equal(Enumerable.Range(3, 8), o.SelectEntries(entries).Select(e => e.Index));
        o.UseRange = false; o.FilterByLength = true; o.FilterLonger = true; o.FilterMinutes = 8;
        Assert.Equal([9, 10], o.SelectEntries(entries).Select(e => e.Index));
        o.FilterLonger = false;
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 11], o.SelectEntries([.. entries, Entry("live", 11, null)]).Select(e => e.Index));
    }

    [Fact]
    public void OptionsDecodeMissingKeys()
    {
        var o = JsonSerializer.Deserialize<DownloadOptions>("""{"mode":"audio","bitrateKbps":320}""", CoreJson.Options)!;
        Assert.Equal(MediaMode.Audio, o.Mode);
        Assert.Equal(320, o.BitrateKbps);
        Assert.Equal("$title", o.FilenamePattern);
        Assert.True(o.TagAudio);
        var bad = JsonSerializer.Deserialize<DownloadOptions>("""{"audioFormat":"aac","maxHeight":"tall","filenamePattern":null,"audioOriginal":1,"mode":2}""", CoreJson.Options)!;
        Assert.Equal(AudioFormat.Mp3, bad.AudioFormat);
        Assert.Equal(1080, bad.MaxHeight);
        Assert.Equal("$title", bad.FilenamePattern);
        Assert.Equal(MediaMode.Video, bad.Mode); // 숫자 enum은 받지 않습니다.

        var custom = new DownloadOptions { Mode = MediaMode.AudioOriginal, VideoContainer = VideoContainer.Mkv, FilterMinutes = 2.5, AudioLanguage = "ko" };
        string json = JsonSerializer.Serialize(custom, CoreJson.Options);
        Assert.Contains("\"mode\": \"audioOriginal\"", json);
        Assert.Contains("\"videoContainer\": \"mkv\"", json);
        Assert.Equal(custom, JsonSerializer.Deserialize<DownloadOptions>(json, CoreJson.Options));
        // 작업에 넘길 복사본은 원본을 바꿔도 따라가지 않습니다.
        var copy = custom with { };
        custom.MaxHeight = 720;
        Assert.Equal(1080, copy.MaxHeight);
    }

    [Fact]
    public void ProgressParsing()
    {
        const string line = "[ypd-dl]512|1024|NA|2048.5|NA|NA|avc1.640028|none|mp4";
        Assert.Equal(new YTDLPEvent.Download(StreamKind.Video, 0.5, 2048.5), YTDLPEvent.Parse(line));
        Assert.Equal(new YTDLPEvent.Download(StreamKind.Audio, 0.5, null), YTDLPEvent.Parse("[ypd-dl]5|NA|NA|NA|2|4|NA|mp4a.40.2|m4a"));
        Assert.Null(YTDLPEvent.Parse("[ypd-dl]5|10|NA|NA|NA|NA|NA|NA|vtt"));
        Assert.Equal(new YTDLPEvent.Download(StreamKind.Muxed, 0.5, null), YTDLPEvent.Parse("[ypd-dl]5|10|NA|NA|NA|NA|NA|NA|mp4"));
        Assert.Equal(new YTDLPEvent.Postprocess("Merger", true), YTDLPEvent.Parse("[ypd-pp]Merger|started"));
        Assert.Equal(new YTDLPEvent.File(@"C:\Users\me\Videos\a b.mp4"), YTDLPEvent.Parse(@"[ypd-file]C:\Users\me\Videos\a b.mp4"));
        Assert.Equal(new YTDLPEvent.Error("[youtube] x: Video unavailable"), YTDLPEvent.Parse("ERROR: [youtube] x: Video unavailable"));
        Assert.Null(YTDLPEvent.Parse("[download] 10%"));
        // 소수점은 문화권과 상관없이 '.'로 읽습니다.
        Assert.Equal(new YTDLPEvent.Download(StreamKind.Muxed, 1, 1.5), YTDLPEvent.Parse("[ypd-dl]20|10|NA|1.5|NA|NA|NA|NA|mp4"));

        var t = new ProgressTracker();
        Assert.Equal(0.85, t.Update(StreamKind.Video, 1), 4);
        Assert.Equal(0.925, t.Update(StreamKind.Audio, 0.5), 4);
        var a = new ProgressTracker();
        Assert.Equal(0.4, a.Update(StreamKind.Audio, 0.4), 4);
        Assert.Equal(0.4, a.Update(StreamKind.Audio, 0.2), 4); // 줄어들지 않음
    }

    private static readonly ToolPaths WindowsTools = new(@"C:\t\yt-dlp\yt-dlp.exe", @"C:\t\ffmpeg.exe", ("deno", @"C:\t\deno.exe"));

    [Fact]
    public void DownloadArguments()
    {
        var o = new DownloadOptions { Mode = MediaMode.Audio, SetBitrate = true, BitrateKbps = 256 };
        var tags = AudioTags.Make(Entry("Artist's - Song"), "Album", "Up");
        var req = new DownloadRequest("https://x", o, @"C:\out", "100% song", @"C:\tmp\x", tags);
        var args = YTDLPArguments.Download(req, WindowsTools);
        Assert.Contains("100%% song.%(ext)s", args);
        Assert.Equal(["--", "https://x"], args.TakeLast(2));
        Assert.Contains("256K", args);
        Assert.Contains(@"deno:C:\t\deno.exe", args);
        Assert.Contains(@"home:C:\out", args);
        Assert.Contains(@"temp:C:\tmp\x", args);
        Assert.Contains("png", args);
        Assert.DoesNotContain("--no-js-runtimes", args);
        string? ppa = args.FirstOrDefault(a => a.StartsWith("Metadata+ffmpeg_o:", StringComparison.Ordinal));
        Assert.Equal("""Metadata+ffmpeg_o:'-id3v2_version' '3' '-metadata' 'title=Song' '-metadata' 'artist=Artist'"'"'s' '-metadata' 'album=Album' '-metadata' 'album_artist=Up' '-metadata' 'track=3'""", ppa);

        o.Mode = MediaMode.Video;
        o.DownloadSubtitles = true;
        var v = YTDLPArguments.Download(new DownloadRequest("u", o, @"C:\o", "n", @"C:\t", null), WindowsTools);
        Assert.Contains("res:1080,vcodec:h264,acodec:aac", v);
        Assert.Contains("--embed-subs", v);
        Assert.Contains("en(-[A-Za-z]+)?,-.*-orig", v);
        o.PreferHighestFPS = true;
        var fps = YTDLPArguments.Download(new DownloadRequest("u", o, @"C:\o", "n", @"C:\t", null), WindowsTools);
        Assert.Contains("res:1080,fps,vcodec:h264,acodec:aac", fps);
        // MP4는 어디서나 열리는 코덱만 고르고, 없을 때만 원래 선택으로 돌아갑니다.
        string fmt = fps[fps.IndexOf("-f") + 1];
        Assert.StartsWith("bv*[vcodec^=avc1]+ba[acodec^=mp4a]/", fmt);
        Assert.EndsWith("/bv*+ba/b", fmt);
        var av1 = YTDLPArguments.Download(new DownloadRequest("u", o, @"C:\o", "n", @"C:\t", null, HardwareAV1: true), WindowsTools);
        Assert.StartsWith("bv*[vcodec~='^(avc1|av01)']", av1[av1.IndexOf("-f") + 1]);
        Assert.DoesNotContain("--embed-metadata", v);

        var node = WindowsTools with { Ffmpeg = null, JsRuntime = ("node", @"C:\n\node.exe") };
        var r = YTDLPArguments.Resolve("u", node);
        Assert.Contains("--no-js-runtimes", r);
        Assert.Contains(@"node:C:\n\node.exe", r);
        Assert.DoesNotContain("--ffmpeg-location", r);
        Assert.Equal(["-J", "--flat-playlist", "--compat-options", "no-youtube-unavailable-videos", "--", "u"], r.TakeLast(6));
    }

    // Mac YTDLPArguments.download가 같은 입력에서 만든 배열(swiftc로 YPDCore를 빌드해 출력한 값).
    private const string GoldenVideoMp4 = """
        ["--ignore-config","--no-warnings","--color","never","--encoding","utf-8","--js-runtimes","deno:/t/deno","--ffmpeg-location","/t/ffmpeg","--newline","--progress","--progress-delta","0.2","--progress-template","download:[ypd-dl]%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s","--progress-template","postprocess:[ypd-pp]%(progress.postprocessor)s|%(progress.status)s","--print","after_move:[ypd-file]%(filepath)s","--no-playlist","--no-mtime","-P","home:/out","-P","temp:/tmp/x","-o","100%% n.%(ext)s","-f","bv*[vcodec^=avc1]+ba[language^=ko][acodec^=mp4a]/bv*[vcodec^=avc1]+ba[acodec^=mp4a]/bv*[vcodec^=avc1]+ba/bv*+ba[language^=ko]/bv*+ba/b","-S","res:1080,vcodec:h264,acodec:aac","--merge-output-format","mp4","--remux-video","mp4","--write-subs","--write-auto-subs","--sub-langs","zh\\.Hans(-[A-Za-z]+)?,-.*-orig","--embed-subs","--compat-options","no-keep-subs","--ignore-errors","--","https://y/v"]
        """;

    private const string GoldenAudioMp3 = """
        ["--ignore-config","--no-warnings","--color","never","--encoding","utf-8","--js-runtimes","deno:/t/deno","--ffmpeg-location","/t/ffmpeg","--newline","--progress","--progress-delta","0.2","--progress-template","download:[ypd-dl]%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s","--progress-template","postprocess:[ypd-pp]%(progress.postprocessor)s|%(progress.status)s","--print","after_move:[ypd-file]%(filepath)s","--no-playlist","--no-mtime","-P","home:/out","-P","temp:/tmp/x","-o","n.%(ext)s","-f","ba/b","-x","--audio-format","mp3","--audio-quality","256K","--embed-metadata","--embed-thumbnail","--convert-thumbnails","png","--ppa","ThumbnailsConvertor+ffmpeg_o:-vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\"","--ppa","Metadata+ffmpeg_o:'-id3v2_version' '3' '-metadata' 'title=Song' '-metadata' 'artist=Artist'\"'\"'s' '-metadata' 'genre=Dubstep' '-metadata' 'album=Album' '-metadata' 'album_artist=Up' '-metadata' 'track=3'","--","https://y/a"]
        """;

    private const string GoldenAudioOriginal = """
        ["--ignore-config","--no-warnings","--color","never","--encoding","utf-8","--js-runtimes","deno:/t/deno","--ffmpeg-location","/t/ffmpeg","--newline","--progress","--progress-delta","0.2","--progress-template","download:[ypd-dl]%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s","--progress-template","postprocess:[ypd-pp]%(progress.postprocessor)s|%(progress.status)s","--print","after_move:[ypd-file]%(filepath)s","--no-playlist","--no-mtime","-P","home:/out","-P","temp:/tmp/x","-o","n.%(ext)s","-f","ba[language^=en][ext=m4a]/ba[language^=en]/ba[ext=m4a]/ba/b","--remux-video","webm>opus","--embed-metadata","--embed-thumbnail","--convert-thumbnails","png","--ppa","ThumbnailsConvertor+ffmpeg_o:-vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\"","--","u"]
        """;

    private const string GoldenVideoMkv = """
        ["--ignore-config","--no-warnings","--color","never","--encoding","utf-8","--newline","--progress","--progress-delta","0.2","--progress-template","download:[ypd-dl]%(progress.downloaded_bytes)s|%(progress.total_bytes)s|%(progress.total_bytes_estimate)s|%(progress.speed)s|%(progress.fragment_index)s|%(progress.fragment_count)s|%(info.vcodec)s|%(info.acodec)s|%(info.ext)s","--progress-template","postprocess:[ypd-pp]%(progress.postprocessor)s|%(progress.status)s","--print","after_move:[ypd-file]%(filepath)s","--no-playlist","--no-mtime","-P","home:/out","-P","temp:/tmp/x","-o","n.%(ext)s","-f","bv*+ba/b","-S","res:2160,fps","--merge-output-format","mkv","--remux-video","mkv","--","u"]
        """;

    [Fact]
    public void DownloadArgumentsMatchSwiftOutput()
    {
        // 경로는 문자열로 그대로 넘기므로 Mac과 같은 경로를 넣으면 배열 전체가 같아야 합니다.
        var tools = new ToolPaths("/t/yt-dlp", "/t/ffmpeg", ("deno", "/t/deno"));
        var e = Entry("[Dubstep] Artist's - Song");
        static List<string> Golden(string json) => JsonSerializer.Deserialize<List<string>>(json)!;

        var video = new DownloadOptions { DownloadSubtitles = true, SubtitleLanguage = "zh.Hans", AudioLanguage = "ko" };
        Assert.Equal(Golden(GoldenVideoMp4), YTDLPArguments.Download(new DownloadRequest("https://y/v", video, "/out", "100% n", "/tmp/x", null), tools));

        var audio = new DownloadOptions { Mode = MediaMode.Audio, SetBitrate = true, BitrateKbps = 256 };
        var tags = AudioTags.Make(e, "Album", "Up");
        Assert.Equal(Golden(GoldenAudioMp3), YTDLPArguments.Download(new DownloadRequest("https://y/a", audio, "/out", "n", "/tmp/x", tags), tools));

        var original = new DownloadOptions { Mode = MediaMode.AudioOriginal, AudioLanguage = "en" };
        Assert.Equal(Golden(GoldenAudioOriginal), YTDLPArguments.Download(new DownloadRequest("u", original, "/out", "n", "/tmp/x", null), tools));

        var mkv = new DownloadOptions { VideoContainer = VideoContainer.Mkv, MaxHeight = 2160, PreferHighestFPS = true };
        Assert.Equal(Golden(GoldenVideoMkv), YTDLPArguments.Download(new DownloadRequest("u", mkv, "/out", "n", "/tmp/x", null), tools with { Ffmpeg = null, JsRuntime = null }));
    }

    [Fact]
    public void PythonRegexEscape()
    {
        Assert.Equal("en", YTDLPArguments.PythonRegexEscape("en"));
        Assert.Equal("en-US", YTDLPArguments.PythonRegexEscape("en-US"));
        Assert.Equal(@"zh\.Hans", YTDLPArguments.PythonRegexEscape("zh.Hans"));
        // NSRegularExpression.escapedPattern(for: "zh.Hans-x (a)#b*")와 같은 결과입니다. 공백과 '#'은 그대로 둡니다.
        Assert.Equal(@"zh\.Hans-x \(a\)#b\*", YTDLPArguments.PythonRegexEscape("zh.Hans-x (a)#b*"));
        Assert.Equal(@"\[\]\{\}\^\$\+\?\|\\", YTDLPArguments.PythonRegexEscape(@"[]{}^$+?|\"));
    }

    [Fact]
    public void ExpectedExtensionsAndFormats()
    {
        Assert.Equal(["mkv"], YTDLPArguments.ExpectedExtensions(new DownloadOptions { VideoContainer = VideoContainer.Mkv }));
        Assert.Equal(["m4a"], YTDLPArguments.ExpectedExtensions(new DownloadOptions { Mode = MediaMode.Audio, AudioFormat = AudioFormat.Alac }));
        Assert.Equal(["m4a", "webm", "opus", "mp4"], YTDLPArguments.ExpectedExtensions(new DownloadOptions { Mode = MediaMode.AudioOriginal }));
        Assert.Equal("vorbis", AudioFormat.Ogg.YtdlpName());
        Assert.Equal("ogg", AudioFormat.Ogg.FileExtension());
        Assert.False(AudioFormat.Wav.SupportsCoverArt());
        Assert.True(AudioFormat.Flac.IsLossless());
    }

    [Fact]
    public void OutputNameChoose()
    {
        HashSet<string> files = ["Song.mp3", "Song-1.mp3", "Other.webm"];
        string? Choose(string baseName, bool skip, HashSet<string>? reserved = null) =>
            OutputName.Choose(baseName, ["mp3"], ["mp3", "webm"], skip,
                              name => reserved?.Contains(name) == true, (name, ext) => files.Contains($"{name}.{ext}"));
        Assert.Equal("New", Choose("New", false));
        Assert.Equal("Song-2", Choose("Song", false));
        Assert.Null(Choose("Song", true));
        // 다른 확장자 파일만 있으면 건너뛰지 않고 새 이름을 씁니다(기존 webm을 yt-dlp가 지우지 않게).
        Assert.Equal("Other-1", Choose("Other", true));
        // 다른 다운로드가 쓰기로 한 이름은 건너뛰지 않고 피합니다.
        Assert.Equal("New-1", Choose("New", true, ["New"]));
    }
}
