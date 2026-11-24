using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YPD.Core;

/// <summary>
/// yt-dlp, ffmpeg, JavaScript 런타임(deno/node)의 위치를 찾고 yt-dlp를 최신으로 유지합니다.
/// 찾는 순서: 앱이 업데이트한 yt-dlp(번들보다 새로울 때만) → 실행 파일 옆 tools 폴더 → PATH.
/// UI 스레드에서 부르고 기다립니다(await가 UI 스레드로 돌아와 속성을 바꿉니다).
/// </summary>
public sealed partial class ToolManager : ObservableObject
{
    private static readonly Lazy<ToolManager> shared = new(() => new ToolManager());
    public static ToolManager Shared => shared.Value;

    private ToolManager() { }

    private ToolPaths? paths;
    private string? ytdlpVersion;
    private bool hasFFmpeg;
    private string? jsRuntimeName;
    private bool isChecking = true;
    private bool isUpdating;
    private string? updateStatus;

    public ToolPaths? Paths
    {
        get => paths;
        private set { if (Set(ref paths, value)) OnPropertyChanged(nameof(IsReady)); }
    }
    public string? YtdlpVersion { get => ytdlpVersion; private set => Set(ref ytdlpVersion, value); }
    public bool HasFFmpeg { get => hasFFmpeg; private set => Set(ref hasFFmpeg, value); }
    public string? JsRuntimeName { get => jsRuntimeName; private set => Set(ref jsRuntimeName, value); }
    public bool IsChecking { get => isChecking; private set => Set(ref isChecking, value); }
    public bool IsUpdating { get => isUpdating; private set => Set(ref isUpdating, value); }
    /// <summary>마지막 업데이트 결과(성공·실패 문구). 설정 화면에 보여 줍니다.</summary>
    public string? UpdateStatus { get => updateStatus; private set => Set(ref updateStatus, value); }

    public bool IsReady => Paths != null;

