using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>설정 화면. 바꾸는 즉시 AppSettings가 저장합니다(WinUI SettingsPage와 같은 흐름).</summary>
public partial class SettingsView : UserControl
{
    private readonly AppSettings settings = AppSettings.Shared;
    private readonly ToolManager tools = ToolManager.Shared;
    private bool updating;

    public SettingsView()
    {
        InitializeComponent();
        Language.Items.Add(L.T("System"));
        Language.Items.Add("English");
        Language.Items.Add("한국어");
        Concurrency.Minimum = AppSettings.ConcurrencyRange.Min;
        Concurrency.Maximum = AppSettings.ConcurrencyRange.Max;
        ToolTip.SetTip(RevealFolder, Explorer.ShowLabel);
        AutomationProperties.SetName(RevealFolder, Explorer.ShowLabel);
        DataFolder.Text = ToolManager.SupportDirectory;
        Ytdlp.Title = "yt-dlp";
        Ffmpeg.Title = "ffmpeg";
        JsRuntime.Title = L.T("JavaScript runtime");
        // 패키지 관리자로 설치한 도구는 PATH가 바뀌므로, 이미 떠 있는 앱은 다시 열어야 찾습니다.
        bool mac = OperatingSystem.IsMacOS();
        InstallTitle.Text = L.T(mac ? "Install missing components with Homebrew:" : "Install missing components with winget:");
        InstallFFmpeg.Text = mac ? "brew install ffmpeg" : "winget install Gyan.FFmpeg";
        InstallDeno.Text = mac ? "brew install deno" : "winget install DenoLand.Deno";

        SystemMode.IsCheckedChanged += (_, _) => { if (!updating && SystemMode.IsChecked == true) settings.Appearance = Appearance.System; };
        LightMode.IsCheckedChanged += (_, _) => { if (!updating && LightMode.IsChecked == true) settings.Appearance = Appearance.Light; };
        DarkMode.IsCheckedChanged += (_, _) => { if (!updating && DarkMode.IsChecked == true) settings.Appearance = Appearance.Dark; };
        Language.SelectionChanged += (_, _) => { if (!updating && Language.SelectedIndex >= 0) settings.Language = (AppLanguage)Language.SelectedIndex; };
        ChooseFolder.Click += async (_, _) => await Ui.ChooseSaveFolderAsync(this);
        RevealFolder.Click += (_, _) => Explorer.OpenFolder(settings.SaveDirectory);
        Concurrency.ValueChanged += (_, e) =>
        {
            if (updating) return;
            // 비운 칸(null)은 원래 값으로 되돌립니다.
            if (e.NewValue is { } value) settings.MaxConcurrentDownloads = (int)value;
            UpdateSettings();
        };
        ConfirmQuit.IsCheckedChanged += (_, _) => { if (!updating) settings.ConfirmQuitWhileDownloading = ConfirmQuit.IsChecked == true; };
        AutoUpdate.IsCheckedChanged += (_, _) => { if (!updating) settings.AutoUpdateYTDLP = AutoUpdate.IsChecked == true; };
        UpdateButton.Click += async (_, _) => await tools.UpdateYTDLPAsync(settings);
        CheckAgain.Click += async (_, _) => await tools.RefreshAsync();
        OpenData.Click += (_, _) =>
        {
            Directory.CreateDirectory(ToolManager.SupportDirectory);
            Explorer.OpenFolder(ToolManager.SupportDirectory);
        };

        AttachedToVisualTree += (_, _) =>
        {
            settings.PropertyChanged -= OnChanged;
            tools.PropertyChanged -= OnChanged;
            settings.PropertyChanged += OnChanged;
            tools.PropertyChanged += OnChanged;
            UpdateSettings();
            UpdateTools();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            settings.PropertyChanged -= OnChanged;
            tools.PropertyChanged -= OnChanged;
        };
        foreach (var rows in new[] { GeneralRows, ComponentRows, UpdateRows }) Ui.UpdateDividers(rows);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (sender == tools) UpdateTools();
        else if (e.PropertyName != nameof(AppSettings.Options)) UpdateSettings();
    });

    private void UpdateSettings()
    {
        updating = true;
        try
        {
            (settings.Appearance switch { Appearance.Light => LightMode, Appearance.Dark => DarkMode, _ => SystemMode }).IsChecked = true;
            Language.SelectedIndex = (int)settings.Language;
            SaveDirectory.Text = settings.SaveDirectory;
            ToolTip.SetTip(SaveDirectory, settings.SaveDirectory);
            Concurrency.Value = settings.MaxConcurrentDownloads;
            ConfirmQuit.IsChecked = settings.ConfirmQuitWhileDownloading;
            AutoUpdate.IsChecked = settings.AutoUpdateYTDLP;
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
        Ytdlp.Update(tools.YtdlpVersion ?? L.T(tools.IsReady ? "Installed" : "Not installed"), paths?.Ytdlp,
                     tools.IsReady ? ComponentState.Ok : ComponentState.Missing, checking);
        Ffmpeg.Update(L.T(tools.HasFFmpeg ? "Installed" : "Not found"), paths?.Ffmpeg,
                      tools.HasFFmpeg ? ComponentState.Ok : ComponentState.Missing, checking);
        JsRuntime.Update(tools.JsRuntimeName ?? L.T("Not found"), paths?.JsRuntime?.Path,
                         tools.JsRuntimeName != null ? ComponentState.Ok : ComponentState.Warning, checking,
                         tools.JsRuntimeName == null ? L.T("Needed for some YouTube formats") : null);
        InstallHint.IsVisible = !checking && (!tools.HasFFmpeg || tools.JsRuntimeName == null);
        InstallFFmpeg.IsVisible = !tools.HasFFmpeg;
        InstallDeno.IsVisible = tools.JsRuntimeName == null;
        CheckAgain.IsEnabled = !checking;
        UpdateButton.Content = L.T(tools.IsReady ? "Update yt-dlp" : "Install yt-dlp");
        UpdateButton.IsEnabled = !tools.IsUpdating;
        UpdateProgress.IsVisible = tools.IsUpdating;
        UpdateStatus.Text = tools.UpdateStatus ?? "";
        UpdateStatus.IsVisible = tools.UpdateStatus != null;
    }
}

