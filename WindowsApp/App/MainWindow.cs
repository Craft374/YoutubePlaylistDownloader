using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using YPD.Core;

namespace YPD.App;

public enum Screen { NewDownload, Bulk, Queue, Settings }

/// <summary>
/// 창 하나짜리 앱입니다. 창을 닫으면 앱도 끝납니다(진행 중인 다운로드가 있으면 먼저 묻습니다).
/// 왼쪽 탐색 창, 가운데 화면, 오른쪽 다운로드 옵션 패널(300px)로 이루어집니다.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private readonly AppSettings settings = AppSettings.Shared;
    private readonly IntPtr hwnd;
    private readonly Grid root = new();
    private readonly NavigationView nav = new();
    private readonly NavigationViewItem downloadsItem;
    private readonly InfoBadge activeBadge = new();
    private readonly TextBlock pageTitle = new() { Style = Ui.Style("TitleTextBlockStyle") };
    private readonly ToggleButton optionsButton = new();
    private readonly ContentControl pageHost = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };
    private readonly Border optionsDivider = Ui.Divider(vertical: true);
    private readonly OptionsPanel options = new() { Width = 300 };
    private readonly Dictionary<Screen, UIElement> pages;
    private Screen screen = Screen.NewDownload;
    private bool showOptions;
    private bool reallyClosing;
    private bool askingToQuit;

    public MainWindow()
    {
        Instance = this;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = L.T("YouTube Playlist Downloader");
        SystemBackdrop = new MicaBackdrop();

        // 화면을 한 번씩만 만들어 두고 바꿔 끼웁니다. 화면을 오가도 입력한 내용이 남습니다.
        // (Frame.Navigate는 XAML 형식 정보로 화면을 만드는데, 코드로 만든 화면은 그 정보가 없습니다.)
        pages = new()
        {
            [Screen.NewDownload] = new NewDownloadPage(),
            [Screen.Bulk] = new BulkDownloadPage(),
            [Screen.Queue] = new DownloadsPage(),
            [Screen.Settings] = new SettingsPage(),
        };

        downloadsItem = NavItem(Screen.Queue, "Downloads", "\uE896");
        downloadsItem.InfoBadge = activeBadge;
        nav.MenuItems.Add(NavItem(Screen.NewDownload, "New Download", "\uE71B"));
        nav.MenuItems.Add(NavItem(Screen.Bulk, "Bulk Download", "\uE8FD"));
        nav.MenuItems.Add(downloadsItem);
        nav.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        nav.IsSettingsVisible = true;
        // 펼친 탐색 창(320) + 화면 420 + 옵션 301이 다 들어갈 때만 펼칩니다. 기본값 1008이면 옵션 패널 오른쪽이 잘립니다.
        nav.ExpandedModeThresholdWidth = 1060;
        nav.Content = Body();
        // 배너는 스스로 숨고 나타나므로, 접힌 탐색 창(아이콘만)에서는 감싼 테두리를 숨깁니다.
        var footer = new Border { Child = new ToolStatusBanner { Margin = new Thickness(8) } };
        nav.PaneFooter = footer;
        nav.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) =>
        {
            footer.Visibility = Ui.Visible(nav.IsPaneOpen);
            // NavigationView가 이때 설정 항목 도움말을 OS 언어로 다시 넣으므로, 그 뒤에 앱 언어로 덮어씁니다.
            DispatcherQueue.TryEnqueue(LocalizeSettingsItem);
        });
        nav.SelectionChanged += (_, e) =>
        {
            if (e.IsSettingsSelected) Show(Screen.Settings);
            else if (e.SelectedItem is NavigationViewItem { Tag: string tag }) Show(Enum.Parse<Screen>(tag));
        };
        // 기본 설정 항목의 글은 OS 언어를 따르므로 앱 언어로 바꿉니다. 탐색 창이 뜬 뒤에야 만들어집니다.
        nav.Loaded += (_, _) => LocalizeSettingsItem();

        var titleBar = new TitleBar { Title = Title, IsBackButtonVisible = false, IsPaneToggleButtonVisible = false };
        Grid.SetRow(nav, 1);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(titleBar);
        root.Children.Add(nav);
        AddShortcuts();
        Content = root;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
            titleBar.IconSource = new ImageIconSource { ImageSource = new BitmapImage(new Uri(icon)) };
        }
        SetInitialSize();

        ApplyTheme();
        UpdateBadge();
        settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.Appearance)) ApplyTheme(); };
        DownloadQueue.Shared.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DownloadQueue.ActiveJobCount)) UpdateBadge(); };

        AppWindow.Closing += OnClosing;
        // 어떻게 끝나든(확인 없이 닫기 포함) yt-dlp·ffmpeg를 남겨 두지 않습니다.
        Closed += (_, _) => AppStartup.KillAll();

        Show(Screen.NewDownload);
        nav.SelectedItem = nav.MenuItems[0];
    }

    // MARK: 화면 전환

    /// <summary>다른 화면으로 갑니다(예: 추가한 뒤 다운로드 화면 보기).</summary>
    public void Navigate(Screen target)
    {
        nav.SelectedItem = target == Screen.Settings
            ? nav.SettingsItem
            : nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == target.ToString());
    }

    /// <summary>오른쪽 다운로드 옵션 패널을 열거나 닫습니다(머리글 버튼, New Download의 "옵션…" 버튼).</summary>
    public void SetOptionsVisible(bool visible)
    {
        showOptions = visible;
        UpdateOptionsPanel();
    }

    public void ToggleOptions() => SetOptionsVisible(!showOptions);

    private void Show(Screen target)
    {
        screen = target;
        pageHost.Content = pages[target];
        pageTitle.Text = L.T(target switch
        {
            Screen.NewDownload => "New Download",
            Screen.Bulk => "Bulk Download",
            Screen.Queue => "Downloads",
            _ => "Settings",
        });
        UpdateOptionsPanel();
    }

    /// <summary>설정 화면에는 옵션 패널과 그 버튼을 두지 않습니다(Mac에서도 설정은 따로 된 창입니다).</summary>
    private void UpdateOptionsPanel()
    {
        bool available = screen != Screen.Settings;
        optionsButton.Visibility = Ui.Visible(available);
        optionsButton.IsChecked = showOptions;
        options.Visibility = optionsDivider.Visibility = Ui.Visible(available && showOptions);
    }

    /// <summary>머리글(화면 제목 + 옵션 버튼)과 그 아래 화면 | 구분선 | 옵션 패널.</summary>
    private Grid Body()
    {
        optionsButton.Content = Ui.Row(8, Ui.Icon("\uE9E9"), new TextBlock { Text = L.T("Download Options") });
        ToolTipService.SetToolTip(optionsButton, L.T("Show or hide download options"));
        optionsButton.Click += (_, _) => ToggleOptions();
        optionsButton.HorizontalAlignment = HorizontalAlignment.Right;
        optionsButton.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid { Padding = new Thickness(24, 12, 16, 12) };
        header.Children.Add(pageTitle);
        header.Children.Add(optionsButton);

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // 화면 최소 폭 420 + 옵션 300(Mac과 같음).
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 420 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumnSpan(header, 3);
        Grid.SetRow(pageHost, 1);
        Grid.SetRow(optionsDivider, 1);
        Grid.SetColumn(optionsDivider, 1);
        Grid.SetRow(options, 1);
        Grid.SetColumn(options, 2);
        body.Children.Add(header);
        body.Children.Add(pageHost);
        body.Children.Add(optionsDivider);
        body.Children.Add(options);
        return body;
    }

    /// <summary>기본 설정 항목의 글·화면 읽기 이름·도움말은 OS 언어를 따르므로 앱 언어로 바꿉니다.</summary>
    private void LocalizeSettingsItem()
    {
        if (nav.SettingsItem is not NavigationViewItem item) return;
        string title = L.T("Settings");
        item.Content = title;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, title);
        ToolTipService.SetToolTip(item, nav.IsPaneOpen ? null : title);
    }

    private static NavigationViewItem NavItem(Screen target, string title, string glyph) =>
        // Tag는 WinRT를 거쳐 오가므로 C# 열거형 대신 문자열을 넣습니다.
        new() { Content = L.T(title), Icon = new FontIcon { Glyph = glyph }, Tag = target.ToString() };

    private void UpdateBadge()
    {
        int count = DownloadQueue.Shared.ActiveJobCount;
        activeBadge.Value = count;
        activeBadge.Visibility = Ui.Visible(count > 0);
    }

    /// <summary>Ctrl+1/2/3으로 화면 전환, Ctrl+,로 설정(Mac의 ⌘1/2/3, ⌘,).</summary>
    private void AddShortcuts()
    {
        void Add(VirtualKey key, Screen target)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, e) => { Navigate(target); e.Handled = true; };
            root.KeyboardAccelerators.Add(accelerator);
        }
        Add(VirtualKey.Number1, Screen.NewDownload);
        Add(VirtualKey.Number2, Screen.Bulk);
        Add(VirtualKey.Number3, Screen.Queue);
        Add((VirtualKey)188, Screen.Settings); // VK_OEM_COMMA
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    // MARK: 창

    /// <summary>1080×700(DIP)으로 화면 가운데에 엽니다. AppWindow 크기는 실제 픽셀이라 DPI 배율을 곱합니다.</summary>
    private void SetInitialSize()
    {
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var size = new SizeInt32((int)(1080 * scale), (int)(700 * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size.Width = Math.Min(size.Width, area.Width);
        size.Height = Math.Min(size.Height, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // 접힌 탐색 창 + 화면 420 + 옵션 300이 들어가는 크기입니다.
            // shortcut: 처음 연 모니터의 배율로 픽셀을 계산합니다. 배율이 다른 모니터로 옮기면 최소 크기가 조금 달라집니다.
            presenter.PreferredMinimumWidth = (int)(800 * scale);
            presenter.PreferredMinimumHeight = (int)(520 * scale);
        }
    }

    /// <summary>화면 모드를 바로 적용합니다. 최소화·닫기 버튼 색도 따라가게 합니다.</summary>
    private void ApplyTheme()
    {
        (root.RequestedTheme, AppWindow.TitleBar.PreferredTheme) = settings.Appearance switch
        {
            Appearance.Light => (ElementTheme.Light, TitleBarTheme.Light),
            Appearance.Dark => (ElementTheme.Dark, TitleBarTheme.Dark),
            _ => (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode),
        };
    }

    /// <summary>두 번째 실행이 왔을 때: 최소화돼 있으면 되살리고 앞으로 가져옵니다.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        SetForegroundWindow(hwnd);
    }

    // MARK: 종료

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (reallyClosing || !AppStartup.HasActiveWork || !settings.ConfirmQuitWhileDownloading) return;
        // 묻는 동안 창을 닫지 않습니다. 닫기를 여러 번 눌러도 대화 상자는 하나만 띄웁니다.
        args.Cancel = true;
        if (askingToQuit) return;
        askingToQuit = true;
        try
        {
            if (await ConfirmQuitAsync())
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

    private async Task<bool> ConfirmQuitAsync()
    {
        var dontAsk = new CheckBox { Content = L.T("Don’t ask again") };
        var dialog = new ContentDialog
        {
            Title = L.T("Downloads are in progress"),
            Content = Ui.Column(Design.Spacing, Ui.Text(L.T("Quitting will stop all downloads. Unfinished files will not be saved.")), dontAsk),
            PrimaryButtonText = L.T("Quit"),
            CloseButtonText = L.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await Ui.ShowDialogAsync(dialog, root.XamlRoot);
        // Mac처럼 어느 버튼을 눌러도 "다시 묻지 않기"는 적용합니다.
        if (dontAsk.IsChecked == true) settings.ConfirmQuitWhileDownloading = false;
        return result == ContentDialogResult.Primary;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
