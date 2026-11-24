using System.Collections.ObjectModel;
using System.ComponentModel;

namespace YPD.Core;

public enum ItemState { Queued, Downloading, Processing, Done, Skipped, Failed, Cancelled }

public static class ItemStateExtensions
{
    public static bool IsRunning(this ItemState state) => state is ItemState.Downloading or ItemState.Processing;
    public static bool IsFinished(this ItemState state) => state is ItemState.Done or ItemState.Skipped or ItemState.Failed or ItemState.Cancelled;
}

public sealed class DownloadItem(MediaEntry entry) : ObservableObject
{
    private ItemState state = ItemState.Queued;
    private string? error;
    private double progress;
    private double? speed;
    private string? filePath;

    public Guid Id { get; } = Guid.NewGuid();
    public MediaEntry Entry { get; } = entry;
    /// <summary>Error와 FilePath를 먼저 바꾼 뒤 바꿉니다. State 변경을 보는 쪽이 새 값을 읽을 수 있습니다.</summary>
    public ItemState State { get => state; internal set => Set(ref state, value); }
    /// <summary>State가 Failed일 때의 이유.</summary>
    public string? Error { get => error; internal set => Set(ref error, value); }
    /// <summary>0~1. 영상·오디오를 따로 받는 경우 합친 값입니다.</summary>
    public double Progress { get => progress; internal set => Set(ref progress, value); }
    /// <summary>바이트/초.</summary>
    public double? Speed { get => speed; internal set => Set(ref speed, value); }
    public string? FilePath { get => filePath; internal set => Set(ref filePath, value); }

    internal CancellationTokenSource? Cancellation;

    /// <summary>목록 행의 화면 읽기 이름이 됩니다(목록 컨테이너는 항목의 ToString을 읽습니다).</summary>
    public override string ToString() => Entry.Title;

    /// <summary>예약해 둔 파일 경로 키(DownloadQueue.ReservationKey).</summary>
    internal string? Reservation;
}

public sealed class DownloadJob : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public Uri? Thumbnail { get; }
    public string Directory { get; }
    public DownloadOptions Options { get; }
    public string? CollectionTitle { get; }
    public string? CollectionUploader { get; }
    public IReadOnlyList<DownloadItem> Items { get; }

    private bool isExpanded;
    /// <summary>목록에서 항목을 펼쳤는지. 화면을 바꿨다 돌아와도 유지됩니다.</summary>
    public bool IsExpanded { get => isExpanded; set => Set(ref isExpanded, value); }

    // 요약 값은 항목이 바뀔 때 Refresh()로 한 번에 다시 계산해 저장합니다(바뀐 값만 알림).
    // 행이 그릴 때마다 수천 개 항목을 훑으면 화면이 버벅입니다.
    private double progress;
    private int completedCount;
    private int failedCount;
    private double? speed;
    private bool isFinished;
    private bool isRunning;
    private bool isCancelled;
    private bool isProcessing;
    private string? currentTitle;

    public double Progress { get => progress; private set => Set(ref progress, value); }
    public int CompletedCount { get => completedCount; private set => Set(ref completedCount, value); }
    public int FailedCount { get => failedCount; private set => Set(ref failedCount, value); }
    public double? Speed { get => speed; private set => Set(ref speed, value); }
    public bool IsFinished { get => isFinished; private set => Set(ref isFinished, value); }
    public bool IsRunning { get => isRunning; private set => Set(ref isRunning, value); }
    public bool IsCancelled { get => isCancelled; private set => Set(ref isCancelled, value); }
    public bool IsProcessing { get => isProcessing; private set => Set(ref isProcessing, value); }
    public string? CurrentTitle { get => currentTitle; private set => Set(ref currentTitle, value); }

    internal bool OpenedFolder;
    /// <summary>다음 대기 항목을 찾을 때 여기서부터 봅니다(앞쪽은 이미 시작했거나 끝난 항목).</summary>
    internal int NextIndex;

    internal DownloadJob(MediaSource source, IEnumerable<MediaEntry> entries, DownloadOptions options, string directory)
    {
        Title = source.Title;
        Thumbnail = source.Thumbnail;
        Directory = directory;
        Options = options;
        CollectionTitle = source.CollectionTitle;
        CollectionUploader = source.Uploader;
        Items = [.. entries.Select(e => new DownloadItem(e))];
        Refresh();
    }

    /// <summary>목록 행의 화면 읽기 이름이 됩니다.</summary>
    public override string ToString() => Title;

    public List<DownloadItem> FailedItems => [.. Items.Where(i => i.State == ItemState.Failed)];

    internal void Refresh()
    {
        double sum = 0, speedSum = 0;
        int completed = 0, failed = 0, finished = 0, downloading = 0, processing = 0, cancelled = 0;
        bool hasSpeed = false;
        string? current = null;
        foreach (var item in Items)
        {
            switch (item.State)
            {
                case ItemState.Done or ItemState.Skipped: completed++; finished++; sum += 1; break;
                case ItemState.Failed: failed++; finished++; sum += 1; break;
                case ItemState.Cancelled: cancelled++; finished++; sum += 1; break;
                case ItemState.Downloading:
                    downloading++;
                    sum += item.Progress;
                    if (item.Speed is { } s) { speedSum += s; hasSpeed = true; }
                    current ??= item.Entry.Title;
                    break;
                case ItemState.Processing:
                    processing++;
                    sum += item.Progress;
                    current ??= item.Entry.Title;
                    break;
            }
        }
        bool done = finished == Items.Count;
        Progress = Items.Count == 0 ? 1 : sum / Items.Count;
        CompletedCount = completed;
        FailedCount = failed;
        Speed = hasSpeed ? speedSum : null;
        IsFinished = done;
        IsRunning = downloading + processing > 0;
        IsCancelled = done && cancelled > 0;
        IsProcessing = processing > 0 && downloading == 0;
        CurrentTitle = current;
    }
}

