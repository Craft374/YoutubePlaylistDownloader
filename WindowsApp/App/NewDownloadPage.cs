using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using YPD.Core;

namespace YPD.App;

/// <summary>
/// 링크 하나로 받기(Mac NewDownloadView). 화면은 한 번만 만들어 계속 쓰므로, 입력한 주소와 조회 결과를 이 안에 둡니다.
/// </summary>
public sealed partial class NewDownloadPage : UserControl
{
    private enum Phase { Idle, Resolving, Failed, Resolved }

    private readonly ToolManager tools = ToolManager.Shared;
    private readonly AppSettings settings = AppSettings.Shared;

    private Phase phase = Phase.Idle;
    private string? failure;
    private MediaSource? source;
    /// <summary>지금 결과 카드가 보여 주는 조회 결과. 같은 결과면 카드를 다시 만들지 않습니다.</summary>
    private MediaSource? shownSource;
    private bool looksInvalid;
    private bool showAdded;
    private int addedToken;
    /// <summary>지금 phase가 가리키는 주소. 공백만 바뀐 경우 다시 조회하지 않기 위해 둡니다.</summary>
    private string? currentURL;
    private CancellationTokenSource? lookup;
    /// <summary>도구가 아직 없어 조회를 미뤘는지. 도구가 준비되면 이어서 조회합니다.</summary>
    private bool waitingForTools;
    /// <summary>TextChanged는 값을 코드로 넣은 뒤 늦게 오므로, 실제로 바뀐 경우만 처리합니다.</summary>
    private string lastText = "";

    private readonly TextBox box = new() { PlaceholderText = L.T("Paste a link"), FontSize = 16 };
    private readonly TextBlock invalid = Ui.Text(L.T("This doesn’t look like a link"), "YpdSecondaryText");
    private readonly StackPanel added;
    private readonly Border banner = new() { Child = new ToolStatusBanner() };
    private readonly StackPanel resolving;
    private readonly InfoBar failBar = new() { Severity = InfoBarSeverity.Error, IsClosable = false, IsOpen = true };
    private readonly Border result = new();
    private readonly TextBlock filterNote = Ui.Caption();
    private readonly HyperlinkButton filterOptions = new() { Content = L.T("Options…"), Padding = new Thickness(0), FontSize = 12 };
    private readonly StackPanel filterRow;

    public NewDownloadPage()
    {
        var paste = Ui.IconButton("", L.T("Paste from clipboard"), (_, _) => _ = PasteAsync());
        paste.VerticalAlignment = VerticalAlignment.Stretch;
        var field = new Grid { ColumnSpacing = 8 };
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(paste, 1);
        field.Children.Add(box);
        field.Children.Add(paste);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, L.T("Paste a link"));
        box.TextChanged += (_, _) =>
        {
            if (box.Text == lastText) return;
            lastText = box.Text;
            TextChanged();
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            LookUpNow();
        };

        added = Ui.Row(8, Ui.Icon("", 16, "YpdSuccessIcon"), Ui.Text(L.T("Added to Downloads")));
        var cancel = new Button { Content = L.T("Cancel") };
        cancel.Click += (_, _) => Cancel();
        var lookingUp = Ui.Text(L.T("Looking up…"), "YpdSecondaryText");
        lookingUp.VerticalAlignment = VerticalAlignment.Center;
        resolving = Ui.Row(8, new ProgressRing { IsActive = true, Width = 16, Height = 16 }, lookingUp, cancel);
        resolving.HorizontalAlignment = HorizontalAlignment.Center;
        var retry = new Button { Content = L.T("Try Again") };
        retry.Click += (_, _) => LookUpNow();
        failBar.ActionButton = retry;
        filterOptions.Click += (_, _) => MainWindow.Instance?.SetOptionsVisible(true);
        filterRow = Ui.Column(0, filterNote, filterOptions);

        var subtitle = Ui.Text(L.T("Paste a link to a YouTube video, playlist or channel"), "YpdSecondaryText");
        subtitle.TextAlignment = TextAlignment.Center;
        var stack = Ui.Column(Design.Spacing * 2, subtitle, field,
                              Ui.Column(Design.Spacing, invalid, added, banner, resolving, failBar, result));
        stack.MaxWidth = Design.ContentWidth;
        stack.Padding = new Thickness(24, 24, 24, 24);
        Content = new ScrollViewer { Content = stack };

