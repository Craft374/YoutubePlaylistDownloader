using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YPD.Core;

namespace YPD.App;

/// <summary>
/// "다운로드" 화면(Mac QueueView). 최근 작업이 위에 옵니다.
/// 목록은 가상화되어 보이는 행만 만들고, 행은 자기 작업·항목만 구독해 진행률이 바뀌면 그 행만 고칩니다.
/// </summary>
public sealed partial class DownloadsPage : UserControl
{
    private readonly DownloadQueue queue = DownloadQueue.Shared;
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.None, Padding = new Thickness(12, 0, 12, 12) };
    private readonly StackPanel empty;
    private readonly AppBarButton clearFinished = ToolbarButton("", "Clear Finished", "Remove finished downloads from the list");
    private readonly AppBarButton stopAll = ToolbarButton("", "Stop All", "Stop all downloads");

    public DownloadsPage()
    {
        list.ItemsSource = queue.Jobs;
        Ui.HostRows(list, () => new JobRow(), (row, item) => row.Bind(item as DownloadJob));
        clearFinished.Click += (_, _) => queue.ClearFinished();
        stopAll.Click += (_, _) => queue.CancelAll();
        var toolbar = new CommandBar
        {
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Right,
            OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
            IsDynamicOverflowEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(12, 0, 0, 0),
        };
        toolbar.PrimaryCommands.Add(clearFinished);
        toolbar.PrimaryCommands.Add(stopAll);

        var newDownload = new Button { Content = L.T("New Download"), HorizontalAlignment = HorizontalAlignment.Center };
        newDownload.Click += (_, _) => MainWindow.Instance?.Navigate(Screen.NewDownload);
        var emptyIcon = Ui.Icon("", 48, "YpdTertiaryIcon");
        var emptyTitle = Ui.Text(L.T("No Downloads"), "SubtitleTextBlockStyle");
        var emptyText = Ui.Text(L.T("Videos you download appear here"), "YpdSecondaryText");
        emptyTitle.TextAlignment = emptyText.TextAlignment = TextAlignment.Center;
        empty = Ui.Column(8, emptyIcon, emptyTitle, emptyText, newDownload);
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        newDownload.Margin = new Thickness(0, 8, 0, 0);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(list, 1);
        Grid.SetRow(empty, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(list);
        grid.Children.Add(empty);
        Content = grid;

        // 화면을 바꿔 끼울 때마다 Loaded가 다시 오므로(두 번 올 때도 있음) 먼저 빼고 다시 붙입니다.
        Loaded += (_, _) =>
        {
            queue.PropertyChanged -= OnQueueChanged;
            queue.Jobs.CollectionChanged -= OnJobsChanged;
            queue.PropertyChanged += OnQueueChanged;
            queue.Jobs.CollectionChanged += OnJobsChanged;
            Update();
        };
        Unloaded += (_, _) =>
        {
            queue.PropertyChanged -= OnQueueChanged;
            queue.Jobs.CollectionChanged -= OnJobsChanged;
        };
        Update();
    }

    private void OnQueueChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void OnJobsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void Update()
    {
        bool hasJobs = queue.Jobs.Count > 0;
        list.Visibility = Ui.Visible(hasJobs);
        empty.Visibility = Ui.Visible(!hasJobs);
        clearFinished.IsEnabled = queue.HasFinishedJobs;
        stopAll.IsEnabled = queue.HasActiveWork;
    }

    private static AppBarButton ToolbarButton(string glyph, string label, string help)
    {
        var button = new AppBarButton { Label = L.T(label), Icon = new FontIcon { Glyph = glyph } };
        ToolTipService.SetToolTip(button, L.T(help));
        return button;
    }

    /// <summary>작업 폴더를 엽니다. 파일 하나짜리 작업이면 그 파일을 선택해 보여 줍니다.</summary>
    internal static void ShowInExplorer(DownloadJob job, DownloadItem? item = null)
    {
        item ??= job.Items.Count == 1 ? job.Items[0] : null;
        if (item?.FilePath is { } file && File.Exists(file)) Explorer.RevealFile(file);
        else Explorer.OpenFolder(job.Directory);
    }
}

