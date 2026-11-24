using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace YPD.Core.Tests;

/// <summary>서비스 쪽 테스트. 실제 설정 파일과 싱글턴(Shared)은 건드리지 않습니다.</summary>
[Collection("ProcessEnvironment")]
public class ServicesTests
{
    // 설정

    private static string TempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "ypd-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void SettingsKeepDefaultsForMissingAndBrokenKeys()
    {
        string path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "maxConcurrentDownloads": 99, "appearance": "purple", "language": "ko", "options": { "mode": "audio" } }""");

        var settings = AppSettings.Load(path);
        Assert.Equal(6, settings.MaxConcurrentDownloads); // 범위로 자릅니다
        Assert.Equal(Appearance.System, settings.Appearance); // 없는 이름은 기본값
        Assert.Equal(AppLanguage.Ko, settings.Language);
        Assert.True(settings.ConfirmQuitWhileDownloading);
        Assert.True(settings.AutoUpdateYTDLP);
        Assert.Equal(DateTimeOffset.MinValue, settings.LastYTDLPCheck);
        Assert.NotEmpty(settings.SaveDirectory);
        Assert.Equal(MediaMode.Audio, settings.Options.Mode);
        Assert.Equal(new DownloadOptions { Mode = MediaMode.Audio }, settings.Options);
    }

    [Fact]
    public void SettingsSaveOnChangeAndRoundTrip()
    {
        string path = TempSettingsPath();
        var settings = AppSettings.Load(path);
        Assert.False(File.Exists(path)); // 읽기만 해서는 쓰지 않습니다

        var changed = new List<string?>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var checkedAt = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.FromHours(9));
        settings.Appearance = Appearance.Dark;
        settings.MaxConcurrentDownloads = 4;
        settings.MaxConcurrentDownloads = 4; // 같은 값은 알리지 않습니다
        settings.SaveDirectory = @"C:\Users\me\Videos";
        settings.LastYTDLPCheck = checkedAt;
        settings.UpdateOptions(o => o.FilenamePattern = "$index - $title");
        Assert.Equal(["Appearance", "MaxConcurrentDownloads", "SaveDirectory", "LastYTDLPCheck", "Options"], changed);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Contains("\"appearance\": \"dark\"", File.ReadAllText(path));

        var reloaded = AppSettings.Load(path);
        Assert.Equal(Appearance.Dark, reloaded.Appearance);
        Assert.Equal(4, reloaded.MaxConcurrentDownloads);
        Assert.Equal(@"C:\Users\me\Videos", reloaded.SaveDirectory);
        Assert.Equal(checkedAt, reloaded.LastYTDLPCheck);
        Assert.Equal("$index - $title", reloaded.Options.FilenamePattern);
    }

    [Fact]
    public void BrokenSettingsFileOpensWithDefaults()
    {
        string path = TempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Equal(2, AppSettings.Load(path).MaxConcurrentDownloads);
    }

    // 도구

    [Fact]
    public void VersionTagsCompareNumerically()
    {
        Assert.True(ToolManager.CompareVersions("2026.11.23", "2026.9.30") > 0);
        Assert.True(ToolManager.CompareVersions("2026.01.02", "2026.1.10") < 0);
        Assert.Equal(0, ToolManager.CompareVersions("2026.11.23", "2026.11.23"));
        Assert.True(ToolManager.CompareVersions("2026.11.23.1", "2026.11.23") > 0);
        Assert.True(ToolManager.CompareVersions("2025.12.31", "2026.01.01") < 0);
    }

    [Fact]
    public void ReleaseTagsAreValidatedBeforeUseAsFolderNames()
    {
        Assert.True(ToolManager.IsValidTag("2026.11.23"));
        Assert.True(ToolManager.IsValidTag("2026.11.23.232323"));
        foreach (string bad in new[] { "", ".", "..", "a/b", @"a\b", "C:x", "1..2", ".hidden", "a b" })
            Assert.False(ToolManager.IsValidTag(bad), bad);
    }

    // 문구

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Value).Distinct().Order()];

    [Fact]
    public void KoreanTableHasEveryMacStringWithMatchingPlaceholders()
    {
        Assert.True(L.Korean.Count >= 160);
        foreach (var (key, value) in L.Korean)
        {
            Assert.Equal(Placeholders(key), Placeholders(value));
            Assert.DoesNotMatch(@"%(\d+\$)?(@|lld|d)", key + value); // 변환하지 않은 %@·%lld가 남지 않았는지
        }
        foreach (var (key, one) in L.EnglishSingular)
        {
            Assert.True(L.Korean.ContainsKey(key), key);
            Assert.Equal(Placeholders(key), Placeholders(one));
        }
    }

    [Fact]
    public void TranslationsAndPlurals()
    {
        Assert.Equal("취소", L.T("Cancel", korean: true));
        Assert.Equal("Cancel", L.T("Cancel", korean: false));
        Assert.Equal("Not in the table", L.T("Not in the table", korean: true));
        Assert.Equal("현재 범위와 길이 필터로 10개 중 3개를 받습니다",
            string.Format(CultureInfo.InvariantCulture, L.T("{0} of {1} will be downloaded with the current range and length filter", true), 3, 10));
        Assert.Equal("Don’t ask again", L.T("Don’t ask again", false));
        Assert.Equal("다시 묻지 않기", L.T("Don’t ask again", true));

        var inv = CultureInfo.InvariantCulture;
        Assert.Equal("1 video", L.Plural("{0} videos", 1, false, inv));
        Assert.Equal("2 videos", L.Plural("{0} videos", 2, false, inv));
        Assert.Equal("Added 1 download", L.Plural("Added {0} downloads", 1, false, inv));
        Assert.Equal("영상 1개", L.Plural("{0} videos", 1, true, inv));
    }

    [Fact]
    public void FormattersMatchMacOutput()
    {
        var inv = CultureInfo.InvariantCulture;
        Assert.Equal("1.2 MB/s", Formatters.Speed(1_234_567, inv));
        Assert.Equal("12 KB/s", Formatters.Speed(12_345, inv));
        Assert.Equal("999 B/s", Formatters.Speed(999, inv));
        Assert.Equal("1.0 MB/s", Formatters.Speed(999_999, inv));
        Assert.Equal("2.35 GB/s", Formatters.Speed(2_345_678_901, inv));
        Assert.Null(Formatters.Speed(0, inv));
        Assert.Null(Formatters.Speed(null, inv));

        Assert.Equal("3:20", Formatters.Duration(200));
        Assert.Equal("0:05", Formatters.Duration(5.7));
        Assert.Equal("1:02:03", Formatters.Duration(3723));
        Assert.Null(Formatters.Duration(0));
        Assert.Null(Formatters.Duration(null));
    }

    // 프로세스

    private static List<string> Split(params string[] chunks)
    {
        var lines = new List<string>();
        var reader = new LineReader(false, lines.Add);
        foreach (string chunk in chunks) reader.Append(Encoding.UTF8.GetBytes(chunk));
        reader.Flush();
        return lines;
    }

    [Fact]
    public void LineReaderSplitsOnCarriageReturnAndNewline()
    {
        Assert.Equal(["a", "b", "c", "d"], Split("a\rb\nc\r\nd"));
        Assert.Equal(["abc", "de"], Split("ab", "c\n", "d", "e"));
        Assert.Equal(["10%", "20%", "done"], Split("10%\r", "20%\r", "\ndone\n"));
        Assert.Empty(Split("\r\n\n"));

        // 여러 바이트 글자가 조각 경계에서 나뉘어도 깨지지 않습니다.
        byte[] korean = Encoding.UTF8.GetBytes("한글 제목\n");
        var lines = new List<string>();
        var reader = new LineReader(false, lines.Add);
        reader.Append(korean.AsSpan(0, 1));
        reader.Append(korean.AsSpan(1));
        Assert.Equal(["한글 제목"], lines);
    }

    [Fact]
    public void LineReaderDropsHugeUnterminatedOutputAndCollects()
    {
        var lines = new List<string>();
        var reader = new LineReader(false, lines.Add);
        reader.Append(new byte[(1 << 20) + 1]);
        reader.Append("tail\n"u8);
        Assert.Equal(["tail"], lines);

        var collector = new LineReader(true, lines.Add);
        collector.Append("a\nb"u8);
        collector.Flush();
        Assert.Equal("a\nb", Encoding.UTF8.GetString(collector.Collected));
        Assert.Single(lines); // 모으는 동안에는 줄을 넘기지 않습니다
    }

    [Fact]
    public void ErrorMessagePrefersLastErrorLine()
    {
        Assert.Equal("Video unavailable",
            new ProcessResult(1, [], ["ERROR: first", "ERROR:  Video unavailable ", "trailing"], false).ErrorMessage);
        Assert.Equal("trailing", new ProcessResult(1, [], ["warning", "trailing"], false).ErrorMessage);
        Assert.Contains("7", new ProcessResult(7, [], [], false).ErrorMessage);
    }

    [Fact]
    public void ChildEnvironmentInheritsAndPrependsToolDirectories()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tools");
        var env = ProcessRunner.Environment([dir]);
        Assert.StartsWith(dir + Path.PathSeparator, env["PATH"]);
        Assert.Equal("utf-8", env["PYTHONIOENCODING"]);
        Assert.Equal("1", env["PYTHONUTF8"]);
        string inherited = Environment.GetEnvironmentVariables().Keys.Cast<string>().First(k => k != "PATH");
        Assert.True(env.ContainsKey(inherited));
        // 파일 이름의 $변수 판정이 자식 환경과 맞아야 합니다.
        Assert.All(env.Keys, key => Assert.True(FilenameBuilder.IsEnvironmentVariable(key), key));
    }

    [Fact]
    public void SupportDirectoryIsPortableUnlessUnwritable()
    {
        string root = Path.Combine(Path.GetTempPath(), "ypd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string portable = Path.Combine(root, "data");
        Assert.Equal(portable, ToolManager.PickSupportDirectory(portable, "fallback"));
        Assert.Empty(Directory.GetFiles(portable, "*", SearchOption.AllDirectories));
        // 같은 이름의 파일이 있어 폴더를 만들 수 없으면 대신 쓸 곳으로 갑니다.
        string blocked = Path.Combine(root, "blocked");
        File.WriteAllText(blocked, "");
        Assert.Equal("fallback", ToolManager.PickSupportDirectory(blocked, "fallback"));
    }

    // 실제 프로세스: Windows에는 /bin/sh가 없어 Mac·Linux에서만 확인합니다.

    private static Task<ProcessResult> Shell(string script, List<string>? lines = null, bool collect = false, CancellationToken token = default) =>
        ProcessRunner.RunAsync("/bin/sh", ["-c", script], ProcessRunner.Environment([]), collect,
            line => { if (lines != null) lock (lines) lines.Add(line); }, token);

    [Fact]
    public async Task RunnerReportsLinesExitCodeAndError()
    {
        if (OperatingSystem.IsWindows()) return;
        var lines = new List<string>();
        var result = await Shell("echo out; printf 'p1\\rp2\\r'; echo 'ERROR: boom' >&2; echo last >&2; exit 3", lines);
        Assert.Equal(3, result.Status);
        Assert.False(result.WasCancelled);
        Assert.Equal("boom", result.ErrorMessage);
        Assert.Equal(["ERROR: boom", "last"], result.StderrTail);
        Assert.Contains("out", lines);
        Assert.Contains("p2", lines);

        var collected = await Shell("printf 'a\\nb'; printf '%s' \"$PYTHONUTF8\"", collect: true);
        Assert.Equal("a\nb1", Encoding.UTF8.GetString(collected.Stdout));
    }

    [Fact]
    public async Task RunnerCancelKillsTheProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = Stopwatch.StartNew();
        var result = await Shell("sleep 30", token: cancellation.Token);
        Assert.True(result.WasCancelled);
        Assert.NotEqual(0, result.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task RunnerFinishesWhenAGrandchildHoldsThePipe()
    {
        if (OperatingSystem.IsWindows()) return;
        var watch = Stopwatch.StartNew();
        var result = await Shell("sleep 6 & echo started");
        Assert.Equal(0, result.Status);
        Assert.InRange(watch.Elapsed.TotalSeconds, 1.5, 5);
    }

    [Fact]
    public async Task RunnerThrowsWhenTheExecutableIsMissing()
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            ProcessRunner.RunAsync("/nonexistent/yt-dlp", [], ProcessRunner.Environment([])));
    }
}
