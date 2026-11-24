using System.Globalization;

namespace YPD.Core;

public enum StreamKind { Video, Audio, Muxed }

public abstract record YTDLPEvent
{
    public sealed record Download(StreamKind Kind, double? Fraction, double? Speed) : YTDLPEvent;
    public sealed record Postprocess(string Name, bool Started) : YTDLPEvent;
    public sealed record File(string Path) : YTDLPEvent;
    public sealed record Error(string Message) : YTDLPEvent;

    private static readonly HashSet<string> SubtitleExtensions = ["vtt", "srt", "ass", "ssa", "ttml", "srv1", "srv2", "srv3", "json3", "lrc"];

    /// <summary>한 줄을 해석합니다. 관심 없는 줄이면 null.</summary>
    public static YTDLPEvent? Parse(string line)
    {
        if (DropPrefix(line, YTDLPArguments.DownloadMarker) is { } download)
        {
            string[] f = download.Split('|');
            if (f.Length < 8) return null;
            bool hasVideo = IsRealCodec(f[6]), hasAudio = IsRealCodec(f[7]);
            // 자막 파일은 진행률 계산에서 뺍니다. 코덱 정보를 주지 않는 사이트의 영상은 한 덩어리로 봅니다.
            if (!hasVideo && !hasAudio && (f.Length < 9 || SubtitleExtensions.Contains(f[8].ToLowerInvariant()))) return null;
            var kind = hasVideo == hasAudio ? StreamKind.Muxed : (hasVideo ? StreamKind.Video : StreamKind.Audio);

            double? downloaded = Number(f[0]);
            double? total = Number(f[1]) ?? Number(f[2]);
            double? fraction = null;
            if (downloaded is { } d && total is { } t && t > 0)
                fraction = d / t;
            else if (Number(f[4]) is { } index && Number(f[5]) is { } count && count > 0)
                fraction = index / count;
            return new Download(kind, fraction is { } x ? Math.Clamp(x, 0, 1) : null, Number(f[3]));
        }
        if (DropPrefix(line, YTDLPArguments.PostprocessMarker) is { } postprocess)
        {
            string[] f = postprocess.Split('|');
            if (f.Length < 2) return null;
            return new Postprocess(f[0], f[1] == "started" || f[1] == "processing");
        }
        if (DropPrefix(line, YTDLPArguments.FileMarker) is { } file)
            return file.Length == 0 ? null : new File(file);
        if (DropPrefix(line, "ERROR:") is { } error)
            return new Error(error.Trim());
        return null;
    }

    private static string? DropPrefix(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;

    private static bool IsRealCodec(string value) => value.Length > 0 && value != "NA" && value != "none" && value != "None";

    private static double? Number(string value) =>
        value != "NA" && value != "None" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n)
            ? n : null;
}

/// <summary>영상·오디오 스트림을 따로 받는 경우에도 한 개의 진행률(0~1)로 합칩니다. 값은 줄어들지 않습니다.</summary>
public sealed class ProgressTracker
{
    private double videoFraction;
    private double audioFraction;
    private bool sawVideo;
    private double overall;

    public double Update(StreamKind kind, double fraction)
    {
        switch (kind)
        {
            case StreamKind.Muxed:
                videoFraction = fraction;
                audioFraction = fraction;
                break;
            case StreamKind.Video:
                sawVideo = true;
                videoFraction = fraction;
                break;
            case StreamKind.Audio:
                audioFraction = fraction;
                break;
        }
        // 영상 스트림이 대부분의 용량을 차지합니다.
        double value = sawVideo ? videoFraction * 0.85 + audioFraction * 0.15 : (kind == StreamKind.Muxed ? videoFraction : audioFraction);
        overall = Math.Max(overall, Math.Min(value, 1));
        return overall;
    }
}
