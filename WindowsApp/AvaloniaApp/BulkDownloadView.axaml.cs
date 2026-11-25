using System.ComponentModel;
using Avalonia.Controls;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>
/// 여러 링크를 한 번에 대기열에 넣습니다(WinUI BulkDownloadPage와 같은 흐름). 화면은 한 번만 만들어 계속 쓰므로,
/// 다른 화면으로 가도 진행 중인 추가 작업은 멈추지 않습니다.
/// </summary>
public partial class BulkDownloadView : UserControl
{
    private readonly ToolManager tools = ToolManager.Shared;
    private readonly AppSettings settings = AppSettings.Shared;

    private bool isRunning;
    private int done;
    private int total;
    private string? currentLink;
    private int addedCount;
    private List<(string Link, string Reason)> failures = [];
    private CancellationTokenSource? task;
    private int linkCount;

    public BulkDownloadView()
    {
        InitializeComponent();
        Editor.TextChanged += (_, _) =>
        {
            linkCount = SourceURL.Lines(Editor.Text ?? "").Count;
            Update();
        };
        DownloadAll.Click += (_, _) => StartFromText();
        StopButton.Click += (_, _) => task?.Cancel();
        ShowDownloads.Click += (_, _) => MainWindow.Instance?.ShowQueue();
        RetryButton.Click += (_, _) => Run([.. failures.Select(f => f.Link)]);
        CopyButton.Click += async (_, _) => await Ui.CopyAsync(this, FailureReport);
        SaveButton.Click += async (_, _) => await Ui.SaveTextAsync(this, L.T("Links not added.txt"), FailureReport);
        DismissButton.Click += (_, _) =>
        {
            failures = [];
            Update();
        };

        AttachedToVisualTree += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            tools.PropertyChanged += OnToolsChanged;
            Update();
        };
        DetachedFromVisualTree += (_, _) => tools.PropertyChanged -= OnToolsChanged;
        Update();
    }

    private string FailureReport => string.Join(Environment.NewLine, failures.Select(f => $"{f.Link} — {f.Reason}"));

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(Update);

    private void StartFromText()
    {
        var links = SourceURL.Lines(Editor.Text ?? "");
        if (links.Count == 0) return;
        Editor.Text = "";
        Run(links);
    }

    /// <summary>링크를 하나씩 차례로 조회해 대기열에 넣습니다. 동시에 여러 개를 조회하면 유튜브가 막을 수 있습니다.</summary>
    private void Run(List<string> links)
    {
        if (isRunning || links.Count == 0) return;
        isRunning = true;
        done = 0;
        total = links.Count;
        addedCount = 0;
        failures = [];
        task = new CancellationTokenSource();
        // 시작할 때의 옵션과 폴더로 모든 링크를 받습니다. 조회 중에 옵션을 바꿔도 섞이지 않습니다(옵션은 바꿀 때마다 새 객체).
        _ = RunAsync(links, settings.Options, settings.SaveDirectory, task.Token);
    }

    private async Task RunAsync(List<string> links, DownloadOptions options, string directory, CancellationToken token)
    {
        try
        {
            foreach (string link in links)
            {
                if (token.IsCancellationRequested) break;
                currentLink = link;
                Update();
                try
                {
                    string url = SourceURL.Normalize(link) ?? throw new UserFacingError(L.T("Not a valid link"));
                    var paths = tools.Paths ?? throw new EnqueueError(L.T("yt-dlp is not installed. Install it in Settings → Components."));
                    var source = await MediaResolver.ResolveAsync(url, paths, tools.Environment, token);
                    DownloadQueue.Shared.Enqueue(source, options, directory);
                    addedCount++;
                }
                catch (Exception e)
                {
                    if (token.IsCancellationRequested) break;
                    failures.Add((link, e.Message));
                }
                done++;
            }
        }
        finally
        {
            // 멈추면 아직 넣지 않은 링크를 입력란에 되돌려 놓습니다(그사이 입력한 내용은 뒤에 둡니다).
            if (done < links.Count) Editor.Text = string.Join("\n", links.Skip(done).Concat(SourceURL.Lines(Editor.Text ?? "")));
            currentLink = null;
            isRunning = false;
            task = null;
            FailureList.Text = FailureReport;
            Update();
        }
    }

    private void Update()
    {
        Count.Text = L.Plural("{0} links", linkCount);
        DownloadAll.IsEnabled = linkCount > 0 && !isRunning && tools.IsReady;

        Motion.SetVisible(ProgressCard, isRunning);
        ProgressText.Text = L.F("Adding {0} of {1}…", Math.Min(done + 1, total), total);
        Progress.Value = (double)done / Math.Max(total, 1);
        Current.Text = currentLink ?? "";
        ToolTip.SetTip(Current, currentLink);

        Motion.SetVisible(AddedRow, !isRunning && addedCount > 0);
        AddedText.Text = L.Plural("Added {0} downloads", addedCount);
        Motion.SetVisible(FailureCard, !isRunning && failures.Count > 0);
        FailureTitle.Text = L.Plural("{0} links could not be added", failures.Count);
    }
}