/// <summary>대기열에 넣을 수 없는 이유. Message를 그대로 사용자에게 보여 줍니다.</summary>
public sealed class EnqueueError(string message) : Exception(message);

/// <summary>
/// 모든 작업의 항목을 순서대로, 설정한 개수만큼 동시에 받습니다. 항목마다 yt-dlp를 하나씩 실행합니다.
/// UI 스레드에서 처음 만들고(SynchronizationContext를 잡아 둡니다) UI 스레드에서만 부릅니다.
/// </summary>
public sealed class DownloadQueue : ObservableObject
{
    private static readonly Lazy<DownloadQueue> shared = new(() => new DownloadQueue());
    public static DownloadQueue Shared => shared.Value;

    /// <summary>최근 작업이 위(0번)에 옵니다.</summary>
    public ObservableCollection<DownloadJob> Jobs { get; } = [];
    private int running;
    /// <summary>진행 중인 항목이 쓸 파일 경로. 작업이 달라도 같은 폴더면 이름이 겹치지 않게 합니다.</summary>
    private readonly HashSet<string> reserved = [];
    private readonly SynchronizationContext context;
    private int activeJobCount;
    private bool hasFinishedJobs;

    /// <summary>끝나지 않은 작업 수(사이드바 배지). 바뀌면 HasActiveWork도 알립니다.</summary>
    public int ActiveJobCount { get => activeJobCount; private set { if (Set(ref activeJobCount, value)) OnPropertyChanged(nameof(HasActiveWork)); } }
    /// <summary>종료 확인에 씁니다. 저장된 값이 아니라 지금 상태를 직접 셉니다.</summary>
    public bool HasActiveWork => Jobs.Any(j => !j.IsFinished);
    /// <summary>"완료 항목 지우기" 버튼용.</summary>
    public bool HasFinishedJobs { get => hasFinishedJobs; private set => Set(ref hasFinishedJobs, value); }

    private static AppSettings Settings => AppSettings.Shared;
    private static ToolManager Tools => ToolManager.Shared;

