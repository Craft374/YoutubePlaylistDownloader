using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YPD.Core;

namespace YPD.App;

/// <summary>
/// 여러 링크를 한 번에 대기열에 넣습니다(Mac BulkDownloadView). 화면은 한 번만 만들어 계속 쓰므로,
/// 다른 화면으로 가도 진행 중인 추가 작업은 멈추지 않습니다.
/// </summary>
public sealed partial class BulkDownloadPage : UserControl
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

    private readonly TextBox editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 220,
        MaxHeight = 420,
        PlaceholderText = "https://www.youtube.com/playlist?list=…",
    };
    private readonly TextBlock count = Ui.Caption();
    private readonly Button downloadAll = new() { Content = L.T("Download All"), HorizontalAlignment = HorizontalAlignment.Right };
    // 진행
    private readonly Border progressCard;
    private readonly TextBlock progressText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar progress = new() { Maximum = 1 };
    private readonly TextBlock current = new()
    {
        Style = Ui.Style("YpdCaption"),
        FontFamily = Ui.Monospace,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    // 결과
    private readonly Grid addedRow = new();
    private readonly TextBlock addedText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border failureCard;
    private readonly TextBlock failureTitle = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    /// <summary>링크가 수천 개여도 가볍도록 실패 목록은 읽기 전용 입력란 하나에 줄로 넣습니다(선택해 복사할 수 있습니다).</summary>
    private readonly TextBox failureList = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 240,
        FontFamily = Ui.Monospace,
        FontSize = 12,
    };

    public BulkDownloadPage()
    {
        editor.FontFamily = Ui.Monospace;
        ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(failureList, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, L.T("Paste one link per line. Each link becomes its own download"));
        editor.TextChanged += (_, _) =>
        {
            linkCount = SourceURL.Lines(editor.Text).Count;
            Update();
        };
        downloadAll.Style = Ui.Style("AccentButtonStyle");
        downloadAll.Click += (_, _) => StartFromText();

        var countRow = new Grid();
        count.VerticalAlignment = VerticalAlignment.Center;
        countRow.Children.Add(count);
        countRow.Children.Add(downloadAll);

        var stop = new Button { Content = L.T("Stop"), HorizontalAlignment = HorizontalAlignment.Right };
        stop.Click += (_, _) => task?.Cancel();
        var progressHeader = new Grid();
        progressHeader.Children.Add(progressText);
        progressHeader.Children.Add(stop);
        progressCard = Ui.Card(Ui.Column(8, progressHeader, progress, current));

        var showDownloads = new Button { Content = L.T("Show Downloads"), HorizontalAlignment = HorizontalAlignment.Right };
        showDownloads.Click += (_, _) => MainWindow.Instance?.Navigate(Screen.Queue);
        addedRow.Children.Add(Ui.Row(8, Ui.Icon("", 16, "YpdSuccessIcon"), addedText));
        addedRow.Children.Add(showDownloads);

        var retry = new Button { Content = L.T("Retry") };
        retry.Click += (_, _) => Run([.. failures.Select(f => f.Link)]);
        var copy = new Button { Content = L.T("Copy") };
        copy.Click += (_, _) => Ui.CopyText(FailureReport);
        var save = new Button { Content = L.T("Save…") };
        save.Click += (_, _) => _ = Ui.SaveTextAsync(save, L.T("Links not added.txt"), FailureReport);
        var dismiss = new Button { Content = L.T("Dismiss"), HorizontalAlignment = HorizontalAlignment.Right };
        dismiss.Click += (_, _) =>
        {
            failures = [];
            Update();
        };
        var failureButtons = new Grid();
        failureButtons.Children.Add(Ui.Row(8, retry, copy, save));
        failureButtons.Children.Add(dismiss);
        failureCard = Ui.Card(Ui.Column(Design.Spacing,
            Ui.Row(8, Ui.Icon("", 16, "YpdCautionIcon"), failureTitle), failureList, failureButtons));

        var subtitle = Ui.Text(L.T("Paste one link per line. Each link becomes its own download"), "YpdSecondaryText");
        subtitle.TextAlignment = TextAlignment.Center;
        var stack = Ui.Column(Design.Spacing * 2, subtitle, Ui.Column(Design.Spacing, editor, countRow),
                              progressCard, addedRow, failureCard);
        stack.Padding = new Thickness(24, 24, 24, 24);
        Content = Ui.CenteredScroll(stack, Design.ContentWidth);

        Loaded += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            tools.PropertyChanged += OnToolsChanged;
            Update();
        };
        Unloaded += (_, _) => tools.PropertyChanged -= OnToolsChanged;
        Update();
    }

    private string FailureReport => string.Join(Environment.NewLine, failures.Select(f => $"{f.Link} — {f.Reason}"));

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void StartFromText()
    {
        var links = SourceURL.Lines(editor.Text);
        if (links.Count == 0) return;
        editor.Text = "";
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
            if (done < links.Count) editor.Text = string.Join("\r", links.Skip(done).Concat(SourceURL.Lines(editor.Text)));
            currentLink = null;
            isRunning = false;
            task = null;
            failureList.Text = FailureReport;
            Update();
        }
    }

    private void Update()
    {
        count.Text = L.Plural("{0} links", linkCount);
        downloadAll.IsEnabled = linkCount > 0 && !isRunning && tools.IsReady;

        progressCard.Visibility = Ui.Visible(isRunning);
        progressText.Text = L.F("Adding {0} of {1}…", Math.Min(done + 1, total), total);
        progress.Value = (double)done / Math.Max(total, 1);
        current.Text = currentLink ?? "";
        ToolTipService.SetToolTip(current, currentLink);

        addedRow.Visibility = Ui.Visible(!isRunning && addedCount > 0);
        addedText.Text = L.Plural("Added {0} downloads", addedCount);
        failureCard.Visibility = Ui.Visible(!isRunning && failures.Count > 0);
        failureTitle.Text = L.Plural("{0} links could not be added", failures.Count);
    }
}