/// <summary>
/// 작업 한 줄: 썸네일, 제목, 상태, 진행 막대, 버튼. 펼치면 아래에 항목 목록(가상화)과 실패 목록 복사·저장 버튼이 붙습니다.
/// 컨테이너마다 한 번 만들고 Bind로 다른 작업에 다시 씁니다. 화면에 붙어 있고 작업이 있을 때만 구독합니다.
/// </summary>
internal sealed partial class JobRow : UserControl
{
    private DownloadJob? job;
    private bool loaded;

    private readonly Border thumbnail = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock status = new() { Style = Ui.Style("YpdSecondaryText"), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock current = new() { Style = Ui.Style("YpdTertiaryText"), FontSize = 12, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar progress = new() { Maximum = 1, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button expand;
    private readonly FontIcon expandIcon = new() { FontSize = 16 };
    private readonly Button stop;
    private readonly Button retry;
    private readonly Button remove;
    private readonly MenuFlyoutItem menuRetry;
    private readonly MenuFlyoutItem menuStop;
    private readonly StackPanel details = new() { Spacing = 4, Margin = new Thickness(0, 8, 0, 4) };
    private ListView? items;
    private readonly StackPanel failureActions;

    public JobRow()
    {
        expand = new Button { Content = expandIcon, Padding = new Thickness(8, 6, 8, 6) };
        expand.Click += (_, _) => { if (job != null) job.IsExpanded = !job.IsExpanded; };
        var folder = Ui.IconButton("", L.T("Show in File Explorer"), (_, _) => ShowInExplorer());
        stop = Ui.IconButton("", L.T("Stop"), (_, _) => Stop());
        retry = Ui.IconButton("", L.T("Retry failed downloads"), (_, _) => Retry());
        remove = Ui.IconButton("", L.T("Remove from list"), (_, _) => Remove());
        var buttons = Ui.Row(4, expand, folder, stop, retry, remove);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        // 버튼을 빠르게 두 번 눌러도 행의 두 번 누르기(폴더 열기)로 번지지 않게 합니다.
        buttons.DoubleTapped += (_, e) => e.Handled = true;

        var text = Ui.Column(2, title, status, current, progress);
        text.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid { ColumnSpacing = Design.Spacing, Padding = new Thickness(0, 6, 0, 6), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(buttons, 2);
        header.Children.Add(thumbnail);
        header.Children.Add(text);
        header.Children.Add(buttons);
        header.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ShowInExplorer();
        };

        var menu = new MenuFlyout();
        var menuFolder = new MenuFlyoutItem { Text = L.T("Show in File Explorer") };
        menuFolder.Click += (_, _) => ShowInExplorer();
        menuRetry = new MenuFlyoutItem { Text = L.T("Retry Failed") };
        menuRetry.Click += (_, _) => Retry();
        menuStop = new MenuFlyoutItem { Text = L.T("Stop") };
        menuStop.Click += (_, _) => Stop();
        var menuRemove = new MenuFlyoutItem { Text = L.T("Remove") };
        menuRemove.Click += (_, _) => Remove();
        foreach (var item in new MenuFlyoutItemBase[] { menuFolder, menuRetry, menuStop, new MenuFlyoutSeparator(), menuRemove }) menu.Items.Add(item);
        header.ContextFlyout = menu;

        var copy = new Button { Content = L.T("Copy Error List") };
        copy.Click += (_, _) => Ui.CopyText(FailureReport());
        var save = new Button { Content = L.T("Save Error List…") };
        save.Click += (_, _) =>
        {
            if (job != null) _ = Ui.SaveTextAsync(save, L.T("Videos not downloaded.txt"), FailureReport(), job.Directory);
        };
        failureActions = Ui.Row(8, copy, save);
        failureActions.Margin = new Thickness(24, 4, 0, 0);
        details.Children.Add(failureActions);

        Content = Ui.Column(0, header, details);

        Loaded += (_, _) =>
        {
            loaded = true;
            Subscribe();
            Update();
        };
        Unloaded += (_, _) =>
        {
            loaded = false;
            Unsubscribe();
        };
    }

    public void Bind(DownloadJob? next)
    {
        if (next == job) return;
        Unsubscribe();
        job = next;
        thumbnail.Child = next == null ? null : Ui.Thumbnail(next.Thumbnail, 112);
        // 항목 목록은 작업마다 다시 채웁니다(펼친 상태는 작업에 저장돼 있습니다).
        if (items != null) items.ItemsSource = null;
        if (next == null) return;
        Subscribe();
        Update();
    }

    private void Subscribe()
    {
        if (job == null || !loaded) return;
        job.PropertyChanged -= OnJobChanged;
        job.PropertyChanged += OnJobChanged;
    }

    private void Unsubscribe()
    {
        if (job != null) job.PropertyChanged -= OnJobChanged;
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        if (job is not { } j) return;
        bool finished = j.IsFinished;
        int failedCount = j.FailedCount;
        bool expanded = j.IsExpanded;
        bool canRetry = finished && (failedCount > 0 || j.IsCancelled);
        string statusText = Status(j);

        title.Text = j.Title;
        ToolTipService.SetToolTip(title, j.Title);
        status.Text = statusText;
        current.Text = j.CurrentTitle ?? "";
        current.Visibility = Ui.Visible(!finished && j.Items.Count > 1 && j.CurrentTitle != null);
        progress.Value = j.Progress;
        // 실패가 있으면 주의 색으로 끝납니다(Mac은 주황색).
        progress.ShowPaused = finished && failedCount > 0;

        expand.Visibility = Ui.Visible(j.Items.Count > 1 || failedCount > 0 || expanded);
        expandIcon.Glyph = expanded ? "" : "";
        string expandLabel = L.T(expanded ? "Hide items" : "Show items");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expand, expandLabel);
        ToolTipService.SetToolTip(expand, expandLabel);
        stop.Visibility = menuStop.Visibility = Ui.Visible(!finished);
        retry.Visibility = menuRetry.Visibility = Ui.Visible(canRetry);
        remove.Visibility = Ui.Visible(finished);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, j.Title + ", " + statusText);

        details.Visibility = Ui.Visible(expanded);
        if (expanded && items?.ItemsSource == null) ShowItems(j);
        else if (!expanded && items != null) items.ItemsSource = null;
        failureActions.Visibility = Ui.Visible(finished && failedCount > 0);
    }

    /// <summary>
    /// 항목이 수천 개여도 버벅이지 않게, 높이를 제한한 안쪽 목록이 보이는 항목만 만듭니다.
    /// 목록은 처음 펼칠 때 한 번 만들고 다른 작업에 다시 씁니다.
    /// </summary>
    private void ShowItems(DownloadJob j)
    {
        if (items == null)
        {
            items = new ListView { SelectionMode = ListViewSelectionMode.None, MaxHeight = 360 };
            Ui.HostRows(items, () => new ItemRow(), (row, item) => row.Bind(item as DownloadItem, job));
            details.Children.Insert(0, items);
        }
        // WinRT로 넘기기 쉬운 List로 복사합니다(항목 구성은 바뀌지 않습니다).
        items.ItemsSource = new List<DownloadItem>(j.Items);
    }

    private static string Status(DownloadJob job)
    {
        if (job.IsFinished)
        {
            if (job.IsCancelled) return L.T("Cancelled");
            return job.FailedCount == 0 ? L.T("Done") : L.F("Done · {0} failed", job.FailedCount);
        }
        if (!job.IsRunning) return L.T("Waiting…");
        List<string> parts = [];
        if (job.Items.Count > 1) parts.Add(L.F("{0} of {1}", job.CompletedCount, job.Items.Count));
        if (job.IsProcessing) parts.Add(L.T("Processing…"));
        else if (Formatters.Speed(job.Speed) is { } speed) parts.Add(speed);
        return parts.Count == 0 ? L.T("Downloading…") : string.Join(" · ", parts);
    }

    private string FailureReport() =>
        job == null ? "" : string.Join(Environment.NewLine, job.FailedItems.Select(i => $"{i.Entry.Index}. {i.Entry.Title} — {i.Error}"));

    private void ShowInExplorer() { if (job != null) DownloadsPage.ShowInExplorer(job); }
    private void Stop() { if (job != null) DownloadQueue.Shared.Cancel(job); }
    private void Retry() { if (job != null) DownloadQueue.Shared.RetryFailed(job); }
    private void Remove() { if (job != null) DownloadQueue.Shared.Remove(job); }
}

/// <summary>펼친 작업 안의 항목 한 줄: 상태 아이콘, 번호, 제목, 진행률, 실패 이유.</summary>
internal sealed partial class ItemRow : UserControl
{
    private DownloadItem? item;
    private DownloadJob? job;
    private bool loaded;