    /// <summary>경로를 짧게 둡니다(MAX_PATH 260). 한 번에 앱 하나만 실행되므로 시작할 때 통째로 지웁니다.</summary>
    public static string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "YPD");

    private DownloadQueue()
    {
        context = SynchronizationContext.Current ?? new SynchronizationContext();
        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.MaxConcurrentDownloads)) Pump();
        };
    }

    /// <summary>
    /// 옵션과 저장 폴더는 이 시점의 값으로 고정됩니다(옵션은 복사). 일괄 다운로드는 시작할 때 잡아 둔 값을 넘깁니다.
    /// </summary>
    /// <exception cref="EnqueueError">받을 항목이 없거나, 폴더에 쓸 수 없거나, yt-dlp가 없을 때</exception>
    public DownloadJob Enqueue(MediaSource source, DownloadOptions? options = null, string? directory = null)
    {
        if (!Tools.IsReady) throw new EnqueueError(L.T("yt-dlp is not installed. Install it in Settings → Components."));
        options = (options ?? Settings.Options) with { };
        var entries = options.SelectEntries(source);
        if (entries.Count == 0) throw new EnqueueError(L.T("There are no videos to download with the current range and length filter."));

        directory ??= Settings.SaveDirectory;
        if (options.SavePlaylistsInSubfolders && source.CollectionTitle is { } collection)
        {
            string folder = FilenameBuilder.FolderName(collection);
            if (folder.Length > 0) directory = Path.Combine(directory, folder);
        }
        try
        {
            directory = Path.GetFullPath(directory);
            System.IO.Directory.CreateDirectory(directory);
            // Windows에는 "쓸 수 있는지" 묻는 API가 없어 실제로 파일을 만들어 봅니다(만든 파일은 닫을 때 지워집니다).
            using (File.Create(Path.Combine(directory, $".ypd-{Guid.NewGuid():N}.tmp"), 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new EnqueueError(L.F("Cannot write to the save folder \"{0}\": {1}", directory, e.Message));
        }

        var job = new DownloadJob(source, entries, options, directory);
        Jobs.Insert(0, job);
        UpdateActiveCount();
        Pump();
        return job;
    }

    public void Cancel(DownloadJob job)
    {
        // 사용자가 멈춘 작업은 끝나도 폴더를 열지 않습니다(다시 시도하면 되살아납니다).
        job.OpenedFolder = true;
        foreach (var item in job.Items)
        {
            if (item.State == ItemState.Queued) item.State = ItemState.Cancelled;
            else if (item.State.IsRunning()) item.Cancellation?.Cancel();
        }
        Refresh(job);
    }

    public void CancelAll()
    {
        foreach (var job in Jobs.ToList()) Cancel(job);
    }

    public void RetryFailed(DownloadJob job)
    {
        foreach (var item in job.Items)
        {
            if (item.State is ItemState.Failed or ItemState.Cancelled)
            {
                item.Error = null;
                item.Progress = 0;
                item.Speed = null;
                item.State = ItemState.Queued;
            }
        }
        job.OpenedFolder = false;
        job.NextIndex = 0;
        Refresh(job);
        Pump();
    }

    public void Remove(DownloadJob job)
    {
        Cancel(job);
        Jobs.Remove(job);
        UpdateActiveCount();
    }

    public void ClearFinished()
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList()) Jobs.Remove(job);
        UpdateActiveCount();
    }

    /// <summary>
    /// 빈 슬롯만큼 대기 중인 항목을 시작합니다. 위에 있는(최근) 작업이 아니라 먼저 넣은 작업부터 받습니다.
    /// 건너뛴 항목은 슬롯을 쓰지 않고 이 반복문 안에서 바로 다음으로 넘어갑니다(재귀 없음).
    /// </summary>
    public void Pump()
    {
        if (Tools.Paths == null) return;
        List<DownloadJob> touched = [];
        while (running < Settings.MaxConcurrentDownloads && NextQueued() is (DownloadJob job, DownloadItem item))
        {
            Start(item, job);
            if (!touched.Contains(job)) touched.Add(job);
        }
        foreach (var job in touched)
        {
            Refresh(job);
            OpenFolderIfDone(job);
        }
    }

    private (DownloadJob, DownloadItem)? NextQueued()
    {
        for (int j = Jobs.Count - 1; j >= 0; j--)
        {
            var job = Jobs[j];
            while (job.NextIndex < job.Items.Count)
            {
                var item = job.Items[job.NextIndex];
                if (item.State == ItemState.Queued) return (job, item);
                job.NextIndex++;
            }
        }
        return null;
    }

    private void Start(DownloadItem item, DownloadJob job)
    {
        if (Tools.Paths is not { } paths) return;
        if (UniqueName(item, job) is not { } name)
        {
            item.State = ItemState.Skipped;
            return;
        }
        running++;
        item.Progress = 0;
        item.Speed = null;
        item.State = ItemState.Downloading;
        string key = ReservationKey(job.Directory, name);
        item.Reservation = key;
        reserved.Add(key);
        var cancellation = new CancellationTokenSource();
        item.Cancellation = cancellation;
        _ = RunAsync(item, job, paths, name, cancellation.Token);
    }

    private async Task RunAsync(DownloadItem item, DownloadJob job, ToolPaths paths, string name, CancellationToken token)
    {
        // 시작한 쪽(Pump)이 끝난 뒤에 이어 갑니다. 곧바로 실패하는 항목이 Finish → Pump를 재귀로 부르지 않게 합니다.
        await Task.Yield();
        string temp = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
        ItemState outcome;
        string? error = null, file = null;
        try
        {
            var tags = job.Options.Mode == MediaMode.Video ? null
                : AudioTags.Make(item.Entry, job.CollectionTitle, job.CollectionUploader);
            // Windows에서는 AV1 하드웨어 디코딩을 기대하지 않습니다. MP4는 어디서나 열리는 H.264/AAC(최대 1080p)입니다.
            var request = new DownloadRequest(item.Entry.Url, job.Options, job.Directory, name, temp, tags, HardwareAV1: false);
            var arguments = YTDLPArguments.Download(request, paths);
            var sink = new ProgressSink(context, (progress, speed, processing) =>
            {
                if (!item.State.IsRunning()) return;
                item.Progress = progress;
                item.Speed = speed;
                item.State = processing ? ItemState.Processing : ItemState.Downloading;
                job.Refresh();
            });

            System.IO.Directory.CreateDirectory(temp);
            var result = await ProcessRunner.RunAsync(paths.Ytdlp, arguments, Tools.Environment, onLine: sink.Handle, cancellationToken: token);
            file = sink.File;
            // 성공 판정을 먼저 합니다. 끝난 직후에 누른 중지 때문에 저장된 파일이 '취소됨'이 되지 않게 합니다.
            if (result.Status == 0) outcome = ItemState.Done;
            else if (result.WasCancelled || token.IsCancellationRequested) outcome = ItemState.Cancelled;
            else
            {
                outcome = ItemState.Failed;
                error = sink.LastError ?? result.ErrorMessage;
            }
        }
        catch (Exception e)
        {
            // 여기서 예외가 새면 슬롯이 반환되지 않아 대기열이 멈춥니다. 모든 예외를 실패로 바꿉니다.
            outcome = token.IsCancellationRequested ? ItemState.Cancelled : ItemState.Failed;
            if (outcome == ItemState.Failed) error = e.Message;
        }
        _ = Task.Run(() => DeleteDirectory(temp));
        try
        {
            item.FilePath = file;
            item.Error = error;
            if (outcome == ItemState.Done) item.Progress = 1;
            item.Speed = null;
            item.State = outcome;
        }
        finally
        {
            // 알림을 받은 화면 코드가 예외를 던져도 슬롯은 돌려줍니다.
            Finish(item, job);
        }
    }

    private void Finish(DownloadItem item, DownloadJob job)
    {
        running--;
        item.Cancellation?.Dispose();
        item.Cancellation = null;
        if (item.Reservation is { } key)
        {
            reserved.Remove(key);
            item.Reservation = null;
        }
        Refresh(job);
        OpenFolderIfDone(job);
        Pump();
    }

    private void Refresh(DownloadJob job)
    {
        job.Refresh();
        UpdateActiveCount();
    }

    private void UpdateActiveCount()
    {
        ActiveJobCount = Jobs.Count(j => !j.IsFinished);
        HasFinishedJobs = Jobs.Count > ActiveJobCount;
    }

    private static void OpenFolderIfDone(DownloadJob job)
    {
        if (!job.IsFinished || !job.Options.OpenFolderWhenDone || job.OpenedFolder
            || !job.Items.Any(i => i.State == ItemState.Done)) return;
        job.OpenedFolder = true;
        Explorer.OpenFolder(job.Directory);
    }

    internal static void DeleteDirectory(string path)
    {
        try
        {
            if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 다음 실행 때 임시 폴더를 통째로 지웁니다.
        }
    }

    /// <summary>NTFS는 대소문자를 구분하지 않으므로 키도 소문자로 맞춥니다.</summary>
    private static string ReservationKey(string directory, string name) =>
        Path.Join(Path.TrimEndingDirectorySeparator(directory), name).ToLowerInvariant();

    /// <summary>겹치지 않는 파일 이름. "이미 있는 파일 건너뛰기"가 켜져 있고 파일이 있으면 null(건너뜀).</summary>
    private string? UniqueName(DownloadItem item, DownloadJob job)
    {
        string baseName = FilenameBuilder.BaseName(job.Options.FilenamePattern, item.Entry, job.CollectionTitle);
        var expected = YTDLPArguments.ExpectedExtensions(job.Options);
        // 오디오 변환 때 yt-dlp는 폴더의 "이름.<원본 확장자>"를 이미 받은 파일로 보고 변환한 뒤 지웁니다.
        // 그런 파일이 있으면 다른 이름을 써서 기존 파일을 건드리지 않게 합니다.
        List<string> blocking = job.Options.Mode == MediaMode.Audio ? [.. expected, "webm", "m4a", "mp4", "opus"] : expected;
        return OutputName.Choose(baseName, expected, blocking, job.Options.SkipExisting,
            isReserved: n => reserved.Contains(ReservationKey(job.Directory, n)),
            exists: (n, ext) => File.Exists(Path.Combine(job.Directory, $"{n}.{ext}")));
    }
}

