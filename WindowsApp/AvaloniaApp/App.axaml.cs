using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Semi.Avalonia;
using SukiUI;
using SukiUI.Enums;
using YPD.Core;

namespace YPD.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // 컨트롤 테마를 앱 스타일보다 앞에 둬야 앱 스타일(카드, 사이드바)이 이깁니다.
        Styles.Insert(0, ThemeChoice.Current == UiTheme.Suki ? new SukiTheme { ThemeColor = SukiColor.Red } : new SemiTheme());
        ApplyAppearance();
        AppSettings.Shared.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.Appearance)) ApplyAppearance(); };
        // 대기열이 UI 스레드의 SynchronizationContext를 잡도록 창보다 먼저 부릅니다.
        AppStartup.Initialize();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => AppStartup.KillAll();
        }
        base.OnFrameworkInitializationCompleted();
        _ = StartServicesAsync();
    }

    private void ApplyAppearance() => RequestedThemeVariant = AppSettings.Shared.Appearance switch
    {
        Appearance.Light => ThemeVariant.Light,
        Appearance.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private static async Task StartServicesAsync()
    {
        try
        {
            // Core는 Windows용 도구(yt-dlp.exe 등)를 찾고 받으므로, 다른 OS에서는 확인만 하고(못 찾음) 받지 않습니다.
            if (OperatingSystem.IsWindows()) await AppStartup.RunAsync();
            else await ToolManager.Shared.RefreshAsync();
        }
        catch (Exception)
        {
            // 시제품: 도구 확인이 실패해도 화면은 그대로 봅니다.
        }
    }
}
