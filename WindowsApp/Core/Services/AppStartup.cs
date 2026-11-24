namespace YPD.Core;

/// <summary>Mac AppDelegate의 시작·종료 처리.</summary>
public static class AppStartup
{
    /// <summary>
    /// UI 스레드에서 SynchronizationContext를 설정한 뒤, 창을 만들기 전에 부릅니다.
    /// 화면 언어를 정하고, 대기열이 UI 스레드의 SynchronizationContext를 잡도록 싱글턴을 여기서 먼저 만듭니다.
    /// </summary>
    public static void Initialize()
    {
        L.Configure(AppSettings.Shared.Language);
        _ = ToolManager.Shared;
        _ = DownloadQueue.Shared;
    }

    /// <summary>
    /// 창을 띄운 뒤 UI 스레드에서 부르고 기다리지 않아도 됩니다(시작을 막지 않음).
    /// 정리를 먼저 끝내야 방금 시작한 다운로드·업데이트의 임시 폴더를 지우지 않습니다(다운로드는 도구 확인 뒤에만 시작됩니다).
    /// </summary>
    public static async Task RunAsync()
    {
        await Task.Run(() =>
        {
            DownloadQueue.DeleteDirectory(DownloadQueue.TempRoot);
            ToolManager.RemoveOldManagedVersions();
        });
        await ToolManager.Shared.RefreshAsync();
        DownloadQueue.Shared.Pump();
        await ToolManager.Shared.AutoUpdateIfNeededAsync(AppSettings.Shared);
    }

    /// <summary>종료 확인을 물을지 판단합니다(설정 ConfirmQuitWhileDownloading과 함께).</summary>
    public static bool HasActiveWork => DownloadQueue.Shared.HasActiveWork;

    /// <summary>종료 직전에 부릅니다. 자식 프로세스 트리를 끝내고 기다리지 않습니다.</summary>
    public static void KillAll() => ProcessRegistry.KillAll();
}
