import XCTest
@testable import YPDCore

final class YPDCoreTests: XCTestCase {
    private func entry(_ title: String, index: Int = 3, duration: Double? = 200) -> MediaEntry {
        MediaEntry(videoID: "abcdefghijk", url: "https://www.youtube.com/watch?v=abcdefghijk", title: title,
                   channel: "Monstercat", duration: duration, thumbnail: nil, index: index)
    }

    func testTitleParser() {
        let p = TitleParser.parse("[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]")
        XCTAssertEqual(p.title, "Infected Mushroom - Spitfire")
        XCTAssertEqual(p.genre, "Dubstep")
        XCTAssertEqual(p.artists, ["Infected Mushroom"])
        XCTAssertEqual(p.songTitle, "Spitfire")

        let q = TitleParser.parse("A & B feat. C - Song [Official Video]")
        XCTAssertEqual(q.genre, "")
        XCTAssertEqual(q.artists, ["A", "B", "C"])
        XCTAssertEqual(q.songTitle, "Song")

        XCTAssertNil(TitleParser.parse("Jay-Z Re-mix").songTitle)
        XCTAssertEqual(TitleParser.parse("Jay-Z - Song").artists, ["Jay-Z"])
    }

    func testFilenamePattern() {
        let e = entry("[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]")
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$title", entry: e, collectionTitle: nil),
                       "Infected Mushroom - Spitfire")
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "song: [$playlist] $artist - $songtitle ($channel) $index", entry: e, collectionTitle: "Example"),
                       "song_ [Example] Infected Mushroom - Spitfire (Monstercat) 3")
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$videoid $genre $unknown", entry: e, collectionTitle: nil),
                       "abcdefghijk Dubstep $unknown")
        // 제목 속 토큰은 다시 치환하지 않습니다.
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$title", entry: entry("Price $index"), collectionTitle: nil), "Price $index")
        // 비면 제목, 그래도 비면 영상 ID.
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$playlist", entry: e, collectionTitle: nil), "Infected Mushroom - Spitfire")
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$title", entry: entry("..."), collectionTitle: nil), "abcdefghijk")
        // 토큰이 비어 구두점만 남으면 제목을 씁니다.
        XCTAssertEqual(FilenameBuilder.baseName(pattern: "$artist - $songtitle", entry: entry("Song (Official Video)"), collectionTitle: nil),
                       "Song (Official Video)")
    }

    func testSanitize() {
        XCTAssertEqual(FilenameBuilder.sanitize("a/b:c\u{0001}d"), "a_b_c_d")
        XCTAssertEqual(FilenameBuilder.sanitize("a//:b"), "a_b")
        XCTAssertEqual(FilenameBuilder.sanitize("..hidden"), "hidden")
        // yt-dlp가 환경 변수로 펼치지 않도록 바꿉니다.
        XCTAssertEqual(FilenameBuilder.sanitize("Welcome $HOME ${USER} $HOMES Ke$ha A$AP $5"), "Welcome ＄HOME ＄{USER} $HOMES Ke$ha A$AP $5")
        let long = String(repeating: "가", count: 100) // 300바이트
        let cut = FilenameBuilder.sanitize(long)
        XCTAssertLessThanOrEqual(cut.utf8.count, 180)
        XCTAssertEqual(cut.count, 60)
    }

    func testSourceURL() {
        XCTAssertEqual(SourceURL.normalize("dQw4w9WgXcQ"), "https://www.youtube.com/watch?v=dQw4w9WgXcQ")
        XCTAssertEqual(SourceURL.normalize("youtube.com/@name"), "https://youtube.com/@name")
        XCTAssertEqual(SourceURL.normalize("https://www.youtube.com/@name/shorts"), "https://www.youtube.com/@name/shorts")
        XCTAssertEqual(SourceURL.normalize(" https://youtu.be/dQw4w9WgXcQ?si=x "), "https://youtu.be/dQw4w9WgXcQ?si=x")
        XCTAssertNil(SourceURL.normalize("hello world"))
        XCTAssertNil(SourceURL.normalize(""))
        XCTAssertNil(SourceURL.normalize("notaurl"))
        XCTAssertEqual(SourceURL.lines("a\r\n\n b \nc"), ["a", "b", "c"])
    }

    func testMediaSourceParse() throws {
        let playlist = """
        {"_type":"playlist","id":"PL1","title":"My List","channel":"Chan","entries":[
          {"_type":"url","id":"aaaaaaaaaaa","url":"https://www.youtube.com/watch?v=aaaaaaaaaaa","title":"One","duration":61.0,"channel":"C1"},
          null,
          {"_type":"url","id":"bbbbbbbbbbb","url":"https://www.youtube.com/watch?v=bbbbbbbbbbb","title":"Two"}
        ]}
        """
        let p = try MediaSource.parse(Data(playlist.utf8))
        XCTAssertEqual(p.kind, .playlist)
        XCTAssertEqual(p.collectionTitle, "My List")
        XCTAssertEqual(p.entries.map(\.index), [1, 3])
        XCTAssertEqual(p.entries[0].duration, 61)
        XCTAssertEqual(p.entries[1].thumbnail?.absoluteString, "https://i.ytimg.com/vi/bbbbbbbbbbb/mqdefault.jpg")

        let channel = #"{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Videos","channel":"Chan","channel_id":"UCabcdefghijklmnopqrstuv","entries":[]}"#
        let c = try MediaSource.parse(Data(channel.utf8))
        XCTAssertEqual(c.kind, .channel)
        XCTAssertEqual(c.title, "Chan")

        // 채널 첫 화면: 탭마다 재생목록이 들어 있어 한 단계 펼칩니다.
        let tabs = #"{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan","channel":"Chan","channel_id":"UCabcdefghijklmnopqrstuv","entries":[{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Videos","entries":[{"id":"aaaaaaaaaaa","title":"A"}]},{"_type":"playlist","id":"UCabcdefghijklmnopqrstuv","title":"Chan - Shorts","entries":[{"id":"bbbbbbbbbbb","title":"B"}]}]}"#
        let t = try MediaSource.parse(Data(tabs.utf8))
        XCTAssertEqual(t.kind, .channel)
        XCTAssertEqual(t.entries.map(\.videoID), ["aaaaaaaaaaa", "bbbbbbbbbbb"])
        XCTAssertEqual(t.entries.map(\.index), [1, 2])

        // Topic 채널은 업로드 재생목록(UU…)으로 옵니다.
        let topic = #"{"_type":"playlist","id":"UUabcdefghijklmnopqrstuv","title":"Uploads from Artist","channel":"Artist","channel_id":"UCabcdefghijklmnopqrstuv","entries":[]}"#
        let tp = try MediaSource.parse(Data(topic.utf8))
        XCTAssertEqual(tp.kind, .channel)
        XCTAssertEqual(tp.title, "Artist")

        let video = #"{"id":"ccccccccccc","title":"Solo","channel":"Me","view_count":42,"webpage_url":"https://www.youtube.com/watch?v=ccccccccccc"}"#
        let v = try MediaSource.parse(Data(video.utf8))
        XCTAssertEqual(v.kind, .video)
        XCTAssertNil(v.collectionTitle)
        XCTAssertEqual(v.viewCount, 42)
        XCTAssertEqual(v.entries.count, 1)

        // 범위·길이 필터는 단일 영상에 적용하지 않습니다.
        var o = DownloadOptions()
        o.useRange = true; o.rangeStart = 5
        XCTAssertEqual(o.selectEntries(from: v).count, 1)
        XCTAssertEqual(o.selectEntries(from: p).count, 0)
    }

    func testSelectEntries() {
        let entries = (1...10).map { entry("t\($0)", index: $0, duration: Double($0) * 60) }
        var o = DownloadOptions()
        XCTAssertEqual(o.selectEntries(entries).count, 10)
        o.useRange = true; o.rangeStart = 3; o.rangeEnd = 5
        XCTAssertEqual(o.selectEntries(entries).map(\.index), [3, 4, 5])
        o.rangeEnd = 0
        XCTAssertEqual(o.selectEntries(entries).map(\.index), Array(3...10))
        o.useRange = false; o.filterByLength = true; o.filterLonger = true; o.filterMinutes = 8
        XCTAssertEqual(o.selectEntries(entries).map(\.index), [9, 10])
        o.filterLonger = false
        XCTAssertEqual(o.selectEntries(entries + [entry("live", index: 11, duration: nil)]).map(\.index), [1, 2, 3, 4, 5, 6, 7, 11])
    }

    func testOptionsDecodeMissingKeys() throws {
        let o = try JSONDecoder().decode(DownloadOptions.self, from: Data(#"{"mode":"audio","bitrateKbps":320}"#.utf8))
        XCTAssertEqual(o.mode, .audio)
        XCTAssertEqual(o.bitrateKbps, 320)
        XCTAssertEqual(o.filenamePattern, "$title")
        XCTAssertTrue(o.tagAudio)
        let bad = try JSONDecoder().decode(DownloadOptions.self, from: Data(#"{"audioFormat":"aac"}"#.utf8))
        XCTAssertEqual(bad.audioFormat, .mp3)
    }

    func testProgressParsing() {
        let line = "[ypd-dl]512|1024|NA|2048.5|NA|NA|avc1.640028|none|mp4"
        XCTAssertEqual(YTDLPEvent.parse(line), .download(kind: .video, fraction: 0.5, speed: 2048.5))
        XCTAssertEqual(YTDLPEvent.parse("[ypd-dl]5|NA|NA|NA|2|4|NA|mp4a.40.2|m4a"), .download(kind: .audio, fraction: 0.5, speed: nil))
        XCTAssertNil(YTDLPEvent.parse("[ypd-dl]5|10|NA|NA|NA|NA|NA|NA|vtt"))
        XCTAssertEqual(YTDLPEvent.parse("[ypd-dl]5|10|NA|NA|NA|NA|NA|NA|mp4"), .download(kind: .muxed, fraction: 0.5, speed: nil))
        XCTAssertEqual(YTDLPEvent.parse("[ypd-pp]Merger|started"), .postprocess(name: "Merger", started: true))
        XCTAssertEqual(YTDLPEvent.parse("[ypd-file]/tmp/a b.mp4"), .file("/tmp/a b.mp4"))
        XCTAssertEqual(YTDLPEvent.parse("ERROR: [youtube] x: Video unavailable"), .error("[youtube] x: Video unavailable"))
        XCTAssertNil(YTDLPEvent.parse("[download] 10%"))

        var t = ProgressTracker()
        XCTAssertEqual(t.update(kind: .video, fraction: 1), 0.85, accuracy: 0.0001)
        XCTAssertEqual(t.update(kind: .audio, fraction: 0.5), 0.925, accuracy: 0.0001)
        var a = ProgressTracker()
        XCTAssertEqual(a.update(kind: .audio, fraction: 0.4), 0.4, accuracy: 0.0001)
        XCTAssertEqual(a.update(kind: .audio, fraction: 0.2), 0.4, accuracy: 0.0001) // 줄어들지 않음
    }

    func testDownloadArguments() {
        let tools = ToolPaths(ytdlp: URL(fileURLWithPath: "/t/yt-dlp"), ffmpeg: URL(fileURLWithPath: "/t/ffmpeg"),
                              jsRuntime: ("deno", URL(fileURLWithPath: "/t/deno")))
        var o = DownloadOptions()
        o.mode = .audio
        o.setBitrate = true
        o.bitrateKbps = 256
        let tags = AudioTags.make(entry: entry("Artist's - Song"), collectionTitle: "Album", collectionUploader: "Up")
        let req = DownloadRequest(url: "https://x", options: o, outputDirectory: URL(fileURLWithPath: "/out"),
                                  baseName: "100% song", tempDirectory: URL(fileURLWithPath: "/tmp/x"), tags: tags)
        let args = YTDLPArguments.download(req, tools: tools)
        XCTAssertTrue(args.contains("100%% song.%(ext)s"))
        XCTAssertEqual(args.suffix(2), ["--", "https://x"])
        XCTAssertTrue(args.contains("256K"))
        XCTAssertTrue(args.contains("deno:/t/deno"))
        XCTAssertTrue(args.contains("png"))
        XCTAssertFalse(args.contains("--no-js-runtimes"))
        let ppa = args.first { $0.hasPrefix("Metadata+ffmpeg_o:") }
        XCTAssertEqual(ppa, #"Metadata+ffmpeg_o:'-id3v2_version' '3' '-metadata' 'title=Song' '-metadata' 'artist=Artist'"'"'s' '-metadata' 'album=Album' '-metadata' 'album_artist=Up' '-metadata' 'track=3'"#)

        o.mode = .video
        o.downloadSubtitles = true
        let v = YTDLPArguments.download(DownloadRequest(url: "u", options: o, outputDirectory: URL(fileURLWithPath: "/o"), baseName: "n", tempDirectory: URL(fileURLWithPath: "/t"), tags: nil), tools: tools)
        XCTAssertTrue(v.contains("res:1080,vcodec:h264,acodec:aac"))
        XCTAssertTrue(v.contains("--embed-subs"))
        XCTAssertTrue(v.contains("en(-[A-Za-z]+)?,-.*-orig"))
        o.preferHighestFPS = true
        let fps = YTDLPArguments.download(DownloadRequest(url: "u", options: o, outputDirectory: URL(fileURLWithPath: "/o"), baseName: "n", tempDirectory: URL(fileURLWithPath: "/t"), tags: nil), tools: tools)
        XCTAssertTrue(fps.contains("res:1080,fps,vcodec:h264,acodec:aac"))
        XCTAssertFalse(v.contains("--embed-metadata"))

        let node = ToolPaths(ytdlp: tools.ytdlp, ffmpeg: nil, jsRuntime: ("node", URL(fileURLWithPath: "/n/node")))
        let r = YTDLPArguments.resolve(url: "u", tools: node)
        XCTAssertTrue(r.contains("--no-js-runtimes"))
        XCTAssertFalse(r.contains("--ffmpeg-location"))
    }

    func testOutputName() {
        let files: Set = ["Song.mp3", "Song-1.mp3", "Other.webm"]
        func choose(_ base: String, skip: Bool, reserved: Set<String> = []) -> String? {
            OutputName.choose(base: base, expected: ["mp3"], blocking: ["mp3", "webm"], skipExisting: skip,
                              isReserved: { reserved.contains($0) }, exists: { files.contains("\($0).\($1)") })
        }
        XCTAssertEqual(choose("New", skip: false), "New")
        XCTAssertEqual(choose("Song", skip: false), "Song-2")
        XCTAssertNil(choose("Song", skip: true))
        // 다른 확장자 파일만 있으면 건너뛰지 않고 새 이름을 씁니다(기존 webm을 yt-dlp가 지우지 않게).
        XCTAssertEqual(choose("Other", skip: true), "Other-1")
        // 다른 다운로드가 쓰기로 한 이름은 건너뛰지 않고 피합니다.
        XCTAssertEqual(choose("New", skip: true, reserved: ["New"]), "New-1")
    }
}