public enum ComponentState { Ok, Missing, Warning }

/// <summary>구성 요소 한 줄: 이름·경로·안내(왼쪽), 버전 또는 상태와 아이콘(오른쪽).</summary>
public sealed class ComponentRow : Border
{
    private readonly TextBlock title = new();
    private readonly TextBlock path = new() { Classes = { "caption", "mono" }, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock note = new() { Classes = { "caption", "warning" } };
    private readonly TextBlock value = new() { Classes = { "secondary" }, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly ProgressBar checking = new() { IsIndeterminate = true, Width = 32, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly Icon icon = new() { Width = 16, Height = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };

    public ComponentRow()
    {
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Children = { title, path, note } };
        var status = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { value, checking, icon } };
        Grid.SetColumn(status, 1);
        Child = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { labels, status } };
    }

    public string Title { get => title.Text ?? ""; set => title.Text = value; }

    public void Update(string valueText, string? toolPath, ComponentState state, bool isChecking, string? noteText = null)
    {
        value.Text = valueText;
        path.Text = toolPath ?? "";
        path.IsVisible = toolPath != null;
        ToolTip.SetTip(path, toolPath);
        note.Text = noteText ?? "";
        note.IsVisible = noteText != null;
        checking.IsVisible = isChecking;
        icon.IsVisible = !isChecking;
        icon.Data = this.FindResource(state switch
        {
            ComponentState.Ok => "IconCheckCircle",
            ComponentState.Missing => "IconXCircle",
            _ => "IconAlert",
        }) as Geometry;
        icon.Classes.Set("success", state == ComponentState.Ok);
        icon.Classes.Set("danger", state == ComponentState.Missing);
        icon.Classes.Set("warning", state == ComponentState.Warning);
        AutomationProperties.SetName(this, $"{Title}, {valueText}");
    }
}
