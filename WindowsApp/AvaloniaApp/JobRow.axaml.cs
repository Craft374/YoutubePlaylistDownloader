using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>
/// 작업 한 줄. 목록이 줄을 다시 쓰므로(DataContext가 다른 작업으로 바뀜) 화면에 붙어 있고 작업이 있을 때만 구독합니다.
/// </summary>
public partial class JobRow : UserControl
{
    private DownloadJob? job;
    private bool attached;

    public JobRow()
    {
        InitializeComponent();
        Items.ItemTemplate = new FuncDataTemplate<DownloadItem>((_, _) => new ItemRow(), supportsRecycling: true);
        string reveal = Explorer.ShowLabel;
        ToolTip.SetTip(FolderButton, reveal);
        AutomationProperties.SetName(FolderButton, reveal);
        MenuFolder.Header = reveal;

        ExpandButton.Click += (_, _) => { if (job != null) job.IsExpanded = !job.IsExpanded; };
        FolderButton.Click += (_, _) => ShowInExplorer();
        StopButton.Click += (_, _) => Stop();
        RetryButton.Click += (_, _) => Retry();
        RemoveButton.Click += (_, _) => Remove();
        MenuFolder.Click += (_, _) => ShowInExplorer();
        MenuRetry.Click += (_, _) => Retry();
        MenuStop.Click += (_, _) => Stop();
        MenuRemove.Click += (_, _) => Remove();
        Buttons.DoubleTapped += (_, e) => e.Handled = true;
        Header.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ShowInExplorer();
        };
        CopyErrors.Click += async (_, _) => await Ui.CopyAsync(this, FailureReport());
        SaveErrors.Click += async (_, _) =>
        {
            if (job != null) await Ui.SaveTextAsync(this, L.T("Videos not downloaded.txt"), FailureReport(), job.Directory);
        };

        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            Subscribe();
            Update();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            Unsubscribe();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var next = DataContext as DownloadJob;
        if (next == job) return;
        Unsubscribe();
        job = next;
        RemoteImage.Load(Thumbnail, next?.Thumbnail);
        // 다른 작업의 진행률에서 미끄러져 오지 않게 바로 맞춥니다.
        Progress.Transitions = null;
        Progress.Value = next?.Progress ?? 0;
        Progress.ClearValue(Animatable.TransitionsProperty);
        Items.ItemsSource = null;
        if (next == null) return;
        Subscribe();
        Update();
    }

    private void Subscribe()
    {
        if (job == null || !attached) return;
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
        string status = Status(j);

        TitleText.Text = j.Title;
        ToolTip.SetTip(TitleText, j.Title);
        StatusText.Text = status;
        CurrentText.Text = j.CurrentTitle ?? "";
        CurrentText.IsVisible = !finished && j.Items.Count > 1 && j.CurrentTitle != null;
        Progress.Value = j.Progress;
        // 실패가 있으면 주의 색으로 끝납니다(Mac은 주황색).
        Progress.Classes.Set("warning", finished && failedCount > 0);

        ExpandButton.IsVisible = j.Items.Count > 1 || failedCount > 0 || expanded;
        ExpandIcon.Data = this.FindResource(expanded ? "IconChevronUp" : "IconChevronDown") as Geometry;
        string expandLabel = L.T(expanded ? "Hide items" : "Show items");
        AutomationProperties.SetName(ExpandButton, expandLabel);
        ToolTip.SetTip(ExpandButton, expandLabel);
        StopButton.IsVisible = MenuStop.IsVisible = !finished;
        RetryButton.IsVisible = MenuRetry.IsVisible = canRetry;
        RemoveButton.IsVisible = finished;
        AutomationProperties.SetName(this, j.Title + ", " + status);

        Motion.SetVisible(Details, expanded);
        // 항목 목록은 펼칠 때만 채웁니다(항목 구성은 바뀌지 않습니다).
        if (expanded && Items.ItemsSource == null) Items.ItemsSource = j.Items;
        else if (!expanded) Items.ItemsSource = null;
        FailureActions.IsVisible = finished && failedCount > 0;
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

    private void ShowInExplorer() { if (job != null) Ui.ShowInExplorer(job); }
    private void Stop() { if (job != null) DownloadQueue.Shared.Cancel(job); }
    private void Retry() { if (job != null) DownloadQueue.Shared.RetryFailed(job); }
    private void Remove() { if (job != null) DownloadQueue.Shared.Remove(job); }
}