    private readonly FontIcon icon = new() { FontSize = 14 };
    private readonly ProgressRing ring = new() { IsIndeterminate = false, Width = 14, Height = 14 };
    private readonly TextBlock index = new() { Style = Ui.Style("YpdTertiaryText"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock percent = new() { Style = Ui.Style("YpdSecondaryText"), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock error = new() { Style = Ui.Style("YpdCriticalCaption"), MaxLines = 3, IsTextSelectionEnabled = true, Margin = new Thickness(24, 0, 0, 0) };
    private readonly MenuFlyout menu = new();

    public ItemRow()
    {
        var iconHost = new Grid { Width = 16, VerticalAlignment = VerticalAlignment.Center };
        iconHost.Children.Add(icon);
        iconHost.Children.Add(ring);
        var line = new Grid { ColumnSpacing = 8 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(index, 1);
        Grid.SetColumn(title, 2);
        Grid.SetColumn(percent, 3);
        line.Children.Add(iconHost);
        line.Children.Add(index);
        line.Children.Add(title);
        line.Children.Add(percent);

        var reveal = new MenuFlyoutItem { Text = L.T("Show in File Explorer") };
        reveal.Click += (_, _) => Reveal();
        menu.Items.Add(reveal);
        // 끝난 항목만 두 번 눌러 파일을 보여 줍니다.
        DoubleTapped += (_, e) =>
        {
            if (item?.State != ItemState.Done) return;
            e.Handled = true;
            Reveal();
        };

        Content = Ui.Column(2, line, error);
        Padding = new Thickness(0, 4, 0, 4);
        Loaded += (_, _) =>
        {
            loaded = true;
            Subscribe();
            Update();
        };
        Unloaded += (_, _) =>
        {
            loaded = false;
            Unsubscribe();
        };
    }

    public void Bind(DownloadItem? next, DownloadJob? owner)
    {
        job = owner;
        if (next == item) return;
        Unsubscribe();
        item = next;
        if (next == null) return;
        Subscribe();
        Update();
    }

    private void Subscribe()
    {
        if (item == null || !loaded) return;
        item.PropertyChanged -= OnItemChanged;
        item.PropertyChanged += OnItemChanged;
    }

    private void Unsubscribe()
    {
        if (item != null) item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        if (item is not { } i) return;
        var state = i.State;
        index.Text = $"{i.Entry.Index}.";
        title.Text = i.Entry.Title;
        ToolTipService.SetToolTip(title, i.Entry.Title);
        percent.Text = $"{(int)(i.Progress * 100)}%";
        percent.Visibility = Ui.Visible(state.IsRunning());
        error.Text = i.Error ?? "";
        error.Visibility = Ui.Visible(state == ItemState.Failed);
        ring.Visibility = Ui.Visible(state == ItemState.Downloading);
        ring.Value = i.Progress * 100;
        icon.Visibility = Ui.Visible(state != ItemState.Downloading);
        var (glyph, style, label) = state switch
        {
            ItemState.Queued => ("", "YpdTertiaryIcon", "Queued"),
            ItemState.Downloading => ("", "YpdSecondaryIcon", "Downloading"),
            ItemState.Processing => ("", "YpdSecondaryIcon", "Processing"),
            ItemState.Done => ("", "YpdSuccessIcon", "Done"),
            ItemState.Skipped => ("", "YpdSecondaryIcon", "Already exists"),
            ItemState.Failed => ("", "YpdCautionIcon", "Failed"),
            _ => ("", "YpdSecondaryIcon", "Cancelled"),
        };
        icon.Glyph = glyph;
        icon.Style = Ui.Style(style);
        ToolTipService.SetToolTip(icon, state == ItemState.Skipped ? L.T(label) : null);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"{i.Entry.Index}. {i.Entry.Title}, {L.T(label)}");
        ContextFlyout = state == ItemState.Done ? menu : null;
    }

    private void Reveal()
    {
        if (job != null && item != null) DownloadsPage.ShowInExplorer(job, item);
    }
}
