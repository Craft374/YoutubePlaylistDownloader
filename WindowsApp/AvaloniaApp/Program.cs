using System.Diagnostics;
using Avalonia;
using YPD.Core;

namespace YPD.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ThemeChoice.Current = ThemeChoice.FromArgs(args) ?? ThemeChoice.Load();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia 미리보기 도구도 이 메서드를 찾습니다.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}

public enum UiTheme { Suki, Semi }

/// <summary>
/// 시제품에서 고른 테마. 테마는 컨트롤 모양 전체를 바꾸므로 실행 중에 갈아 끼우지 않고, 저장한 뒤 앱을 다시 띄웁니다.
/// </summary>
public static class ThemeChoice
{
    public static UiTheme Current { get; set; } = UiTheme.Suki;

    private static string FilePath => Path.Combine(ToolManager.SupportDirectory, "prototype-theme.txt");

    public static UiTheme? FromArgs(string[] args)
    {
        int i = Array.IndexOf(args, "--theme");
        return i >= 0 && i + 1 < args.Length && Enum.TryParse<UiTheme>(args[i + 1], ignoreCase: true, out var theme) ? theme : null;
    }

    public static UiTheme Load()
    {
        try
        {
            return Enum.TryParse<UiTheme>(File.ReadAllText(FilePath).Trim(), out var theme) ? theme : UiTheme.Suki;
        }
        catch (Exception)
        {
            return UiTheme.Suki;
        }
    }

    /// <summary>테마를 저장하고 새 프로세스로 다시 띄운 뒤 지금 앱을 끝냅니다.</summary>
    public static void SwitchAndRestart(UiTheme theme)
    {
        if (theme == Current) return;
        try
        {
            Directory.CreateDirectory(ToolManager.SupportDirectory);
            File.WriteAllText(FilePath, theme.ToString());
            if (Environment.ProcessPath is not { } exe) return;
            List<string> args = ["--theme", theme.ToString()];
            // "dotnet YPDDesktop.dll"로 띄웠으면 dll 경로를 앞에 붙여야 같은 앱이 뜹니다.
            if (Path.GetFileNameWithoutExtension(exe) == "dotnet") args.Insert(0, typeof(Program).Assembly.Location);
            Process.Start(exe, args);
        }
        catch (Exception)
        {
            return; // 다시 띄우지 못하면 지금 창을 그대로 둡니다.
        }
        AppStartup.KillAll();
        Environment.Exit(0);
    }
}
