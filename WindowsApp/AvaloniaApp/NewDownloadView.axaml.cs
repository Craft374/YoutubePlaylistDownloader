using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>
/// 링크 하나로 받기. 조회 흐름은 WinUI NewDownloadPage(= Mac NewDownloadModel)와 같습니다:
/// 입력이 멈추고 0.5초 뒤 조회, Enter·붙여넣기는 바로 조회.
/// </summary>
public partial class NewDownloadView : UserControl
{
    private enum Phase { Idle, Resolving, Failed, Resolved }

    private readonly ToolManager tools = ToolManager.Shared;
    private readonly AppSettings settings = AppSettings.Shared;

    private Phase phase = Phase.Idle;
    private string? failure;
    private MediaSource? source;
    private MediaSource? shownSource;
    private bool looksInvalid;
    private bool showAdded;
    private int addedToken;
    private string? currentURL;
    private CancellationTokenSource? lookup;
    private bool waitingForTools;
    private string lastText = "";

    public NewDownloadView()
    {
        InitializeComponent();

        Box.TextChanged += (_, _) =>
        {
            string text = Box.Text ?? "";
            if (text == lastText) return;
            lastText = text;
            Update();
            TextChanged();
        };
        Box.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            LookUpNow();
        };
        ClearButton.Click += (_, _) => Box.Text = "";
        PasteButton.Click += async (_, _) => await PasteAsync();
        CancelButton.Click += (_, _) => Cancel();
        RetryButton.Click += (_, _) => LookUpNow();
        FilterOptions.Click += (_, _) => MainWindow.Instance?.ShowOptions();
        AddButton.Click += async (_, _) =>
        {
            if (shownSource is { } media && Enqueue(media)) await FlashAddedAsync();
        };
        DownloadButton.Click += (_, _) => { if (shownSource is { } media && Enqueue(media)) MainWindow.Instance?.ShowQueue(); };

        AttachedToVisualTree += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            settings.PropertyChanged -= OnSettingsChanged;
            tools.PropertyChanged += OnToolsChanged;
            settings.PropertyChanged += OnSettingsChanged;
            if (waitingForTools && tools.IsReady) LookUpNow();
            Update();
            // 다른 화면에 있는 동안 옵션 패널에서 필터를 바꿨을 수 있습니다.
            UpdateFilterNote();
            Box.Focus();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            settings.PropertyChanged -= OnSettingsChanged;
        };
        Update();
    }

    // MARK: 조회

    private void TextChanged()
    {
        string? url = SourceURL.Normalize(Box.Text ?? "");
        if (url != null && url == currentURL) return;
        Start(url, 500);
    }

    private void LookUpNow()
    {
        string? url = SourceURL.Normalize(Box.Text ?? "");
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

    private void Start(string? url, int delay)
    {
        lookup?.Cancel();
        lookup = new CancellationTokenSource();
        currentURL = url;
        phase = Phase.Idle;
        waitingForTools = false;
        bool isEmpty = string.IsNullOrWhiteSpace(Box.Text);
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
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            if (await clipboard.TryGetTextAsync() is not { } text) return;
            Box.Text = text.Trim();
            LookUpNow();
        }
        catch (Exception)
        {
            // 클립보드를 읽지 못하면 다시 누르면 됩니다.
        }
    }

    // MARK: 화면

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (e.PropertyName == nameof(ToolManager.IsReady) && waitingForTools && tools.IsReady) LookUpNow();
        Update();
    });

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.Options)) Dispatcher.UIThread.Post(UpdateFilterNote);
    }

    private void Update()
    {
        bool idle = phase == Phase.Idle;
        bool hasText = !string.IsNullOrEmpty(Box.Text);
        ClearButton.IsVisible = hasText;
        Invalid.IsVisible = idle && looksInvalid;
        Added.IsVisible = idle && showAdded;
        Resolving.IsVisible = phase == Phase.Resolving;
        Failure.IsVisible = phase == Phase.Failed;
        FailureText.Text = failure ?? "";
        bool resolved = phase == Phase.Resolved && source != null;
        Result.IsVisible = resolved;
        if (resolved && source != shownSource)
        {
            shownSource = source;
            ShowResult(source!);
        }
    }

    private void ShowResult(MediaSource media)
    {
        KindText.Text = L.T(media.Kind switch { MediaKind.Video => "Video", MediaKind.Playlist => "Playlist", _ => "Channel" });
        TitleText.Text = media.Title;
        UploaderText.Text = media.Uploader ?? "";
        UploaderText.IsVisible = media.Uploader != null;
        DetailText.Text = Detail(media);
        EnqueueError.IsVisible = false;
        RemoteImage.Load(Thumbnail, media.Thumbnail);
        UpdateFilterNote();
    }

    private void UpdateFilterNote()
    {
        if (shownSource is not { } shown) return;
        int total = shown.Entries.Count;
        int selected = settings.Options.SelectEntries(shown).Count;
        FilterRow.IsVisible = selected != total;
        FilterNote.Text = L.F("{0} of {1} will be downloaded with the current range and length filter", selected, total);
    }

    private static string Detail(MediaSource media)
    {
        if (media.Kind != MediaKind.Video) return L.Plural("{0} videos", media.Entries.Count);
        string?[] parts = [Formatters.Duration(media.Entries.FirstOrDefault()?.Duration),
                           media.ViewCount is { } views ? L.F("{0} views", Formatters.Number(views)) : null];
        return string.Join(" · ", parts.OfType<string>());
    }

    private bool Enqueue(MediaSource media)
    {
        try
        {
            DownloadQueue.Shared.Enqueue(media);
            Box.Text = "";
            Cancel();
            return true;
        }
        catch (Exception e)
        {
            // 결과는 그대로 두고 카드 안에 이유만 보여 줍니다(필터로 받을 영상이 없을 때 등).
            EnqueueError.Text = L.T("Couldn’t add the download") + ": " + e.Message;
            EnqueueError.IsVisible = true;
            return false;
        }
    }

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
