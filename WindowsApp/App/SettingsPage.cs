using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YPD.Core;

namespace YPD.App;

/// <summary>설정 화면(Mac SettingsView의 일반·구성 요소 탭을 한 화면에). Windows 설정 앱처럼 카드 줄로 보여 줍니다.</summary>
public sealed partial class SettingsPage : UserControl
{
    private readonly AppSettings settings = AppSettings.Shared;
    private readonly ToolManager tools = ToolManager.Shared;
    private bool updating;

    private readonly ComboBox appearance = new();
    private readonly ComboBox language = new();
    private readonly TextBlock saveDirectory = RowDescription();
    private readonly NumberBox concurrency = new()
    {
        Minimum = AppSettings.ConcurrencyRange.Min,
        Maximum = AppSettings.ConcurrencyRange.Max,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
    };
    private readonly ToggleSwitch confirmQuit = Switch();
    private readonly ComponentRow ytdlp = new("yt-dlp");
    private readonly ComponentRow ffmpeg = new("ffmpeg");
    private readonly ComponentRow jsRuntime = new(L.T("JavaScript runtime"));
    private readonly StackPanel installHint;
    private readonly TextBlock installFFmpeg = Command("winget install Gyan.FFmpeg");
    private readonly TextBlock installDeno = Command("winget install DenoLand.Deno");
    private readonly Button checkAgain = new() { Content = L.T("Check Again"), HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button update = new();
    private readonly ProgressRing updateProgress = new() { IsActive = true, Width = 16, Height = 16 };
    private readonly TextBlock updateStatus = RowDescription();
    private readonly ToggleSwitch autoUpdate = Switch();

    public SettingsPage()
    {
        foreach (string name in new[] { "System", "Light", "Dark" }) appearance.Items.Add(L.T(name));
        language.Items.Add(L.T("System"));
        language.Items.Add("English");
        language.Items.Add("한국어");
        updateStatus.IsTextSelectionEnabled = true;
        // winget으로 설치한 도구는 PATH가 바뀌므로, 이미 떠 있는 앱은 다시 열어야 찾습니다.
        installHint = Ui.Column(4, Ui.Caption(L.T("Install missing components with winget:")), installFFmpeg, installDeno,
                                Ui.Caption(L.T("Restart the app after installing them.")));

        var dataFolder = new Button { Content = L.T("Open") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dataFolder, L.T("Open Data Folder"));
        dataFolder.Click += (_, _) =>
        {
            Directory.CreateDirectory(ToolManager.SupportDirectory);
            Explorer.OpenFolder(ToolManager.SupportDirectory);
        };
        var dataDescription = RowDescription();
        dataDescription.Text = ToolManager.SupportDirectory;

        var footer = new Grid { ColumnSpacing = Design.Spacing, Margin = new Thickness(0, 4, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(checkAgain, 1);
        checkAgain.VerticalAlignment = VerticalAlignment.Top;
        footer.Children.Add(installHint);
        footer.Children.Add(checkAgain);

        var stack = Ui.Column(4,
            Ui.SectionHeader(L.T("General")),
            SettingsRow(L.T("Appearance"), null, appearance),
            SettingsRow(L.T("Language"), Description(L.T("Takes effect the next time you open the app")), language),
            SettingsRow(L.T("Save to"), saveDirectory, Ui.SaveFolderButtons()),
            SettingsRow(L.T("Simultaneous downloads"), null, concurrency),
            SettingsRow(L.T("Ask before quitting while downloading"), null, confirmQuit),
            Ui.SectionHeader(L.T("Components")),
            ytdlp, ffmpeg, jsRuntime, footer,
            Ui.SectionHeader(L.T("Updates")),
            SettingsRow(L.T("yt-dlp"), updateStatus, Ui.Row(8, updateProgress, update)),
            SettingsRow(L.T("Keep yt-dlp up to date automatically"), null, autoUpdate),
            SettingsRow(L.T("Data folder"), dataDescription, dataFolder));
        stack.MaxWidth = 1000;
        stack.Padding = new Thickness(0, 0, 0, 24);
        Content = new ScrollViewer { Content = stack, Padding = new Thickness(24, 0, 24, 0) };

        appearance.SelectionChanged += (_, _) => { if (!updating) settings.Appearance = (Appearance)appearance.SelectedIndex; };
        language.SelectionChanged += (_, _) => { if (!updating) settings.Language = (AppLanguage)language.SelectedIndex; };
        concurrency.ValueChanged += (_, e) =>
        {
            if (updating) return;
            if (double.IsFinite(e.NewValue)) settings.MaxConcurrentDownloads = (int)e.NewValue;
            UpdateSettings();
        };
        confirmQuit.Toggled += (_, _) => { if (!updating) settings.ConfirmQuitWhileDownloading = confirmQuit.IsOn; };
        autoUpdate.Toggled += (_, _) => { if (!updating) settings.AutoUpdateYTDLP = autoUpdate.IsOn; };
        update.Click += async (_, _) => await tools.UpdateYTDLPAsync(settings);
        checkAgain.Click += async (_, _) => await tools.RefreshAsync();

        // 화면을 바꿔 끼울 때마다 Loaded가 다시 오므로(두 번 올 때도 있음) 먼저 빼고 다시 붙입니다.
        Loaded += (_, _) =>
        {
            settings.PropertyChanged -= OnChanged;
            tools.PropertyChanged -= OnChanged;
            settings.PropertyChanged += OnChanged;
            tools.PropertyChanged += OnChanged;
            UpdateSettings();
            UpdateTools();
        };
        Unloaded += (_, _) =>
        {
            settings.PropertyChanged -= OnChanged;
            tools.PropertyChanged -= OnChanged;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == tools) UpdateTools();
        else if (e.PropertyName != nameof(AppSettings.Options)) UpdateSettings();
    }

    private void UpdateSettings()
    {
        updating = true;
        try
        {
            appearance.SelectedIndex = (int)settings.Appearance;
            language.SelectedIndex = (int)settings.Language;
            saveDirectory.Text = settings.SaveDirectory;
            concurrency.Value = settings.MaxConcurrentDownloads;
            confirmQuit.IsOn = settings.ConfirmQuitWhileDownloading;
            autoUpdate.IsOn = settings.AutoUpdateYTDLP;
        }
        finally
        {
            updating = false;
        }
    }

    private void UpdateTools()
    {
        bool checking = tools.IsChecking;
        var paths = tools.Paths;
        ytdlp.Update(tools.YtdlpVersion ?? L.T(tools.IsReady ? "Installed" : "Not installed"), paths?.Ytdlp,
                     tools.IsReady ? ComponentState.Ok : ComponentState.Missing, checking);
        ffmpeg.Update(L.T(tools.HasFFmpeg ? "Installed" : "Not found"), paths?.Ffmpeg,
                      tools.HasFFmpeg ? ComponentState.Ok : ComponentState.Missing, checking);
        jsRuntime.Update(tools.JsRuntimeName ?? L.T("Not found"), paths?.JsRuntime?.Path,
                         tools.JsRuntimeName != null ? ComponentState.Ok : ComponentState.Warning, checking,
                         tools.JsRuntimeName == null ? L.T("Needed for some YouTube formats") : null);
        installHint.Visibility = Ui.Visible(!checking && (!tools.HasFFmpeg || tools.JsRuntimeName == null));
        installFFmpeg.Visibility = Ui.Visible(!tools.HasFFmpeg);
        installDeno.Visibility = Ui.Visible(tools.JsRuntimeName == null);
        checkAgain.IsEnabled = !checking;
        update.Content = L.T(tools.IsReady ? "Update yt-dlp" : "Install yt-dlp");
        update.IsEnabled = !tools.IsUpdating;
        updateProgress.Visibility = Ui.Visible(tools.IsUpdating);
        updateStatus.Text = tools.UpdateStatus ?? "";
        updateStatus.Visibility = Ui.Visible(tools.UpdateStatus != null);
    }

    /// <summary>Windows 설정 앱의 한 줄: 왼쪽에 제목과 설명, 오른쪽에 컨트롤.</summary>
    internal static Border SettingsRow(string title, UIElement? description, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = Design.Spacing, MinHeight = 36 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = Ui.Column(2, Ui.Text(title));
        if (description != null) labels.Children.Add(description);
        labels.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        // 제목이 따로 된 글이라 화면 읽기 프로그램이 컨트롤 이름을 모릅니다. 글이 이름이 되는 버튼과 묶음 칸은 건너뜁니다.
        if (control is Control and not Button) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
        Grid.SetColumn(control, 1);
        grid.Children.Add(labels);
        grid.Children.Add(control);
        var card = Ui.Card(grid);
        card.CornerRadius = new CornerRadius(4);
        card.Padding = new Thickness(16, 12, 16, 12);
        return card;
    }

    private static TextBlock Description(string text)
    {
        var block = RowDescription();
        block.Text = text;
        return block;
    }

    /// <summary>경로처럼 긴 글은 한 줄로 자르고, 전체는 선택해 볼 수 있게 합니다.</summary>
    private static TextBlock RowDescription() => new()
    {
        Style = Ui.Style("YpdCaption"),
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    private static TextBlock Command(string text) => new() { Text = text, FontFamily = Ui.Monospace, FontSize = 12, IsTextSelectionEnabled = true };

    private static ToggleSwitch Switch() => new() { OnContent = L.T("On"), OffContent = L.T("Off"), MinWidth = 0 };
}

internal enum ComponentState { Ok, Missing, Warning }

/// <summary>구성 요소 한 줄: 이름·경로·안내(왼쪽), 버전 또는 상태와 아이콘(오른쪽).</summary>
internal sealed partial class ComponentRow : UserControl
{
    private readonly TextBlock path = new()
    {
        Style = Ui.Style("YpdCaption"),
        FontFamily = Ui.Monospace,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock note = Ui.Text("", "YpdCautionCaption");
    private readonly TextBlock value = Ui.Text("", "YpdSecondaryText");
    private readonly ProgressRing checking = new() { IsActive = true, Width = 16, Height = 16 };
    private readonly FontIcon icon = new() { FontSize = 16 };

    public ComponentRow(string name)
    {
        value.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Content = SettingsPage.SettingsRow(name, Ui.Column(2, path, note), Ui.Row(8, value, checking, icon));
    }

    public void Update(string valueText, string? toolPath, ComponentState state, bool isChecking, string? noteText = null)
    {
        value.Text = valueText;
        path.Text = toolPath ?? "";
        path.Visibility = Ui.Visible(toolPath != null);
        ToolTipService.SetToolTip(path, toolPath);
        note.Text = noteText ?? "";
        note.Visibility = Ui.Visible(noteText != null);
        checking.Visibility = Ui.Visible(isChecking);
        icon.Visibility = Ui.Visible(!isChecking);
        (icon.Glyph, icon.Style) = state switch
        {
            ComponentState.Ok => ("\uEC61", Ui.Style("YpdSuccessIcon")),
            ComponentState.Missing => ("\uEB90", Ui.Style("YpdCriticalIcon")),
            _ => ("\uE7BA", Ui.Style("YpdCautionIcon")),
        };
    }
}
