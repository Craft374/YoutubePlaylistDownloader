import AppKit
import Foundation
import Observation
import YPDCore

@MainActor @Observable
final class DownloadItem: Identifiable {
    enum State: Equatable {
        case queued, downloading, processing, done, skipped, failed(String), cancelled

        var isRunning: Bool { self == .downloading || self == .processing }
        var isFinished: Bool {
            switch self {
            case .done, .skipped, .failed, .cancelled: return true
            default: return false
            }
        }
    }

    nonisolated let id = UUID()
    let entry: MediaEntry
    var state: State = .queued
    /// 0~1. 영상·오디오를 따로 받는 경우 합친 값입니다.
    var progress = 0.0
    /// 바이트/초.
    var speed: Double?
    var fileURL: URL?
    @ObservationIgnored fileprivate var task: Task<Void, Never>?
    /// 예약해 둔 파일 경로 키(DownloadQueue.reservationKey).
    @ObservationIgnored fileprivate var reservation: String?

    init(entry: MediaEntry) { self.entry = entry }
}

@MainActor @Observable
final class DownloadJob: Identifiable {
    nonisolated let id = UUID()
    let title: String
    let thumbnail: URL?
    let directory: URL
    let options: DownloadOptions
    let collectionTitle: String?
    let collectionUploader: String?
    let items: [DownloadItem]
    /// 목록에서 항목을 펼쳤는지. 화면을 바꿨다 돌아와도 유지됩니다.
    var isExpanded = false

    // 요약 값은 항목이 바뀔 때 refresh()로 한 번에 다시 계산해 저장합니다.
    // 행이 그릴 때마다 수천 개 항목을 훑고 관찰을 등록하면 120Hz에서 프레임이 밀립니다.
    private(set) var progress = 0.0
    private(set) var completedCount = 0
    private(set) var failedCount = 0
    private(set) var speed: Double?
    private(set) var isFinished = false
    private(set) var isRunning = false
    private(set) var isCancelled = false
    private(set) var isProcessing = false
    private(set) var currentTitle: String?

    @ObservationIgnored fileprivate var openedFolder = false
    /// 다음 대기 항목을 찾을 때 여기서부터 봅니다(앞쪽은 이미 시작했거나 끝난 항목).
    @ObservationIgnored fileprivate var nextIndex = 0

    init(source: MediaSource, entries: [MediaEntry], options: DownloadOptions, directory: URL) {
        title = source.title
        thumbnail = source.thumbnail
        self.directory = directory
        self.options = options
        collectionTitle = source.collectionTitle
        collectionUploader = source.uploader
        items = entries.map(DownloadItem.init)
        refresh()
    }

    var failedItems: [DownloadItem] { items.filter { if case .failed = $0.state { return true } else { return false } } }

    fileprivate func refresh() {
        var sum = 0.0, completed = 0, failed = 0, finished = 0, downloading = 0, processing = 0, cancelled = 0
        var speedSum = 0.0, hasSpeed = false
        var current: String?
        for item in items {
            switch item.state {
            case .done, .skipped: completed += 1; finished += 1; sum += 1
            case .failed: failed += 1; finished += 1; sum += 1
            case .cancelled: cancelled += 1; finished += 1; sum += 1
            case .downloading:
                downloading += 1
                sum += item.progress
                if let s = item.speed { speedSum += s; hasSpeed = true }
                if current == nil { current = item.entry.title }
            case .processing:
                processing += 1
                sum += item.progress
                if current == nil { current = item.entry.title }
            case .queued: break
            }
        }
        let done = finished == items.count
        update(\.progress, items.isEmpty ? 1 : sum / Double(items.count))
        update(\.completedCount, completed)
        update(\.failedCount, failed)
        update(\.speed, hasSpeed ? speedSum : nil)
        update(\.isFinished, done)
        update(\.isRunning, downloading + processing > 0)
        update(\.isCancelled, done && cancelled > 0)
        update(\.isProcessing, processing > 0 && downloading == 0)
        update(\.currentTitle, current)
    }

    /// 값이 같으면 쓰지 않습니다. @Observable은 같은 값을 써도 화면을 다시 그리게 합니다.
    private func update<T: Equatable>(_ keyPath: ReferenceWritableKeyPath<DownloadJob, T>, _ value: T) {
        if self[keyPath: keyPath] != value { self[keyPath: keyPath] = value }
    }
}

/// 모든 작업의 항목을 순서대로, 설정한 개수만큼 동시에 받습니다. 항목마다 yt-dlp를 하나씩 실행합니다.
@MainActor @Observable
final class DownloadQueue {
    static let shared = DownloadQueue()

    private(set) var jobs: [DownloadJob] = []
    private var running = 0
    /// 진행 중인 항목이 쓸 파일 경로. 작업이 달라도 같은 폴더면 이름이 겹치지 않게 합니다.
    @ObservationIgnored private var reserved = Set<String>()

    var hasActiveWork: Bool { jobs.contains { !$0.isFinished } }
    var activeJobCount: Int { jobs.filter { !$0.isFinished }.count }

