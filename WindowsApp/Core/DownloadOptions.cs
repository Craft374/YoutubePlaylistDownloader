using System.Text.Json.Serialization;

namespace YPD.Core;

/// <summary>무엇을 받을지: 동영상, 변환한 오디오, 변환하지 않은 원본 오디오.</summary>
public enum MediaMode { Video, Audio, AudioOriginal }

public enum VideoContainer { Mp4, Mkv }

public enum AudioFormat { Mp3, M4a, Opus, Ogg, Flac, Wav, Alac }

public static class MediaFormatExtensions
{
    /// <summary>"mp4", "mkv". yt-dlp 인자와 확장자로 씁니다.</summary>
    public static string Name(this VideoContainer container) => container == VideoContainer.Mp4 ? "mp4" : "mkv";

    /// <summary>yt-dlp <c>--audio-format</c> 값.</summary>
    public static string YtdlpName(this AudioFormat format) => format == AudioFormat.Ogg ? "vorbis" : format.ToString().ToLowerInvariant();

    public static string FileExtension(this AudioFormat format) => format == AudioFormat.Alac ? "m4a" : format.ToString().ToLowerInvariant();

    public static bool IsLossless(this AudioFormat format) => format is AudioFormat.Flac or AudioFormat.Wav or AudioFormat.Alac;

    /// <summary>yt-dlp가 커버 이미지를 넣을 수 있는 형식인지. wav는 지원하지 않아 넣으면 후처리 오류가 납니다.</summary>
    public static bool SupportsCoverArt(this AudioFormat format) => format != AudioFormat.Wav;
}

public static class Resolution
{
    public static readonly IReadOnlyList<int> Heights = [144, 240, 360, 480, 720, 1080, 1440, 2160, 4320];
}

/// <summary>
/// 다운로드 옵션. record라 값으로 비교되며, 작업에 넘길 때는 <c>options with { }</c>로 복사해 이후 화면 수정이 반영되지 않게 합니다.
/// </summary>
[JsonConverter(typeof(LenientJsonConverter<DownloadOptions>))]
public sealed record DownloadOptions
{
    public const string DefaultPattern = "$title";

    public MediaMode Mode { get; set; } = MediaMode.Video;

    // 동영상
    public VideoContainer VideoContainer { get; set; } = VideoContainer.Mp4;
    public int MaxHeight { get; set; } = 1080;
    public bool PreferHighestFPS { get; set; }
    public bool DownloadSubtitles { get; set; }
    public string SubtitleLanguage { get; set; } = "en";
    /// <summary>빈 문자열이면 기본 오디오 트랙.</summary>
    public string AudioLanguage { get; set; } = "";

    // 오디오
    public AudioFormat AudioFormat { get; set; } = AudioFormat.Mp3;
    public bool SetBitrate { get; set; }
    public int BitrateKbps { get; set; } = 192;
    public bool TagAudio { get; set; } = true;

    // 공통
    public bool SavePlaylistsInSubfolders { get; set; }
    public bool OpenFolderWhenDone { get; set; }
    public string FilenamePattern { get; set; } = DefaultPattern;
    public bool SkipExisting { get; set; }

    // 재생목록 범위와 길이 필터
    public bool UseRange { get; set; }
    public int RangeStart { get; set; } = 1;
    /// <summary>0이면 끝까지.</summary>
    public int RangeEnd { get; set; }
    public bool FilterByLength { get; set; }
    public bool FilterLonger { get; set; } = true;
    public double FilterMinutes { get; set; } = 4;

    /// <summary>받을 항목. 범위와 길이 필터는 재생목록·채널에만 적용합니다(단일 영상에는 적용하지 않음).</summary>
    public List<MediaEntry> SelectEntries(MediaSource source) =>
        source.Kind == MediaKind.Video ? [.. source.Entries] : SelectEntries(source.Entries);

    /// <summary>
    /// 재생목록 범위(1부터 시작, 끝 포함)와 길이 필터를 적용한 항목.
    /// 길이를 모르는 항목(라이브 등)은 필터에서 제외하지 않습니다.
    /// </summary>
    public List<MediaEntry> SelectEntries(IEnumerable<MediaEntry> entries)
    {
        var result = entries;
        if (UseRange)
        {
            int start = Math.Max(1, RangeStart);
            int end = RangeEnd <= 0 ? int.MaxValue : RangeEnd;
            result = result.Where(e => e.Index >= start && e.Index <= end);
        }
        if (FilterByLength)
        {
            double limit = FilterMinutes * 60;
            result = result.Where(e => e.Duration is not { } d || (FilterLonger ? d > limit : d < limit));
        }
        return [.. result];
    }
}
