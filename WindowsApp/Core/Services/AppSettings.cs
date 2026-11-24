using System.Text.Json;
using System.Text.Json.Serialization;

namespace YPD.Core;

public enum Appearance { System, Light, Dark }

/// <summary>화면 언어. 시스템 언어 대신 고를 수 있습니다(다음 실행부터 적용).</summary>
public enum AppLanguage { System, En, Ko }

/// <summary>
/// 앱 설정과 다운로드 옵션. 바뀌는 즉시 %LOCALAPPDATA%\YoutubePlaylistDownloader\settings.json에 저장합니다.
/// 패키지 앱 전용 API(ApplicationData)는 패키지 없이 실행하면 예외가 나므로 파일을 직접 씁니다.
/// 파일의 키를 하나씩 읽어(LenientJsonConverter) 없는 키는 기본값을 쓰므로, 새 설정을 추가해도 기존 설정이 지워지지 않습니다.
/// UI 스레드에서만 바꿉니다.
/// </summary>
[JsonConverter(typeof(LenientJsonConverter<AppSettings>))]
public sealed class AppSettings : ObservableObject
{
    private static readonly Lazy<AppSettings> shared = new(() => Load(DefaultPath));
    public static AppSettings Shared => shared.Value;

    public static string DefaultPath => Path.Combine(ToolManager.SupportDirectory, "settings.json");

    public static readonly (int Min, int Max) ConcurrencyRange = (1, 6);

    /// <summary>저장할 파일. 읽는 동안에는 null이라, 읽으면서 값을 채울 때 저장하지 않습니다.</summary>
    private string? path;

    private Appearance appearance = Appearance.System;
    private AppLanguage language = AppLanguage.System;
    private string saveDirectory = DefaultSaveDirectory();
    private int maxConcurrentDownloads = 2;
    private bool confirmQuitWhileDownloading = true;
    private bool autoUpdateYTDLP = true;
    private DateTimeOffset lastYTDLPCheck = DateTimeOffset.MinValue;
    private DownloadOptions options = new();

    /// <summary>JSON 변환기가 씁니다. 앱에서는 <see cref="Shared"/>를 쓰십시오.</summary>
    public AppSettings() { }

    /// <summary>바뀌면 App이 바로 창에 적용합니다(PropertyChanged).</summary>
    public Appearance Appearance { get => appearance; set { if (Set(ref appearance, value)) Save(); } }

    /// <summary>앱을 다시 열면 적용됩니다. 시작할 때 L.Configure에 넘깁니다.</summary>
    public AppLanguage Language { get => language; set { if (Set(ref language, value)) Save(); } }

    public string SaveDirectory { get => saveDirectory; set { if (Set(ref saveDirectory, value)) Save(); } }

    /// <summary>바뀌면 DownloadQueue가 PropertyChanged를 보고 대기열을 다시 돌립니다.</summary>
    public int MaxConcurrentDownloads
    {
        get => maxConcurrentDownloads;
        set { if (Set(ref maxConcurrentDownloads, Math.Clamp(value, ConcurrencyRange.Min, ConcurrencyRange.Max))) Save(); }
    }

    public bool ConfirmQuitWhileDownloading { get => confirmQuitWhileDownloading; set { if (Set(ref confirmQuitWhileDownloading, value)) Save(); } }

    public bool AutoUpdateYTDLP { get => autoUpdateYTDLP; set { if (Set(ref autoUpdateYTDLP, value)) Save(); } }

    public DateTimeOffset LastYTDLPCheck { get => lastYTDLPCheck; set { if (Set(ref lastYTDLPCheck, value)) Save(); } }

    /// <summary>
    /// 다운로드 옵션. DownloadOptions는 참조 형식이라 속성을 직접 바꾸면 저장도 알림도 되지 않습니다.
    /// <see cref="UpdateOptions"/>를 쓰거나 새 값을 대입하십시오. 대입하면 같은 값이어도 저장하고 알립니다.
    /// </summary>
    public DownloadOptions Options
    {
        get => options;
        set
        {
            options = value;
            OnPropertyChanged(nameof(Options));
            Save();
        }
    }

    /// <summary>복사본을 바꿔 대입합니다. 예: <c>settings.UpdateOptions(o =&gt; o.Mode = MediaMode.Audio)</c>.</summary>
    public void UpdateOptions(Action<DownloadOptions> change)
    {
        var copy = options with { };
        change(copy);
        Options = copy;
    }

    internal static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), CoreJson.Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // 망가진 설정 파일 때문에 앱이 시작되지 않는 것보다 기본값으로 여는 편이 낫습니다.
            settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(settings.saveDirectory)) settings.saveDirectory = DefaultSaveDirectory();
        settings.path = path;
        return settings;
    }

    /// <summary>임시 파일에 쓴 뒤 바꿔치기해, 쓰는 도중 앱이 꺼져도 설정 파일이 반쯤 남지 않게 합니다.</summary>
    private void Save()
    {
        if (path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, CoreJson.Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 저장하지 못해도 이번 실행에서는 바뀐 값을 그대로 씁니다.
        }
    }

    private static string DefaultSaveDirectory()
    {
        string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return videos.Length > 0 ? videos : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
    }
}
