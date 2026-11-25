using System.Diagnostics;

namespace YPD.Core;

/// <summary>파일 탐색기(맥은 Finder) 열기. 둘 다 아니면(테스트) 아무것도 하지 않습니다.</summary>
public static class Explorer
{
    /// <summary>"파일 탐색기에서 보기" 또는 "Finder에서 보기".</summary>
    public static string ShowLabel => L.T(OperatingSystem.IsMacOS() ? "Show in Finder" : "Show in File Explorer");

    /// <summary>폴더를 엽니다. 셸로 폴더 경로를 열어 explorer.exe의 인자 해석(쉼표 등)을 거치지 않습니다.</summary>
    public static void OpenFolder(string directory)
    {
        // 셸 실행은 파일도 실행하므로 폴더일 때만 엽니다.
        if (!Directory.Exists(directory)) return;
        Launch(OperatingSystem.IsMacOS() ? Open(directory) : new ProcessStartInfo(directory) { UseShellExecute = true });
    }

    /// <summary>파일이 든 폴더를 열고 그 파일을 선택합니다. Windows 경로에는 "가 들어갈 수 없어 따옴표로 감싸기만 하면 됩니다.</summary>
    public static void RevealFile(string file) => Launch(OperatingSystem.IsMacOS()
        ? Open("-R", file)
        : new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{file}\"", UseShellExecute = false });

    /// <summary>경로는 늘 절대 경로("/…")라 open이 옵션으로 읽지 않습니다.</summary>
    private static ProcessStartInfo Open(params string[] args)
    {
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    private static void Launch(ProcessStartInfo info)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) return;
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            // 폴더가 지워졌거나 열 수 없으면 조용히 넘어갑니다.
        }
    }
}
