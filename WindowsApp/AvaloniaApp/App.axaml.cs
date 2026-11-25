using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        ApplyAppearance();
        AppSettings.Shared.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.Appearance)) ApplyAppearance(); };
        // 언어를 정하고 대기열이 UI 스레드의 SynchronizationContext를 잡도록 창보다 먼저 부릅니다.
        AppStartup.Initialize();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI exception", e.Exception);
            AppStartup.KillAll();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // 어떻게 끝나든 yt-dlp·ffmpeg를 남겨 두지 않습니다.
            desktop.Exit += (_, _) => AppStartup.KillAll();
            // 맥의 ⌘Q(앱 메뉴의 종료)는 창 닫기가 아니라 여기로 옵니다. 창 닫기와 같은 확인을 거칩니다.
            desktop.ShutdownRequested += (_, e) =>
            {
                if (!window.NeedsQuitConfirmation) return;
                e.Cancel = true;
                window.Close();
            };
            AddMacMenu(window);
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

    /// <summary>맥 앱 메뉴에 "설정…"(⌘,)을 넣습니다. 메뉴를 직접 정하면 Avalonia가 넣는 "About Avalonia"도 빠집니다.</summary>
    private void AddMacMenu(MainWindow window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var settings = new NativeMenuItem(L.T("Settings") + "…") { Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) };
        settings.Click += (_, _) => window.Navigate(Screen.Settings);
        var menu = new NativeMenu();
        menu.Add(settings);
        NativeMenu.SetMenu(this, menu);
    }

    /// <summary>정리, 도구 확인, 대기열 시작, 자동 업데이트. 시작을 막지 않습니다.</summary>
    private static async Task StartServicesAsync()
    {
        try
        {
            await AppStartup.RunAsync();
        }
        catch (Exception e)
        {
            CrashLog.Write("Startup", e);
        }
    }
}
