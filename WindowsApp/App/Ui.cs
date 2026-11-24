using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using YPD.Core;

namespace YPD.App;

/// <summary>화면 전체에서 같이 쓰는 간격·모양 값(Mac Design.swift). Windows 모양에 맞춰 모서리는 Fluent 값을 씁니다.</summary>
public static class Design
{
    public const double Spacing = 12;
    public const double CornerRadius = 8;
    public const double ThumbnailCorner = 4;
    /// <summary>본문 영역 최대 폭. 넓은 창에서도 글줄이 너무 길어지지 않게 합니다.</summary>
    public const double ContentWidth = 640;
}

/// <summary>코드로 화면을 만들 때 쓰는 작은 도우미 모음.</summary>
public static class Ui
{
    /// <summary>
    /// 테마를 따라 바뀌는 색이 필요한 스타일. 코드로 읽은 브러시는 ThemeResource가 아니라 화면 모드를 바꿔도 그대로이므로,
    /// 이 몇 개만 XAML 문자열로 읽습니다(XAML 컴파일러 없이 실행 중에 해석됩니다). App이 시작할 때 앱 리소스에 넣습니다.
    /// </summary>
    public static ResourceDictionary CreateThemeStyles() => (ResourceDictionary)XamlReader.Load("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="YpdSecondaryText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
          </Style>
          <Style x:Key="YpdCaption" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
          </Style>
          <Style x:Key="YpdCautionCaption" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{ThemeResource SystemFillColorCautionBrush}" />
          </Style>
          <Style x:Key="YpdCard" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="8" />
          </Style>
          <Style x:Key="YpdDivider" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource DividerStrokeColorDefaultBrush}" />
          </Style>
          <Style x:Key="YpdPlaceholder" TargetType="Border">
            <Setter Property="Background" Value="{ThemeResource SubtleFillColorSecondaryBrush}" />
          </Style>
          <Style x:Key="YpdTertiaryIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorTertiaryBrush}" />
          </Style>
          <Style x:Key="YpdSuccessIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource SystemFillColorSuccessBrush}" />
          </Style>
          <Style x:Key="YpdCriticalIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource SystemFillColorCriticalBrush}" />
          </Style>
          <Style x:Key="YpdCautionIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource SystemFillColorCautionBrush}" />
          </Style>
          <Style x:Key="YpdSecondaryIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
          </Style>
          <Style x:Key="YpdAccentIcon" TargetType="FontIcon">
            <Setter Property="Foreground" Value="{ThemeResource AccentTextFillColorPrimaryBrush}" />
          </Style>
          <Style x:Key="YpdAccentText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{ThemeResource AccentTextFillColorPrimaryBrush}" />
          </Style>
          <Style x:Key="YpdTertiaryText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{ThemeResource TextFillColorTertiaryBrush}" />
          </Style>
          <Style x:Key="YpdCriticalCaption" TargetType="TextBlock">
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{ThemeResource SystemFillColorCriticalBrush}" />
          </Style>
        </ResourceDictionary>
        """);

    /// <summary>앱 리소스의 스타일(위의 Ypd* 또는 BodyStrongTextBlockStyle 같은 기본 스타일).</summary>
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static readonly FontFamily Monospace = new("Cascadia Mono, Consolas");

    public static TextBlock Text(string text, string? style = null)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style != null) block.Style = Style(style);
        return block;
    }

    /// <summary>회색 작은 설명 글.</summary>
    public static TextBlock Caption(string text = "") => Text(text, "YpdCaption");

    /// <summary>구역 제목(Windows 설정 앱처럼 굵은 본문 크기).</summary>
    public static TextBlock SectionHeader(string text) =>
        new() { Text = text, Style = Style("BodyStrongTextBlockStyle"), Margin = new Thickness(0, 16, 0, 4) };

    /// <summary>카드: 배경 위에 살짝 떠 있는 면. 라이트·다크 모드에서 색이 따라 바뀝니다.</summary>
    public static Border Card(UIElement child, double padding = 16) =>
        new() { Style = Style("YpdCard"), Padding = new Thickness(padding), Child = child };

    /// <summary>1px 구분선. vertical이면 세로선입니다.</summary>
    public static Border Divider(bool vertical = false)
    {
        var line = new Border { Style = Style("YpdDivider") };
        if (vertical) line.Width = 1; else line.Height = 1;
        return line;
    }

    public static FontIcon Icon(string glyph, double size = 16, string? style = null)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = size };
        if (style != null) icon.Style = Style(style);
        return icon;
    }

    /// <summary>글자 없이 아이콘만 있는 버튼. 화면 읽기 프로그램과 툴팁에 쓸 이름을 꼭 붙입니다.</summary>
    public static Button IconButton(string glyph, string label, RoutedEventHandler click)
    {
        var button = new Button { Content = Icon(glyph), Padding = new Thickness(8, 6, 8, 6) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += click;
        return button;
    }

    public static StackPanel Row(double spacing, params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    public static StackPanel Column(double spacing, params UIElement[] children)
    {
        var column = new StackPanel { Spacing = spacing };
        foreach (var child in children) column.Children.Add(child);
        return column;
    }

    public static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 16:9 썸네일. 받는 동안에는 자리만 잡아 두고 아이콘을 보여 줍니다.
    /// BitmapImage가 같은 주소를 캐시하고, 화면 크기에 맞춰 디코딩해(DecodePixelWidth) 큰 이미지를 그대로 들고 있지 않습니다.
    /// </summary>
    public static FrameworkElement Thumbnail(Uri? url, double width, double cornerRadius = Design.ThumbnailCorner)
    {
        var grid = new Grid { Width = width, Height = width * 9 / 16 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(grid, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        grid.Children.Add(new Border
        {
            Style = Style("YpdPlaceholder"),
            CornerRadius = new CornerRadius(cornerRadius),
            Child = new FontIcon
            {
                Glyph = "\uE714",
                FontSize = Math.Max(12, width * 0.16),
                Style = Style("YpdTertiaryIcon"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        if (url != null)
        {
            // 이미지를 테두리 배경으로 칠하면 모서리가 둥글게 잘립니다. 다 받기 전에는 투명해 자리 표시가 보입니다.
            var bitmap = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = (int)Math.Ceiling(width),
                UriSource = url,
            };
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(cornerRadius),
                Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill },
            });
        }
        return grid;
    }

    // MARK: 대화 상자

    private static Task dialogQueue = Task.CompletedTask;

    /// <summary>
    /// ContentDialog는 한 번에 하나만 열 수 있어(두 번째 ShowAsync는 예외), 앞의 대화 상자가 닫힐 때까지 기다렸다가 엽니다.
    /// UI 스레드에서만 부릅니다.
    /// </summary>
    public static async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog, XamlRoot root)
    {
        dialog.XamlRoot = root;
        dialog.Style = Style("DefaultContentDialogStyle");
        // 대화 상자는 창 내용과 다른 층에 떠서 화면 모드를 물려받지 못합니다.
        if (root.Content is FrameworkElement content) dialog.RequestedTheme = content.RequestedTheme;
        var previous = dialogQueue;
        var done = new TaskCompletionSource();
        dialogQueue = done.Task;
        try
        {
            await previous;
            return await dialog.ShowAsync();
        }
        finally
        {
            done.SetResult();
        }
    }

    /// <summary>오류 알림. 글은 선택해 복사할 수 있습니다.</summary>
    public static Task ShowErrorAsync(XamlRoot root, string title, string message) =>
        ShowDialogAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = L.T("OK"),
            DefaultButton = ContentDialogButton.Close,
        }, root);

    /// <summary>오류 알림을 띄우고 기다리지 않습니다. 알림이 실패해도 앱이 끝나지 않게 예외를 삼킵니다.</summary>
    public static async void ShowError(FrameworkElement anchor, string title, string message)
    {
        try
        {
            if (anchor.XamlRoot is { } root) await ShowErrorAsync(root, title, message);
        }
        catch (Exception)
        {
            // 대화 상자를 못 띄우는 상황(창이 닫히는 중 등)은 무시합니다.
        }
    }

    // MARK: 목록

    private static DataTemplate? hostTemplate;

    /// <summary>
    /// 가상화 목록의 행을 코드로 채웁니다. 코드로는 DataTemplate을 만들 수 없어 빈 Grid 템플릿 하나만 XAML 문자열로 읽고,
    /// 컨테이너마다 행 컨트롤을 한 번 만들어 넣은 뒤 컨테이너가 다른 항목에 재사용될 때 bind로 새 항목을 넘깁니다(재활용 대기면 null).
    /// </summary>
    public static void HostRows<TRow>(ListViewBase list, Func<TRow> create, Action<TRow, object?> bind) where TRow : UIElement
    {
        hostTemplate ??= (DataTemplate)XamlReader.Load(
            """<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Grid /></DataTemplate>""");
        list.ItemTemplate = hostTemplate;
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer?.ContentTemplateRoot is not Grid host) return;
            if (host.Children.Count == 0)
            {
                host.Children.Add(create());
                args.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            }
            bind((TRow)host.Children[0], args.InRecycleQueue ? null : args.Item);
            args.Handled = true;
        };
    }

    // MARK: 클립보드·파일

    public static void CopyText(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // 다른 앱이 클립보드를 잡고 있으면 실패합니다. 다시 누르면 됩니다.
        }
    }

    /// <summary>글을 .txt 파일로 저장합니다. 이름은 "Links not added.txt"처럼 확장자까지 받습니다.</summary>
    public static async Task SaveTextAsync(FrameworkElement anchor, string suggestedName, string text, string? folder = null)
    {
        try
        {
            if (MainWindow.Instance is not { } window) return;
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(window.AppWindow.Id)
            {
                SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
                DefaultFileExtension = ".txt",
            };
            picker.FileTypeChoices.Add(L.T("Text document"), [".txt"]);
            if (folder != null && Directory.Exists(folder)) picker.SuggestedFolder = folder;
            var result = await picker.PickSaveFileAsync();
            if (result?.Path is { Length: > 0 } path) await File.WriteAllTextAsync(path, text);
        }
        catch (Exception e)
        {
            ShowError(anchor, L.T("Couldn’t save the file"), e.Message);
        }
    }

    // MARK: 저장 폴더

    /// <summary>저장 폴더를 고르는 창을 엽니다. 패키지 없는 앱에서도 되는 Windows App SDK 선택기를 씁니다.</summary>
    public static async Task ChooseSaveFolderAsync()
    {
        if (MainWindow.Instance is not { } window) return;
        var settings = AppSettings.Shared;
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
        {
            CommitButtonText = L.T("Choose"),
            SuggestedFolder = settings.SaveDirectory,
        };
        var result = await picker.PickSingleFolderAsync();
        if (result?.Path is { Length: > 0 } path) settings.SaveDirectory = path;
    }

    /// <summary>"선택…"과 탐색기 열기 버튼. 설정 화면과 옵션 패널에서 같이 씁니다.</summary>
    public static StackPanel SaveFolderButtons()
    {
        var choose = new Button { Content = L.T("Choose…") };
        choose.Click += async (_, _) =>
        {
            // 처리하지 못한 예외는 앱을 끝내고 다운로드까지 멈추므로 여기서 받아 알립니다.
            try { await ChooseSaveFolderAsync(); }
            catch (Exception e) when (choose.XamlRoot != null) { await ShowErrorAsync(choose.XamlRoot, L.T("Save to"), e.Message); }
        };
        var reveal = IconButton("\uE838", L.T("Show in File Explorer"), (_, _) => Explorer.OpenFolder(AppSettings.Shared.SaveDirectory));
        return Row(8, choose, reveal);
    }

    /// <summary>폴더 이름("Videos"). 드라이브 루트면 경로 그대로입니다.</summary>
    public static string FolderDisplayName(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 ? name : path;
    }
}

/// <summary>옵션 패널의 저장 폴더 줄: 폴더 아이콘, 이름, 경로, 선택·열기 버튼. 쓰는 곳마다 새로 만듭니다(요소는 부모가 하나뿐).</summary>
public sealed partial class SaveFolderRow : UserControl
{
    private readonly TextBlock name = new() { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock path = new() { Style = Ui.Style("YpdCaption"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel labels;

    public SaveFolderRow()
    {
        labels = Ui.Column(2, name, path);
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var icon = Ui.Icon("\uE8B7", 20);
        icon.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(labels, 1);
        var buttons = Ui.SaveFolderButtons();
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(icon);
        grid.Children.Add(labels);
        grid.Children.Add(buttons);
        Content = grid;

        Loaded += (_, _) =>
        {
            AppSettings.Shared.PropertyChanged -= OnSettingsChanged;
            AppSettings.Shared.PropertyChanged += OnSettingsChanged;
            Update();
        };
        Unloaded += (_, _) => AppSettings.Shared.PropertyChanged -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.SaveDirectory)) Update();
    }

    private void Update()
    {
        string dir = AppSettings.Shared.SaveDirectory;
        name.Text = Ui.FolderDisplayName(dir);
        path.Text = dir;
        ToolTipService.SetToolTip(labels, dir);
    }
}