        // 화면을 바꿔 끼울 때마다 Loaded가 다시 오므로(두 번 올 때도 있음) 먼저 빼고 다시 붙입니다.
        Loaded += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            settings.PropertyChanged -= OnSettingsChanged;
            tools.PropertyChanged += OnToolsChanged;
            settings.PropertyChanged += OnSettingsChanged;
            // 다른 화면에 있는 동안 도구가 준비됐을 수 있습니다.
            if (waitingForTools && tools.IsReady) LookUpNow();
            Update();
            UpdateFilterNote();
            box.Focus(FocusState.Programmatic);
        };
        Unloaded += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            settings.PropertyChanged -= OnSettingsChanged;
        };
        Update();
    }

    // MARK: 조회(Mac NewDownloadModel)

    /// <summary>입력이 멈추고 0.5초 뒤에 조회합니다.</summary>
    private void TextChanged()
    {
        string? url = SourceURL.Normalize(box.Text);
        if (url != null && url == currentURL) return;
        Start(url, 500);
    }

    /// <summary>Enter·붙여넣기·다시 시도: 바로 조회합니다. 이미 조회 중이거나 결과가 있으면 그대로 둡니다.</summary>
    private void LookUpNow()
    {
        string? url = SourceURL.Normalize(box.Text);
        if (url != null && url == currentURL && phase is Phase.Resolving or Phase.Resolved) return;
        Start(url, 0);
    }

    private void Cancel()
    {
        lookup?.Cancel();
        waitingForTools = false;
        phase = Phase.Idle;
        Update();
    }

    private void Reset()
    {
        box.Text = "";
        Cancel();
    }

    private void Start(string? url, int delay)
    {
        lookup?.Cancel();
        lookup = new CancellationTokenSource();
        currentURL = url;
        phase = Phase.Idle;
        waitingForTools = false;
        bool isEmpty = string.IsNullOrWhiteSpace(box.Text);
        if (url != null || isEmpty) looksInvalid = false;
        Update();
        _ = LookUpAsync(url, isEmpty, delay, lookup.Token);
    }

    private async Task LookUpAsync(string? url, bool isEmpty, int delay, CancellationToken token)
    {
        try
        {
            if (delay > 0) await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (token.IsCancellationRequested) return;
        if (url == null)
        {
            looksInvalid = !isEmpty;
            Update();
            return;
        }
        // 도구가 아직 없으면 조회하지 않습니다. 화면에는 도구 안내가 보입니다.
        if (tools.Paths is not { } paths)
        {
            waitingForTools = true;
            return;
        }
        phase = Phase.Resolving;
        Update();
        try
        {
            var found = await MediaResolver.ResolveAsync(url, paths, tools.Environment, token);
            if (token.IsCancellationRequested) return;
            source = found;
            phase = Phase.Resolved;
        }
        catch (Exception e)
        {
            if (token.IsCancellationRequested || e is OperationCanceledException) return;
            failure = e.Message;
            phase = Phase.Failed;
        }
        Update();
    }

    private async Task PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            box.Text = (await content.GetTextAsync()).Trim();
            LookUpNow();
        }
        catch (Exception)
        {
            // 다른 앱이 클립보드를 잡고 있으면 읽지 못합니다. 다시 누르면 됩니다.
        }
    }

    // MARK: 화면

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 실행 직후 도구 확인이 끝나기 전에 붙여 넣은 주소도 조회되게 합니다.
        if (e.PropertyName == nameof(ToolManager.IsReady) && waitingForTools && tools.IsReady) LookUpNow();
        Update();
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Options)) UpdateFilterNote();
    }

    private void Update()
    {
        bool idle = phase == Phase.Idle;
        invalid.Visibility = Ui.Visible(idle && looksInvalid);
        added.Visibility = Ui.Visible(idle && showAdded);
        banner.Visibility = Ui.Visible(idle && !tools.IsReady);
        resolving.Visibility = Ui.Visible(phase == Phase.Resolving);
        failBar.Visibility = Ui.Visible(phase == Phase.Failed);
        failBar.Message = failure ?? "";
        bool resolved = phase == Phase.Resolved && source != null;
        result.Visibility = Ui.Visible(resolved);
        if (resolved && source != shownSource)
        {
            shownSource = source;
            result.Child = ResultCard(source!);
            UpdateFilterNote();
        }
    }

    /// <summary>범위·길이 필터로 일부만 받게 되면 몇 개를 받는지 알려 줍니다. 옵션을 바꾸면 바로 다시 셉니다.</summary>
    private void UpdateFilterNote()
    {
        if (shownSource is not { } shown) return;
        int total = shown.Entries.Count;
        int selected = settings.Options.SelectEntries(shown).Count;
        filterRow.Visibility = Ui.Visible(selected != total);
        filterNote.Text = L.F("{0} of {1} will be downloaded with the current range and length filter", selected, total);
    }

    private Border ResultCard(MediaSource media)
    {
        var info = Ui.Column(6, KindBadge(media.Kind));
        var title = new TextBlock
        {
            Text = media.Title,
            Style = Ui.Style("SubtitleTextBlockStyle"),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        info.Children.Add(title);
        if (media.Uploader is { } uploader) info.Children.Add(Ui.Text(uploader, "YpdSecondaryText"));
        if (Detail(media) is { Length: > 0 } detail) info.Children.Add(Ui.Text(detail, "YpdSecondaryText"));
        // 패널은 결과마다 새로 만들므로 같은 요소를 옮겨 붙이기 전에 이전 부모에서 뗍니다.
        (filterRow.Parent as Panel)?.Children.Remove(filterRow);
        info.Children.Add(filterRow);

        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var thumbnail = Ui.Thumbnail(media.Thumbnail, 208);
        thumbnail.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(info, 1);
        top.Children.Add(thumbnail);
        top.Children.Add(info);

        var addToQueue = new Button { Content = L.T("Add to Queue") };
        addToQueue.Click += (_, _) =>
        {
            if (Enqueue(media)) _ = FlashAddedAsync();
        };
        var download = new Button { Content = L.T("Download"), Style = Ui.Style("AccentButtonStyle") };
        download.Click += (_, _) =>
        {
            if (Enqueue(media)) MainWindow.Instance?.Navigate(Screen.Queue);
        };
        var buttons = Ui.Row(Design.Spacing, addToQueue, download);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        return Ui.Card(Ui.Column(16, top, buttons));
    }

    private static string Detail(MediaSource media)
    {
        if (media.Kind != MediaKind.Video) return L.Plural("{0} videos", media.Entries.Count);
        string?[] parts = [Formatters.Duration(media.Entries.FirstOrDefault()?.Duration),
                           media.ViewCount is { } views ? L.F("{0} views", Formatters.Number(views)) : null];
        return string.Join(" · ", parts.OfType<string>());
    }

    private static Border KindBadge(MediaKind kind)
    {
        var (glyph, label) = kind switch
        {
            MediaKind.Video => ("", "Video"),
            MediaKind.Playlist => ("", "Playlist"),
            _ => ("", "Channel"),
        };
        var text = Ui.Text(L.T(label), "YpdAccentText");
        text.FontSize = 12;
        text.FontWeight = FontWeights.SemiBold;
        return new Border
        {
            Style = Ui.Style("YpdPlaceholder"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = Ui.Row(4, Ui.Icon(glyph, 12, "YpdAccentIcon"), text),
        };
    }

    /// <summary>성공하면 입력을 비웁니다. 실패하면 알림을 띄우고 결과는 그대로 둡니다.</summary>
    private bool Enqueue(MediaSource media)
    {
        try
        {
            DownloadQueue.Shared.Enqueue(media);
            Reset();
            return true;
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.T("Couldn’t add the download"), e.Message);
            return false;
        }
    }

    /// <summary>"다운로드에 추가했습니다"를 2초 보여 줍니다. 그사이 다시 추가하면 2초를 새로 셉니다.</summary>
    private async Task FlashAddedAsync()
    {
        int token = ++addedToken;
        showAdded = true;
        Update();
        await Task.Delay(2000);
        if (token != addedToken) return;
        showAdded = false;
        Update();
    }
}
