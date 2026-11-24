using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace YPD.App;

public static class Program
{
    // 프로세스가 끝날 때까지 이름 있는 뮤텍스를 붙잡아 둡니다.
    private static Mutex? instanceMutex;

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        CrashLog.Install();
        // 한 번에 하나만 실행합니다. 시작할 때 공용 임시 폴더를 지우고 자동 업데이트도 하므로 둘이 돌면 서로 망가뜨립니다.
        if (RedirectToRunningInstance()) return;
        // AppInstance는 압축을 푼 폴더(exe 경로)마다 따로라, 다른 폴더의 사본은 이 이름으로 막습니다.
        instanceMutex = new Mutex(false, @"Local\YoutubePlaylistDownloader", out bool first);
        if (!first)
        {
            Core.L.Configure(Core.AppSettings.Shared.Language);
            _ = MessageBoxW(IntPtr.Zero, Core.L.T("The app is already running from another folder. Quit it first."), Core.L.T("YouTube Playlist Downloader"), 0x40);
            return;
        }
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }

    /// <summary>이미 실행 중이면 그 창을 앞으로 가져오게 하고 true를 돌려줍니다.</summary>
    private static bool RedirectToRunningInstance()
    {
        var main = AppInstance.FindOrRegisterForKey("main");
        if (main.IsCurrent)
        {
            // 두 번째 실행이 보낸 활성화는 백그라운드 스레드로 옵니다.
            main.Activated += (_, _) => App.BringToFront();
            return false;
        }
        // 지금 앞에 있는 쪽(이 프로세스)만 다른 프로세스가 창을 앞으로 가져오도록 허락할 수 있습니다.
        AllowSetForegroundWindow(main.ProcessId);
        var args = AppInstance.GetCurrent().GetActivatedEventArgs();
        // STA 스레드를 그냥 막으면 COM 호출이 멈출 수 있어, 다른 스레드에서 보내고 COM 메시지를 처리하며 기다립니다(Microsoft 예제 방식).
        using var done = new ManualResetEvent(false);
        Task.Run(() =>
        {
            try { main.RedirectActivationToAsync(args).AsTask().Wait(); }
            catch (Exception e) { CrashLog.Write("Redirect activation", e); }
            finally { done.Set(); }
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1, [done.SafeWaitHandle.DangerousGetHandle()], out _);
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(uint flags, uint timeout, uint count, IntPtr[] handles, out uint index);
}

/// <summary>처리하지 못한 예외를 %LOCALAPPDATA%\YoutubePlaylistDownloader\log.txt에 남깁니다. 사용자가 오류를 알려 줄 때 씁니다.</summary>
public static class CrashLog
{
    private static readonly string LogPath = Path.Combine(Core.ToolManager.SupportDirectory, "log.txt");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write("Unhandled exception", e.ExceptionObject as Exception);
            // 앱이 죽어도 yt-dlp·ffmpeg가 남아 돌지 않게 합니다.
            Core.AppStartup.KillAll();
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