    private var settings: AppSettings { AppSettings.shared }
    private var tools: ToolManager { ToolManager.shared }

    nonisolated static let tempRoot = FileManager.default.temporaryDirectory.appendingPathComponent("YoutubePlaylistDownloader", isDirectory: true)

    enum EnqueueError: LocalizedError {
        case nothingToDownload
        case folderNotWritable(String, String)
        case toolsMissing

        var errorDescription: String? {
            switch self {
            case .nothingToDownload:
                return String(localized: "There are no videos to download with the current range and length filter.")
            case let .folderNotWritable(path, reason):
                return String(localized: "Cannot write to the save folder \"\(path)\": \(reason)")
            case .toolsMissing:
                return String(localized: "yt-dlp is not installed. Install it in Settings → Components.")
            }
        }
    }

    /// 옵션과 저장 폴더는 이 시점의 값으로 고정됩니다. 일괄 다운로드는 시작할 때 잡아 둔 값을 넘깁니다.
    @discardableResult
    func enqueue(_ source: MediaSource, options: DownloadOptions? = nil, directory: URL? = nil) throws -> DownloadJob {
        guard tools.isReady else { throw EnqueueError.toolsMissing }
        let options = options ?? settings.options
        let entries = options.selectEntries(from: source)
        guard !entries.isEmpty else { throw EnqueueError.nothingToDownload }

        var directory = directory ?? settings.saveDirectory
        if options.savePlaylistsInSubfolders, let collection = source.collectionTitle {
            let folder = FilenameBuilder.sanitize(collection)
            if !folder.isEmpty { directory.appendPathComponent(folder, isDirectory: true) }
        }
        do {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            guard FileManager.default.isWritableFile(atPath: directory.path) else {
                throw CocoaError(.fileWriteNoPermission)
            }
        } catch {
            throw EnqueueError.folderNotWritable(directory.path, error.localizedDescription)
        }

        let job = DownloadJob(source: source, entries: entries, options: options, directory: directory)
        jobs.insert(job, at: 0)
        pump()
        return job
    }

    func cancel(_ job: DownloadJob) {
        // 사용자가 멈춘 작업은 끝나도 Finder를 열지 않습니다(다시 시도하면 되살아납니다).
        job.openedFolder = true
        for item in job.items {
            switch item.state {
            case .queued: item.state = .cancelled
            case .downloading, .processing: item.task?.cancel()
            default: break
            }
        }
        job.refresh()
    }

    func cancelAll() { jobs.forEach(cancel) }

    func retryFailed(_ job: DownloadJob) {
        for item in job.items {
            switch item.state {
            case .failed, .cancelled:
                item.state = .queued
                item.progress = 0
                item.speed = nil
            default: break
            }
        }
        job.openedFolder = false
        job.nextIndex = 0
        job.refresh()
        pump()
    }

    func remove(_ job: DownloadJob) {
        cancel(job)
        jobs.removeAll { $0 === job }
    }

    func clearFinished() {
        jobs.removeAll { $0.isFinished }
    }

    /// 빈 슬롯만큼 대기 중인 항목을 시작합니다. 위에 있는(최근) 작업이 아니라 먼저 넣은 작업부터 받습니다.
    /// 건너뛴 항목은 슬롯을 쓰지 않고 이 반복문 안에서 바로 다음으로 넘어갑니다(재귀 없음).
    func pump() {
        guard tools.paths != nil else { return }
        var touched: [DownloadJob] = []
        while running < settings.maxConcurrentDownloads, let (job, item) = nextQueued() {
            start(item, in: job)
            if !touched.contains(where: { $0 === job }) { touched.append(job) }
        }
        for job in touched {
            job.refresh()
            openFolderIfDone(job)
        }
    }

    private func nextQueued() -> (DownloadJob, DownloadItem)? {
        for job in jobs.reversed() {
            while job.nextIndex < job.items.count {
                let item = job.items[job.nextIndex]
                if item.state == .queued { return (job, item) }
                job.nextIndex += 1
            }
        }
        return nil
    }