    public static string SupportDirectory { get; } =
        Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "YoutubePlaylistDownloader");
    public static string ManagedYTDLPRoot => Path.Combine(SupportDirectory, "yt-dlp");
    public static string BundledToolsDirectory => Path.Combine(AppContext.BaseDirectory, "tools");

    private const string YtdlpExe = "yt-dlp.exe";

    /// <summary>자식 프로세스 PATH 앞에 넣을 폴더(ffprobe 등 yt-dlp가 이름으로 찾는 도구용).</summary>
    public List<string> ToolDirectories
    {
        get
        {
            List<string> dirs = [];
            if (Paths?.Ffmpeg is { } ffmpeg) dirs.Add(Path.GetDirectoryName(ffmpeg)!);
            if (Paths?.JsRuntime is { } js) dirs.Add(Path.GetDirectoryName(js.Path)!);
            return dirs;
        }
    }

    public Dictionary<string, string> Environment => ProcessRunner.Environment(ToolDirectories);

    /// <summary>실행 파일 위치를 다시 찾고 yt-dlp 버전을 읽습니다. 디스크만 보므로 빠르지만 UI 스레드 밖에서 찾습니다.</summary>
    public async Task RefreshAsync()
    {
        IsChecking = true;
        var found = await Task.Run(Locate);
        Paths = found;
        HasFFmpeg = found?.Ffmpeg != null;
        JsRuntimeName = found?.JsRuntime?.Name;
        YtdlpVersion = found != null ? await ReadVersionAsync(found.Ytdlp, Environment) : null;
        IsChecking = false;
    }

    /// <summary>하루에 한 번 조용히 yt-dlp 업데이트를 확인합니다. 유튜브 쪽 변경으로 다운로드가 깨지는 일을 줄입니다.</summary>
    public async Task AutoUpdateIfNeededAsync(AppSettings settings)
    {
        if (!settings.AutoUpdateYTDLP || DateTimeOffset.Now - settings.LastYTDLPCheck <= TimeSpan.FromHours(24)) return;
        await Task.Delay(TimeSpan.FromSeconds(3)); // 시작 직후의 다른 작업과 겹치지 않게 조금 늦춥니다.
        await UpdateYTDLPAsync(settings, quiet: true);
    }

    public async Task UpdateYTDLPAsync(AppSettings settings, bool quiet = false)
    {
        if (IsUpdating) return;
        IsUpdating = true;
        try
        {
            if (!quiet) UpdateStatus = L.T("Checking for updates…");
            string latest = await LatestYTDLPTagAsync();
            settings.LastYTDLPCheck = DateTimeOffset.Now;
            if (YtdlpVersion == latest)
            {
                if (!quiet) UpdateStatus = L.F("yt-dlp is up to date ({0}).", latest);
                return;
            }
            if (!quiet) UpdateStatus = L.F("Downloading yt-dlp {0}…", latest);
            await Task.Run(() => InstallAsync(latest));
            await RefreshAsync();
            UpdateStatus = L.F("yt-dlp was updated to {0}.", latest);
        }
        catch (Exception e)
        {
            if (!quiet || Paths == null) UpdateStatus = L.F("Update failed: {0}", e.Message);
        }
        finally
        {
            IsUpdating = false;
        }
    }

    // MARK: 찾기

    private static ToolPaths? Locate()
    {
        string bundled = BundledToolsDirectory;
        string? bundledYTDLP = Existing(Path.Combine(bundled, "yt-dlp", YtdlpExe));
        // 업데이트한 버전이 번들 버전보다 새로울 때만 씁니다. 앱을 새로 설치하면 번들이 더 새로울 수 있습니다.
        string? managed = NewestManagedYTDLP();
        if (managed != null && bundledYTDLP != null && BundledYTDLPVersion() is { } bundledTag
            && CompareVersions(Path.GetFileName(Path.GetDirectoryName(managed)!), bundledTag) <= 0)
            managed = null;
        string? ytdlp = managed ?? bundledYTDLP ?? FindOnPath(YtdlpExe);
        if (ytdlp == null) return null;

        string? Find(string name) => Existing(Path.Combine(bundled, name)) ?? FindOnPath(name);
        (string, string)? js = Find("deno.exe") is { } deno ? ("deno", deno)
            : Find("node.exe") is { } node ? ("node", node) : null;
        return new ToolPaths(ytdlp, Find("ffmpeg.exe"), js);
    }

    /// <summary>빌드 스크립트가 번들 yt-dlp 옆에 남긴 버전(태그).</summary>
    private static string? BundledYTDLPVersion()
    {
        try
        {
            string tag = File.ReadAllText(Path.Combine(BundledToolsDirectory, "yt-dlp", "VERSION")).Trim();
            return tag.Length == 0 ? null : tag;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static string? FindOnPath(string name) =>
        (System.Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Existing(Path.Combine(dir.Trim('"'), name)))
            .FirstOrDefault(path => path != null);

    /// <summary>업데이트한 yt-dlp는 버전별 폴더에 둡니다. 실행 중인 다운로드가 쓰는 파일을 지우지 않기 위해서입니다.</summary>
    private static string? NewestManagedYTDLP()
    {
        if (!Directory.Exists(ManagedYTDLPRoot)) return null;
        try
        {
            return Directory.GetDirectories(ManagedYTDLPRoot)
                .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
                .OrderByDescending(dir => Path.GetFileName(dir), Comparer<string>.Create(CompareVersions))
                .Select(dir => Existing(Path.Combine(dir, YtdlpExe)))
                .FirstOrDefault(path => path != null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>지난 실행에서 남은 오래된 버전 폴더와 받다 만 파일을 지웁니다. 아무것도 실행 중이지 않을 때(앱 시작 시)만 부릅니다.</summary>
    public static void RemoveOldManagedVersions()
    {
        if (NewestManagedYTDLP() is not { } newestExe) return;
        string newest = Path.GetDirectoryName(newestExe)!;
        try
        {
            foreach (string entry in Directory.GetFileSystemEntries(ManagedYTDLPRoot))
            {
                if (string.Equals(entry, newest, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // 다음 실행에서 다시 지웁니다.
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 버전 태그 비교. "."로 나눈 조각이 둘 다 숫자면 숫자로, 아니면 글자로 비교합니다(Mac의 .numeric 비교와 같은 결과).
    /// 조각이 더 많은 쪽이 새 버전입니다("2026.11.23" &lt; "2026.11.23.1").
    /// </summary>
    internal static int CompareVersions(string a, string b)
    {
        string[] x = a.Split('.'), y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            int result = long.TryParse(x[i], out long m) && long.TryParse(y[i], out long n)
                ? m.CompareTo(n) : string.CompareOrdinal(x[i], y[i]);
            if (result != 0) return result;
        }
        return x.Length.CompareTo(y.Length);
    }

    private static async Task<string?> ReadVersionAsync(string ytdlp, Dictionary<string, string> environment)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(ytdlp, ["--version"], environment, collectStdout: true);
            if (result.Status != 0) return null;
            string text = Encoding.UTF8.GetString(result.Stdout).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    // MARK: 업데이트

    // GitHub API는 User-Agent가 없으면 요청을 거절합니다.
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("YoutubePlaylistDownloader");
        return client;
    }

    /// <summary>폴더 이름과 주소에 그대로 쓰므로 "2026.11.23" 같은 모양만 받습니다(/, \, :, .., "." 차단).</summary>
    internal static bool IsValidTag(string tag) => ValidTag().IsMatch(tag) && !tag.Contains("..", StringComparison.Ordinal);

    [GeneratedRegex(@"^[0-9A-Za-z][0-9A-Za-z._-]*$")]
    private static partial Regex ValidTag();

    private static async Task<string> LatestYTDLPTagAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await Http.SendAsync(request, timeout.Token);
        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException(L.T("The server returned an unexpected response."));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        if (json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
            && tag.GetString() is { } value && IsValidTag(value))
            return value;
        throw new HttpRequestException(L.T("The server returned an unexpected response."));
    }

    /// <summary>PyInstaller onedir 묶음(yt-dlp.exe + _internal). 한 파일짜리 yt-dlp.exe보다 매번 시작이 훨씬 빠릅니다.</summary>
    private static string AssetName() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => "yt-dlp_win_arm64.zip",
        Architecture.X86 => "yt-dlp_win_x86.zip",
        _ => "yt-dlp_win.zip",
    };

    private static async Task InstallAsync(string tag)
    {
        // 같은 버전이 이미 있으면 다시 받지 않습니다. 실행 중인 다운로드가 쓰는 폴더를 지우지 않기 위해서이기도 합니다.
        string target = Path.Combine(ManagedYTDLPRoot, tag);
        if (File.Exists(Path.Combine(target, YtdlpExe))) return;

        Directory.CreateDirectory(ManagedYTDLPRoot);
        string id = Guid.NewGuid().ToString("N");
        string zip = Path.Combine(ManagedYTDLPRoot, $".download-{id}.zip");
        string staging = Path.Combine(ManagedYTDLPRoot, $".staging-{id}");
        try
        {
            // 본문을 받는 동안에는 HttpClient.Timeout이 적용되지 않아, 멈춘 다운로드가 IsUpdating을 계속 붙잡지 않게 끝까지 시간을 잽니다.
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            using (var response = await Http.GetAsync($"https://github.com/yt-dlp/yt-dlp/releases/download/{tag}/{AssetName()}",
                                                      HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException(L.T("The server returned an unexpected response."));
                await using var file = File.Create(zip);
                await response.Content.CopyToAsync(file, timeout.Token);
            }
            ZipFile.ExtractToDirectory(zip, staging);
            // Arm64·x86 묶음의 실행 파일은 yt-dlp_arm64.exe처럼 이름이 달라 맞춥니다(onedir는 이름과 상관없이 옆의 _internal을 씁니다).
            if (!File.Exists(Path.Combine(staging, YtdlpExe)) && Directory.GetFiles(staging, "yt-dlp_*.exe") is [var renamed])
                File.Move(renamed, Path.Combine(staging, YtdlpExe));
            if (!File.Exists(Path.Combine(staging, YtdlpExe))) throw new InvalidDataException(L.T("The downloaded file is damaged."));
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
        }
        finally
        {
            try { File.Delete(zip); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
