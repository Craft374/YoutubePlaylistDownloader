using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using YPD.Core;

namespace YPD.Desktop;

public static class Program
{
    // 프로세스가 끝날 때까지 이름 있는 뮤텍스를 붙잡아 둡니다.
    private static Mutex? instanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        CrashLog.Install();
        // 한 번에 하나만 실행합니다. 시작할 때 공용 임시 폴더를 지우고 자동 업데이트도 하므로 둘이 돌면 서로 망가뜨립니다.
        // 맥은 LaunchServices가 .app을 하나만 띄웁니다.
        if (OperatingSystem.IsWindows() && !TakeInstanceLock()) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia 미리보기 도구도 이 메서드를 찾습니다.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    /// <summary>이미 떠 있으면 그 창을 앞으로 가져오고 false를 돌려줍니다(창을 못 찾으면 알림을 띄웁니다).</summary>
    private static bool TakeInstanceLock()
    {
        instanceMutex = new Mutex(false, @"Local\YoutubePlaylistDownloader", out bool first);
        if (first) return true;
        // 창 제목은 언어와 상관없이 같습니다. 방금 실행한 쪽(앞에 있는 프로세스)이라 창을 앞으로 가져올 수 있습니다.
        IntPtr window = FindWindowW(null, "YouTube Playlist Downloader");
        if (window != IntPtr.Zero)
        {
            if (IsIconic(window)) ShowWindow(window, 9); // SW_RESTORE
            SetForegroundWindow(window);
            return false;
        }
        L.Configure(AppSettings.Shared.Language);
        _ = MessageBoxW(IntPtr.Zero, L.T("The app is already running from another folder. Quit it first."), L.T("YouTube Playlist Downloader"), 0x40);
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}

/// <summary>처리하지 못한 예외를 SupportDirectory의 log.txt에 남깁니다. 사용자가 오류를 알려 줄 때 씁니다.</summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(ToolManager.SupportDirectory, "log.txt");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write("Unhandled exception", e.ExceptionObject as Exception);
            // 앱이 죽어도 yt-dlp·ffmpeg가 남아 돌지 않게 합니다.
            AppStartup.KillAll();
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Unobserved task exception", e.Exception);
    }

    public static void Write(string context, Exception? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            // 끝없이 커지지 않게 1 MB를 넘으면 새로 씁니다.
            bool append = !File.Exists(LogPath) || new FileInfo(LogPath).Length < 1024 * 1024;
            string text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context} (v{Version})\n{error}\n\n";
            if (append) File.AppendAllText(LogPath, text); else File.WriteAllText(LogPath, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(e);
        }
    }

    private static string? Version => typeof(CrashLog).Assembly.GetName().Version?.ToString();
}