    private func start(_ item: DownloadItem, in job: DownloadJob) {
        guard let paths = tools.paths else { return }
        guard let name = uniqueName(for: item, in: job) else {
            item.state = .skipped
            return
        }
        running += 1
        item.state = .downloading
        item.progress = 0
        item.speed = nil
        let key = Self.reservationKey(job.directory, name)
        item.reservation = key
        reserved.insert(key)

        let temp = Self.tempRoot.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let tags = job.options.mode == .video ? nil
            : AudioTags.make(entry: item.entry, collectionTitle: job.collectionTitle, collectionUploader: job.collectionUploader)
        let request = DownloadRequest(url: item.entry.url, options: job.options, outputDirectory: job.directory,
                                      baseName: name, tempDirectory: temp, tags: tags)
        let arguments = YTDLPArguments.download(request, tools: paths)
        let environment = tools.environment
        let sink = ProgressSink { [weak item, weak job] progress, speed, processing in
            guard let item, item.state.isRunning else { return }
            let state: DownloadItem.State = processing ? .processing : .downloading
            if item.state != state { item.state = state }
            item.progress = progress
            item.speed = speed
            job?.refresh()
        }

        item.task = Task { [weak self] in
            var outcome: DownloadItem.State
            var file: URL?
            do {
                try FileManager.default.createDirectory(at: temp, withIntermediateDirectories: true)
                let result = try await ProcessRunner.run(paths.ytdlp, arguments, environment: environment) { line in
                    sink.handle(line)
                }
                file = sink.file.map { URL(fileURLWithPath: $0) }
                // 성공 판정을 먼저 합니다. 끝난 직후에 누른 중지 때문에 저장된 파일이 '취소됨'이 되지 않게 합니다.
                if result.status == 0 {
                    outcome = .done
                } else if result.wasCancelled || Task.isCancelled {
                    outcome = .cancelled
                } else {
                    outcome = .failed(sink.lastError ?? result.errorMessage)
                }
            } catch {
                outcome = Task.isCancelled ? .cancelled : .failed(error.localizedDescription)
            }
            Task.detached(priority: .background) { try? FileManager.default.removeItem(at: temp) }
            guard let self else { return }
            item.fileURL = file
            item.state = outcome
            if outcome == .done { item.progress = 1 }
            item.speed = nil
            self.finish(item, in: job)
        }
    }

    private func finish(_ item: DownloadItem, in job: DownloadJob) {
        running -= 1
        item.task = nil
        if let key = item.reservation {
            reserved.remove(key)
            item.reservation = nil
        }
        job.refresh()
        openFolderIfDone(job)
        pump()
    }

    private func openFolderIfDone(_ job: DownloadJob) {
        guard job.isFinished, job.options.openFolderWhenDone, !job.openedFolder,
              job.items.contains(where: { $0.state == .done }) else { return }
        job.openedFolder = true
        NSWorkspace.shared.open(job.directory)
    }

    /// APFS는 기본적으로 대소문자를 구분하지 않으므로 키도 소문자로 맞춥니다.
    private static func reservationKey(_ directory: URL, _ name: String) -> String {
        directory.path + "/" + name.lowercased()
    }

    /// 겹치지 않는 파일 이름. "이미 있는 파일 건너뛰기"가 켜져 있고 파일이 있으면 nil(건너뜀).
    private func uniqueName(for item: DownloadItem, in job: DownloadJob) -> String? {
        let base = FilenameBuilder.baseName(pattern: job.options.filenamePattern, entry: item.entry,
                                            collectionTitle: job.collectionTitle)
        let expected = YTDLPArguments.expectedExtensions(job.options)
        // 오디오 변환 때 yt-dlp는 폴더의 "이름.<원본 확장자>"를 이미 받은 파일로 보고 변환한 뒤 지웁니다.
        // 그런 파일이 있으면 다른 이름을 써서 기존 파일을 건드리지 않게 합니다.
        let blocking = job.options.mode == .audio ? expected + ["webm", "m4a", "mp4", "opus"] : expected
        return OutputName.choose(
            base: base, expected: expected, blocking: blocking, skipExisting: job.options.skipExisting,
            isReserved: { reserved.contains(Self.reservationKey(job.directory, $0)) },
            exists: { FileManager.default.fileExists(atPath: job.directory.appendingPathComponent("\($0).\($1)").path) })
    }
}

/// yt-dlp 출력 줄을 받아 진행률을 계산하고, 화면 갱신은 초당 최대 10번으로 묶어서 메인 스레드에 보냅니다.
/// 진행률 줄마다 화면을 다시 그리면 항목이 많을 때 UI가 버벅입니다.
private final class ProgressSink: @unchecked Sendable {
    private let lock = NSLock()
    private var tracker = ProgressTracker()
    private var progress = 0.0
    private var speed: Double?
    private var processing = false
    private var scheduled = false
    private var _file: String?
    private var _lastError: String?
    private let apply: @MainActor (Double, Double?, Bool) -> Void

    init(apply: @escaping @MainActor (Double, Double?, Bool) -> Void) { self.apply = apply }

    var file: String? { lock.withLock { _file } }
    var lastError: String? { lock.withLock { _lastError } }

    func handle(_ line: String) {
        guard let event = YTDLPEvent.parse(line) else { return }
        lock.lock()
        switch event {
        case let .download(kind, fraction, newSpeed):
            if let fraction { progress = tracker.update(kind: kind, fraction: fraction) }
            speed = newSpeed
            processing = false
        case let .postprocess(_, started):
            if started {
                processing = true
                speed = nil
            }
        case let .file(path):
            _file = path
        case let .error(message):
            _lastError = message
        }
        let needsSchedule = !scheduled
        scheduled = true
        lock.unlock()

        if needsSchedule {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.1) { [self] in
                lock.lock()
                scheduled = false
                let values = (progress, speed, processing)
                lock.unlock()
                MainActor.assumeIsolated { apply(values.0, values.1, values.2) }
            }
        }
    }
}