/// <summary>
/// yt-dlp 출력 줄을 받아 진행률을 계산하고, 화면 갱신은 초당 최대 10번으로 묶어서 UI 스레드에 보냅니다.
/// 진행률 줄마다 화면을 다시 그리면 항목이 많을 때 UI가 버벅입니다.
/// </summary>
internal sealed class ProgressSink(SynchronizationContext context, Action<double, double?, bool> apply)
{
    private readonly Lock gate = new();
    private readonly ProgressTracker tracker = new();
    private double progress;
    private double? speed;
    private bool processing;
    private bool scheduled;
    private string? file;
    private string? lastError;

    public string? File { get { lock (gate) return file; } }
    public string? LastError { get { lock (gate) return lastError; } }

    public void Handle(string line)
    {
        if (YTDLPEvent.Parse(line) is not { } e) return;
        bool needsSchedule;
        lock (gate)
        {
            switch (e)
            {
                case YTDLPEvent.Download d:
                    if (d.Fraction is { } fraction) progress = tracker.Update(d.Kind, fraction);
                    speed = d.Speed;
                    processing = false;
                    break;
                case YTDLPEvent.Postprocess { Started: true }:
                    processing = true;
                    speed = null;
                    break;
                case YTDLPEvent.File f:
                    file = f.Path;
                    break;
                case YTDLPEvent.Error error:
                    lastError = error.Message;
                    break;
            }
            needsSchedule = !scheduled;
            scheduled = true;
        }
        if (needsSchedule) _ = ApplyLaterAsync();
    }

    private async Task ApplyLaterAsync()
    {
        await Task.Delay(100).ConfigureAwait(false);
        context.Post(_ =>
        {
            double p;
            double? s;
            bool processingNow;
            lock (gate)
            {
                scheduled = false;
                (p, s, processingNow) = (progress, speed, processing);
            }
            apply(p, s, processingNow);
        }, null);
    }
}
