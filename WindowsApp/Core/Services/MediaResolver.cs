using System.Text;

namespace YPD.Core;

/// <summary>사용자에게 그대로 보여 줄 오류.</summary>
public sealed class UserFacingError(string message) : Exception(message);

public static class MediaResolver
{
    /// <summary>
    /// 주소가 가리키는 영상·재생목록·채널의 정보와 항목 목록을 가져옵니다. 취소하면 yt-dlp도 멈추고 OperationCanceledException을 던집니다.
    /// </summary>
    /// <exception cref="UserFacingError">yt-dlp가 실패했거나 출력을 읽을 수 없을 때</exception>
    public static async Task<MediaSource> ResolveAsync(string url, ToolPaths tools, IReadOnlyDictionary<string, string> environment,
                                                       CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(tools.Ytdlp, YTDLPArguments.Resolve(url, tools), environment,
                                                  collectStdout: true, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != 0 || result.Stdout.Length == 0) throw new UserFacingError(result.ErrorMessage);
        try
        {
            return MediaSource.Parse(Encoding.UTF8.GetString(result.Stdout));
        }
        catch (Exception)
        {
            throw new UserFacingError(L.T("Could not read the video information."));
        }
    }
}
