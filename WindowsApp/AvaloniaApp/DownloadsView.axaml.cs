using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using YPD.Core;

namespace YPD.Desktop;

public partial class DownloadsView : UserControl
{
    private readonly DownloadQueue queue = DownloadQueue.Shared;

    public DownloadsView()
    {
        InitializeComponent();
        List.ItemsSource = queue.Jobs;
        List.ItemTemplate = new FuncDataTemplate<DownloadJob>((_, _) => new JobRow(), supportsRecycling: true);
        ClearFinished.Click += (_, _) => queue.ClearFinished();
        StopAll.Click += (_, _) => queue.CancelAll();
        NewDownload.Click += (_, _) => MainWindow.Instance?.Navigate(Screen.NewDownload);

        AttachedToVisualTree += (_, _) =>
        {
            queue.PropertyChanged -= OnQueueChanged;
            queue.Jobs.CollectionChanged -= OnJobsChanged;
            queue.PropertyChanged += OnQueueChanged;
            queue.Jobs.CollectionChanged += OnJobsChanged;
            Update();
        };
        DetachedFromVisualTree += (_, _) =>
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
        Scroller.IsVisible = hasJobs;
        Motion.SetVisible(Empty, !hasJobs);
        ClearFinished.IsEnabled = queue.HasFinishedJobs;
        StopAll.IsEnabled = queue.HasActiveWork;
    }
}
