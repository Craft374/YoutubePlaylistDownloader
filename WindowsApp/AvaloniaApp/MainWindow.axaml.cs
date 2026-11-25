using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

public enum Screen { NewDownload, Bulk, Queue, Settings }

/// <summary>
/// 창 하나짜리 앱입니다. 창을 닫으면 앱도 끝납니다(진행 중인 다운로드가 있으면 먼저 묻습니다).
/// 왼쪽 사이드바, 가운데 화면, 오른쪽 다운로드 옵션 패널로 이루어집니다.
/// </summary>
public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly AppSettings settings = AppSettings.Shared;
    private readonly Dictionary<Screen, Control> pages;
    private readonly Dictionary<Screen, RadioButton> navItems;
    private Screen screen = Screen.NewDownload;
    private bool reallyClosing;
    private bool askingToQuit;
    private int optionsToken;

    public MainWindow()
    {
        Instance = this;
        InitializeComponent();

        pages = new()
        {
            [Screen.NewDownload] = new NewDownloadView(),
            [Screen.Bulk] = new BulkDownloadView(),
            [Screen.Queue] = new DownloadsView(),
            [Screen.Settings] = new SettingsView(),
        };
        foreach (var page in pages.Values)
        {
            page.IsVisible = false;
            Pages.Children.Add(page);
        }
        navItems = new() { [Screen.NewDownload] = NavNew, [Screen.Bulk] = NavBulk, [Screen.Queue] = NavQueue, [Screen.Settings] = NavSettings };
        foreach (var (target, item) in navItems)
            item.IsCheckedChanged += (_, _) => { if (item.IsChecked == true) Show(target); };

        OptionsToggle.IsCheckedChanged += (_, _) => AnimateOptions(OptionsToggle.IsChecked == true);
        if (!Motion.Reduced)
            OptionsHost.Transitions = [new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = Motion.Move, Easing = Motion.Spring }];

        // 맥만 제목 표시줄을 화면 안으로 넓힙니다(신호등 버튼은 OS가 그림). Windows는 기본 제목 표시줄을 씁니다.
        bool extended = OperatingSystem.IsMacOS();
        ExtendClientAreaToDecorationsHint = extended;
        if (extended)
        {
            SidebarDrag.PointerPressed += Drag;
            Header.PointerPressed += Drag;
        }
        else
        {
            SidebarDrag.Height = 12;
        }

        UseSystemMaterial();
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        Closing += OnClosing;

        ToolManager.Shared.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(UpdateToolBanner);
        DownloadQueue.Shared.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadQueue.ActiveJobCount)) UpdateBadge(); };
        UpdateToolBanner();
        UpdateBadge();
        Show(Screen.NewDownload);
    }

    // MARK: 화면 전환

    /// <summary>다른 화면으로 갑니다(예: 추가한 뒤 다운로드 화면 보기).</summary>
    public void Navigate(Screen target) => navItems[target].IsChecked = true;

    public void ShowOptions() => OptionsToggle.IsChecked = true;

    public void ShowQueue() => Navigate(Screen.Queue);

    private void Show(Screen target)
    {
        var previous = pages[screen];
        screen = target;
        var page = pages[target];
        if (previous != page) previous.IsVisible = false;
        if (!page.IsVisible)
        {
            page.IsVisible = true;
            Motion.FadeIn(page);
        }
        PageTitle.Text = L.T(target switch
        {
            Screen.NewDownload => "New Download",
            Screen.Bulk => "Bulk Download",
            Screen.Queue => "Downloads",
            _ => "Settings",
        });
        // 설정에는 옵션 패널과 그 버튼을 두지 않습니다(Mac에서도 설정은 따로 된 창입니다).
        bool available = target != Screen.Settings;
        OptionsToggle.IsVisible = available;
        AnimateOptions(available && OptionsToggle.IsChecked == true);
    }

    /// <summary>
    /// 옵션 패널을 오른쪽에서 밀어 넣고, 닫을 때는 같은 길로 내보냅니다. 움직이는 도중 다시 누르면 지금 위치에서 방향을 바꿉니다.
    /// 다 닫힌 뒤에는 숨겨서 Tab 키가 보이지 않는 컨트롤로 가지 않게 합니다.
    /// </summary>
    private async void AnimateOptions(bool open)
    {
        int token = ++optionsToken;
        if (open)
        {
            OptionsHost.IsVisible = true;
            OptionsHost.Width = Options.Width;
            return;
        }
        if (!OptionsHost.IsVisible) return;
        OptionsHost.Width = 0;
        if (OptionsHost.Transitions != null) await Task.Delay(Motion.Move);
        if (token == optionsToken) OptionsHost.IsVisible = false;
    }

    // MARK: 창

    private void Drag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    /// <summary>
    /// 사이드바만 반투명 재질로 둡니다(구역을 나누는 무거운 재질). 화면 쪽은 단색이라 글이 늘 또렷합니다.
    /// 고대비·투명도 줄이기를 켰거나 OS가 재질을 주지 않으면 단색으로 둡니다(투명한 창에 재질이 없으면 검게 보입니다).
    /// </summary>
    private void UseSystemMaterial()
    {
        bool highContrast = Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High;
        if (SystemPreferences.ReduceTransparency || highContrast) return;
        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur];
        void Apply()
        {
            bool material = ActualTransparencyLevel == WindowTransparencyLevel.Mica || ActualTransparencyLevel == WindowTransparencyLevel.AcrylicBlur;
            if (material)
            {
                Background = Avalonia.Media.Brushes.Transparent;
                Sidebar.Bind(Border.BackgroundProperty, Sidebar.GetResourceObservable("YpdSidebarMaterial"));
            }
            else
            {
                Bind(BackgroundProperty, this.GetResourceObservable("YpdWindowBackground"));
                Sidebar.Bind(Border.BackgroundProperty, Sidebar.GetResourceObservable("YpdSidebarBackground"));
            }
        }
        PropertyChanged += (_, e) => { if (e.Property == ActualTransparencyLevelProperty) Apply(); };
        Apply();
    }

    /// <summary>Ctrl(맥은 ⌘)+1/2/3으로 화면 전환, +,로 설정.</summary>
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.KeyModifiers != command) return;
        Screen? target = e.Key switch
        {
            Key.D1 or Key.NumPad1 => Screen.NewDownload,
            Key.D2 or Key.NumPad2 => Screen.Bulk,
            Key.D3 or Key.NumPad3 => Screen.Queue,
            Key.OemComma => Screen.Settings,
            _ => null,
        };
        if (target is not { } screenTarget) return;
        e.Handled = true;
        Navigate(screenTarget);
    }

    private void UpdateToolBanner()
    {
        var tools = ToolManager.Shared;
        string? problem = tools.IsChecking ? null
            : !tools.IsReady ? L.T("yt-dlp is not installed.")
            : !tools.HasFFmpeg ? L.T("ffmpeg was not found. Merging and converting will fail.")
            : null;
        ToolBanner.IsVisible = problem != null;
        ToolBannerText.Text = problem ?? "";
    }

    private void UpdateBadge()
    {
        int count = DownloadQueue.Shared.ActiveJobCount;
        Badge.IsVisible = count > 0;
        BadgeText.Text = count.ToString();
        AutomationProperties.SetName(NavQueue, count > 0 ? $"{L.T("Downloads")} ({count})" : L.T("Downloads"));
    }

    // MARK: 종료

    /// <summary>진행 중인 다운로드가 있고 묻기로 했으면 true(맥 ⌘Q도 App이 이걸 보고 창 닫기로 돌립니다).</summary>
    public bool NeedsQuitConfirmation => !reallyClosing && AppStartup.HasActiveWork && settings.ConfirmQuitWhileDownloading;

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!NeedsQuitConfirmation) return;
        // 묻는 동안 창을 닫지 않습니다. 닫기를 여러 번 눌러도 대화 상자는 하나만 띄웁니다.
        e.Cancel = true;
        if (askingToQuit) return;
        askingToQuit = true;
        try
        {
            if (await MessageDialog.ConfirmQuitAsync(this))
            {
                reallyClosing = true;
                Close();
            }
        }
        finally
        {
            askingToQuit = false;
        }
    }
}
