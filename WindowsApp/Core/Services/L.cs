using System.Globalization;

namespace YPD.Core;

/// <summary>
/// 화면 문구. Mac의 String(localized:)처럼 영어 문구가 키이고, 한국어는 표에서 찾습니다(없으면 영어 그대로).
/// .resw는 PRI 도구가 필요해 쓰지 않습니다. 키의 ’(U+2019)와 …(U+2026)도 그대로 맞춰 써야 합니다.
/// </summary>
public static class L
{
    /// <summary>
    /// 한국어로 보여 줄지. 앱 시작 때 <see cref="Configure"/>로 한 번 정합니다(언어 설정은 다음 실행부터 적용).
    /// </summary>
    public static bool IsKorean { get; private set; } = Resolve(AppLanguage.System);

    public static void Configure(AppLanguage language) => IsKorean = Resolve(language);

    private static bool Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Ko => true,
        AppLanguage.En => false,
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko",
    };

    public static string T(string key) => T(key, IsKorean);

    /// <summary>자리표시자는 {0}, {1} 순서입니다. 한국어 문구는 어순이 달라도 같은 번호를 씁니다.</summary>
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>영어 단수형("1 video")이 있는 키. 한국어는 단·복수가 같습니다.</summary>
    public static string Plural(string key, long count) => Plural(key, count, IsKorean, CultureInfo.CurrentCulture);

    internal static string T(string key, bool korean) => korean && Korean.TryGetValue(key, out var value) ? value : key;

    internal static string Plural(string key, long count, bool korean, IFormatProvider culture) =>
        string.Format(culture, !korean && count == 1 && EnglishSingular.TryGetValue(key, out var one) ? one : T(key, korean), count);

    // en.lproj/Localizable.stringsdict의 one 형태.
    internal static readonly IReadOnlyDictionary<string, string> EnglishSingular = new Dictionary<string, string>
    {
        ["{0} videos"] = "{0} video",
        ["{0} links"] = "{0} link",
        ["Added {0} downloads"] = "Added {0} download",
        ["{0} links could not be added"] = "{0} link could not be added",
    };

    // ko.lproj/Localizable.strings를 옮긴 표(%@·%lld·%d → {0}, %2$lld → {1}). 뒤쪽은 Windows 앱에서 새로 쓰는 문구입니다.
    internal static readonly IReadOnlyDictionary<string, string> Korean = new Dictionary<string, string>
    {
        ["{0} views"] = "조회수 {0}회",
        ["{0} kbps"] = "{0} kbps",
        ["{0} links"] = "링크 {0}개",
        ["{0} links could not be added"] = "링크 {0}개를 추가하지 못했습니다",
        ["{0} of {1}"] = "{0}/{1}",
        ["{0} of {1} will be downloaded with the current range and length filter"] = "현재 범위와 길이 필터로 {1}개 중 {0}개를 받습니다",
        ["{0} videos"] = "영상 {0}개",
        ["Add tags and cover art"] = "태그와 커버 이미지 넣기",
        ["Add to Queue"] = "대기열에 추가",
        ["Added {0} downloads"] = "다운로드 {0}개를 추가했습니다",
        ["Added to Downloads"] = "다운로드에 추가했습니다",
        ["Adding {0} of {1}…"] = "{1}개 중 {0}번째 추가 중…",
        ["Already exists"] = "이미 있음",
        ["Appearance"] = "화면 모드",
        ["Artist"] = "아티스트",
        ["Artist and title are read from the video title"] = "아티스트와 제목은 영상 제목에서 읽어 옵니다",
        ["Ask before quitting while downloading"] = "다운로드 중에 종료하면 확인하기",
        ["Audio"] = "오디오",
        ["Audio converted to the format below"] = "오디오를 아래 형식으로 변환합니다",
        ["Audio language"] = "오디오 언어",
        ["Best audio track as is, without converting"] = "가장 좋은 오디오를 변환하지 않고 그대로 받습니다",
        ["Bitrate"] = "비트레이트",
        ["Bulk Download"] = "일괄 다운로드",
        ["Cancel"] = "취소",
        ["Cancelled"] = "취소됨",
        ["Cannot write to the save folder \"{0}\": {1}"] = "저장 폴더 \"{0}\"에 쓸 수 없습니다: {1}",
        ["Channel"] = "채널",
        ["Check Again"] = "다시 확인",
        ["Checking for updates…"] = "업데이트 확인 중…",
        ["Choose"] = "선택",
        ["Choose…"] = "선택…",
        ["Clear"] = "지우기",
        ["Clear Finished"] = "완료 항목 지우기",
        ["Components"] = "구성 요소",
        ["Container"] = "파일 형식",
        ["Copy"] = "복사",
        ["Copy Error List"] = "오류 목록 복사",
        ["Could not read the video information."] = "영상 정보를 읽지 못했습니다.",
        ["Couldn’t add the download"] = "다운로드를 추가하지 못했습니다",
        ["Dark"] = "다크",
        ["Default"] = "기본",
        ["Dismiss"] = "닫기",
        ["Done"] = "완료",
        ["Done · {0} failed"] = "완료 · {0}개 실패",
        ["Don’t ask again"] = "다시 묻지 않기",
        ["Download"] = "다운로드",
        ["Download All"] = "모두 다운로드",
        ["Download Options"] = "다운로드 옵션",
        ["Download subtitles"] = "자막 받기",
        ["Downloading"] = "다운로드 중",
        ["Downloading yt-dlp {0}…"] = "yt-dlp {0} 받는 중…",
        ["Downloading…"] = "다운로드 중…",
        ["Downloads"] = "다운로드",
        ["Downloads are in progress"] = "다운로드가 진행 중입니다",
        ["Example: {0}"] = "예시: {0}",
        ["Failed"] = "실패",
        ["File name"] = "파일 이름",
        ["Files"] = "파일",
        ["Format"] = "형식",
        ["From"] = "시작",
        ["General"] = "일반",
        ["Genre"] = "장르",
        ["Hide Items"] = "항목 숨기기",
        ["Hide items"] = "항목 숨기기",
        ["Insert a token"] = "토큰 넣기",
        ["Install missing components with Homebrew:"] = "빠진 구성 요소는 Homebrew로 설치할 수 있습니다:",
        ["Install yt-dlp"] = "yt-dlp 설치",
        ["Installed"] = "설치됨",
        ["JavaScript runtime"] = "JavaScript 런타임",
        ["Keep yt-dlp up to date automatically"] = "yt-dlp 자동으로 최신 상태 유지",
        ["Leave To at 0 to download until the end"] = "끝까지 받으려면 끝을 0으로 두십시오",
        ["Length"] = "길이",
        ["Light"] = "라이트",
        ["Links not added.txt"] = "추가하지 못한 링크.txt",
        ["Longer than"] = "더 긴 영상",
        ["Looking up…"] = "찾는 중…",
        ["Lossless formats ignore bitrate"] = "무손실 형식은 비트레이트를 쓰지 않습니다",
        ["MP4 plays in QuickTime and on iPhone"] = "MP4는 QuickTime과 iPhone에서 재생됩니다",
        ["Max resolution"] = "최대 해상도",
        ["Minutes"] = "분",
        ["Needed for some YouTube formats"] = "일부 YouTube 형식에 필요합니다",
        ["New Download"] = "새 다운로드",
        ["No Downloads"] = "다운로드 없음",
        ["No JavaScript runtime (deno) was found. Some YouTube formats may be missing."] = "JavaScript 런타임(deno)을 찾지 못했습니다. 일부 YouTube 형식을 받지 못할 수 있습니다.",
        ["Not a valid link"] = "올바른 링크가 아닙니다",
        ["Not found"] = "찾을 수 없음",
        ["Not installed"] = "설치 안 됨",
        ["OK"] = "확인",
        ["Only download a range"] = "범위만 받기",
        ["Only download videos by length"] = "길이로 영상 거르기",
        ["Open folder when done"] = "끝나면 폴더 열기",
        ["Options…"] = "옵션…",
        ["Original Audio"] = "원본 오디오",
        ["Paste a link"] = "링크 붙여넣기",
        ["Paste a link to a YouTube video, playlist or channel"] = "YouTube 영상, 재생목록, 채널 링크를 붙여 넣으십시오",
        ["Paste from clipboard"] = "클립보드에서 붙여넣기",
        ["Paste one link per line. Each link becomes its own download"] = "한 줄에 링크 하나씩 넣으십시오. 링크마다 따로 다운로드합니다",
        ["Playlist"] = "재생목록",
        ["Playlist name"] = "재생목록 이름",
        ["Position in the playlist"] = "재생목록 안 순서",
        ["Prefer higher frame rate"] = "높은 프레임 레이트 우선",
        ["Processing"] = "처리 중",
        ["Processing…"] = "처리 중…",
        ["Queued"] = "대기 중",
        ["Quit"] = "종료",
        ["Quitting will stop all downloads. Unfinished files will not be saved."] = "종료하면 모든 다운로드가 멈추고, 끝나지 않은 파일은 저장되지 않습니다.",
        ["Remove"] = "제거",
        ["Remove finished downloads from the list"] = "끝난 다운로드를 목록에서 지웁니다",
        ["Remove from list"] = "목록에서 제거",
        ["Reset"] = "초기화",
        ["Reset Options"] = "옵션 초기화",
        ["Reset to $title"] = "$title로 초기화",
        ["Retry"] = "다시 시도",
        ["Retry Failed"] = "실패 항목 다시 시도",
        ["Retry failed downloads"] = "실패한 다운로드 다시 시도",
        ["Save Error List…"] = "오류 목록 저장…",
        ["Save each playlist in its own folder"] = "재생목록마다 폴더 따로 만들기",
        ["Save to"] = "저장 위치",
        ["Save…"] = "저장…",
        ["Set bitrate"] = "비트레이트 지정",
        ["Shorter than"] = "더 짧은 영상",
        ["Show Downloads"] = "다운로드 보기",
        ["Show Items"] = "항목 보기",
        ["Show in Finder"] = "Finder에서 보기",
        ["Show items"] = "항목 보기",
        ["Show or hide download options"] = "다운로드 옵션 보기/숨기기",
        ["Simultaneous downloads: {0}"] = "동시 다운로드: {0}개",
        ["Skip files that already exist"] = "이미 있는 파일 건너뛰기",
        ["Song title"] = "곡 제목",
        ["Stop"] = "중지",
        ["Stop All"] = "모두 중지",
        ["Stop all downloads"] = "모든 다운로드 중지",
        ["Subtitle language"] = "자막 언어",
        ["System"] = "시스템",
        ["The process exited with code {0}."] = "프로세스가 코드 {0}로 종료되었습니다.",
        ["There are no videos to download with the current range and length filter."] = "현재 범위와 길이 필터에 맞는 영상이 없습니다.",
        ["This doesn’t look like a link"] = "링크 형식이 아닙니다",
        ["Title"] = "제목",
        ["To"] = "끝",
        ["Try Again"] = "다시 시도",
        ["Update failed: {0}"] = "업데이트 실패: {0}",
        ["Update yt-dlp"] = "yt-dlp 업데이트",
        ["Updates"] = "업데이트",
        ["Video"] = "동영상",
        ["Video ID"] = "영상 ID",
        ["Video with sound, merged into one file"] = "소리가 있는 영상을 파일 하나로 합칩니다",
        ["Videos not downloaded.txt"] = "받지 못한 영상.txt",
        ["Videos you download appear here"] = "받는 영상이 여기에 표시됩니다",
        ["Waiting…"] = "대기 중…",
        ["YouTube Playlist Downloader"] = "YouTube Playlist Downloader",
        ["ffmpeg"] = "ffmpeg",
        ["ffmpeg was not found. Merging and converting will fail."] = "ffmpeg를 찾지 못했습니다. 병합과 변환을 할 수 없습니다.",
        ["min"] = "분",
        ["yt-dlp"] = "yt-dlp",
        ["yt-dlp is not installed."] = "yt-dlp가 설치되어 있지 않습니다.",
        ["yt-dlp is not installed. Install it in Settings → Components."] = "yt-dlp가 설치되어 있지 않습니다. 설정 → 구성 요소에서 설치하십시오.",
        ["yt-dlp is up to date ({0})."] = "yt-dlp가 최신 버전입니다({0}).",
        ["yt-dlp was updated to {0}."] = "yt-dlp를 {0} 버전으로 업데이트했습니다.",
        ["Language"] = "언어",
        ["Takes effect the next time you open the app"] = "앱을 다시 열면 적용됩니다",
        // Windows 앱 전용
        ["Show in File Explorer"] = "파일 탐색기에서 보기",
        ["MP4 plays in Media Player and on most devices"] = "MP4는 미디어 플레이어와 대부분의 기기에서 재생됩니다",
        ["Install missing components with winget:"] = "빠진 구성 요소는 winget으로 설치할 수 있습니다:",
        ["The server returned an unexpected response."] = "서버에서 예상하지 못한 응답이 왔습니다.",
        ["The downloaded file is damaged."] = "받은 파일이 손상되었습니다.",
        ["Settings"] = "설정",
        ["Simultaneous downloads"] = "동시 다운로드",
        ["On"] = "켬",
        ["Off"] = "끔",
        ["Data folder"] = "데이터 폴더",
        ["Open"] = "열기",
        ["Restart the app after installing them."] = "설치한 뒤 앱을 다시 여십시오.",
        ["Text document"] = "텍스트 문서",
        ["Couldn’t save the file"] = "파일을 저장하지 못했습니다",
        ["The app is already running from another folder. Quit it first."] = "다른 폴더에서 이미 앱이 실행 중입니다. 먼저 종료하십시오.",
        ["Open Data Folder"] = "데이터 폴더 열기",
    };
}
