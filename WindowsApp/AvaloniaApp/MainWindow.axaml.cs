using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly NewDownloadView newDownload = new();

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        // 화면 이동
        NavNew.IsCheckedChanged += (_, _) => { if (NavNew.IsChecked == true) PageHost.Content = newDownload; };
        NavBulk.IsCheckedChanged += (_, _) => { if (NavBulk.IsChecked == true) PageHost.Content = Placeholder(L.T("Bulk Download")); };
        NavQueue.IsCheckedChanged += (_, _) => { if (NavQueue.IsChecked == true) PageHost.Content = Placeholder(L.T("Downloads")); };
        NavSettings.IsCheckedChanged += (_, _) => { if (NavSettings.IsChecked == true) PageHost.Content = Placeholder(L.T("Settings")); };
        PageHost.Content = newDownload;

        // 옵션 패널
        OptionsToggle.IsCheckedChanged += (_, _) => Options.IsVisible = OptionsToggle.IsChecked == true;
        // 맥만 제목 표시줄을 화면 안으로 넓힙니다(신호등 버튼은 OS가 그림). Windows에서 넓히면 테마가 창 버튼을 그려야 하는데,
        // SukiUI는 SukiWindow가 아닌 창에는 최소화·닫기 버튼을 그리지 않고 Semi는 제목 글을 사이드바 위에 그립니다. 그래서 Windows는 기본 제목 표시줄을 씁니다.
        bool extended = OperatingSystem.IsMacOS();
        ExtendClientAreaToDecorationsHint = extended;
        if (!extended) SidebarDrag.Height = 8;
        Options.SetTopInset(extended ? 44 : 8);

        // 제목 표시줄을 없앴으므로 빈 위쪽을 끌어 창을 옮깁니다.
        SidebarDrag.PointerPressed += Drag;
        TopDrag.PointerPressed += Drag;

        // 테마(시제품 전용): 고르면 다시 띄웁니다.
        ThemeLabel.Text = L.IsKorean ? "컨트롤 테마 (시제품)" : "Control theme (prototype)";
        (ThemeChoice.Current == UiTheme.Suki ? SukiButton : SemiButton).IsChecked = true;
        SukiButton.IsCheckedChanged += (_, _) => { if (SukiButton.IsChecked == true) ThemeChoice.SwitchAndRestart(UiTheme.Suki); };
        SemiButton.IsCheckedChanged += (_, _) => { if (SemiButton.IsChecked == true) ThemeChoice.SwitchAndRestart(UiTheme.Semi); };

        // 화면 모드
        var settings = AppSettings.Shared;
        (settings.Appearance switch { Appearance.Light => LightMode, Appearance.Dark => DarkMode, _ => SystemMode }).IsChecked = true;
        SystemMode.IsCheckedChanged += (_, _) => { if (SystemMode.IsChecked == true) settings.Appearance = Appearance.System; };
        LightMode.IsCheckedChanged += (_, _) => { if (LightMode.IsChecked == true) settings.Appearance = Appearance.Light; };
        DarkMode.IsCheckedChanged += (_, _) => { if (DarkMode.IsChecked == true) settings.Appearance = Appearance.Dark; };

        ToolManager.Shared.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(UpdateToolBanner);
        UpdateToolBanner();
    }

    public void ShowOptions() => OptionsToggle.IsChecked = true;

    public void ShowQueue() => NavQueue.IsChecked = true;

    private void Drag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void UpdateToolBanner()
    {
        var tools = ToolManager.Shared;
        string? problem = tools.IsChecking ? null
            : !tools.IsReady ? L.T("yt-dlp is not installed.")
            : !tools.HasFFmpeg ? L.T("ffmpeg was not found. Merging and converting will fail.")
            : null;
        ToolBanner.IsVisible = problem != null;
        ToolBannerTitle.Text = L.IsKorean ? "구성 요소 확인" : "Components";
        ToolBannerText.Text = problem ?? "";
    }

    /// <summary>시제품에 아직 없는 화면.</summary>
    private static Control Placeholder(string title) => new StackPanel
    {
        Spacing = 8,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = title, Classes = { "hero" } },
            new TextBlock
            {
                Text = L.IsKorean ? "시제품에는 새 다운로드 화면과 옵션 패널만 있습니다" : "The prototype only has New Download and the options panel",
                Classes = { "secondary" },
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        },
    };
}
