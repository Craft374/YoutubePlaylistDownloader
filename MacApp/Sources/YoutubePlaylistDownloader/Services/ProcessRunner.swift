import Foundation

struct ProcessResult: Sendable {
    let status: Int32
    let stdout: Data
    /// stderr 마지막 몇 줄. 오류 메시지를 보여 줄 때 씁니다.
    let stderrTail: [String]
    let wasCancelled: Bool

    /// yt-dlp가 남긴 마지막 "ERROR:" 줄, 없으면 stderr 마지막 줄.
    var errorMessage: String {
        if let line = stderrTail.last(where: { $0.hasPrefix("ERROR:") }) {
            return String(line.dropFirst("ERROR:".count)).trimmingCharacters(in: .whitespaces)
        }
        return stderrTail.last ?? String(localized: "The process exited with code \(status).")
    }
}

/// 외부 프로그램을 실행하고 stdout/stderr를 줄 단위로 넘겨줍니다.
/// 파이프는 실행 중에 계속 비웁니다(끝난 뒤 읽으면 큰 출력에서 파이프가 꽉 차 멈춥니다).
/// 모든 콜백은 백그라운드 스레드에서 불립니다.
enum ProcessRunner {
    static func run(_ executable: URL,
                    _ arguments: [String],
                    environment: [String: String],
                    collectStdout: Bool = false,
                    onLine: (@Sendable (String) -> Void)? = nil) async throws -> ProcessResult {
        let process = Process()
        process.executableURL = executable
        process.arguments = arguments
        process.environment = environment
        process.standardInput = FileHandle.nullDevice
        let outPipe = Pipe(), errPipe = Pipe()
        process.standardOutput = outPipe
        process.standardError = errPipe

        let state = RunState()
        let out = LineReader(collect: collectStdout) { line in onLine?(line) }
        let err = LineReader(collect: false) { line in
            state.appendStderr(line)
            onLine?(line)
        }

        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<ProcessResult, Error>) in
                state.onFinish = { status in
                    out.flush()
                    err.flush()
                    continuation.resume(returning: ProcessResult(status: status, stdout: out.collected,
                                                                 stderrTail: state.stderrTail, wasCancelled: state.cancelled))
                }
                outPipe.fileHandleForReading.readabilityHandler = { handle in
                    let data = handle.availableData
                    if data.isEmpty { handle.readabilityHandler = nil; state.streamClosed() } else { out.append(data) }
                }
                errPipe.fileHandleForReading.readabilityHandler = { handle in
                    let data = handle.availableData
                    if data.isEmpty { handle.readabilityHandler = nil; state.streamClosed() } else { err.append(data) }
                }
                process.terminationHandler = { p in
                    ProcessRegistry.shared.remove(p)
                    state.terminated(status: p.terminationStatus)
                }
                do {
                    try process.run()
                    ProcessRegistry.shared.add(process)
                    state.started(process)
                } catch {
                    outPipe.fileHandleForReading.readabilityHandler = nil
                    errPipe.fileHandleForReading.readabilityHandler = nil
                    state.onFinish = nil
                    continuation.resume(throwing: error)
                }
            }
        } onCancel: {
            state.cancel()
        }
    }

    /// GUI 앱은 셸 PATH와 로캘을 물려받지 않습니다. 도구 폴더와 UTF-8 로캘을 명시합니다(한글 제목 깨짐 방지).
    static func environment(toolDirectories: [URL]) -> [String: String] {
        let inherited = ProcessInfo.processInfo.environment
        var env: [String: String] = [:]
        // 변수를 추가하면 FilenameBuilder.sanitize의 $ 치환 목록에도 넣어야 합니다(파일 이름 속 $변수가 펼쳐지지 않게).
        for key in ["HOME", "USER", "LOGNAME", "TMPDIR", "SHELL"] {
            if let value = inherited[key] { env[key] = value }
        }
        let dirs = toolDirectories.map(\.path) + ["/usr/bin", "/bin", "/usr/sbin", "/sbin"]
        env["PATH"] = dirs.joined(separator: ":")
        env["LANG"] = "en_US.UTF-8"
        env["LC_ALL"] = "en_US.UTF-8"
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONUTF8"] = "1"
        return env
    }
}

/// 실행 중인 자식 프로세스 목록. 앱 종료 때 한 번에 멈추게 합니다.
final class ProcessRegistry: @unchecked Sendable {
    static let shared = ProcessRegistry()
    private let lock = NSLock()
    private var processes: [ObjectIdentifier: Process] = [:]

    func add(_ p: Process) { lock.withLock { processes[ObjectIdentifier(p)] = p } }
    func remove(_ p: Process) { lock.withLock { processes[ObjectIdentifier(p)] = nil } }

    /// SIGINT를 보내 yt-dlp가 스스로 ffmpeg 자식까지 정리하게 합니다. 기다리지 않고 바로 돌아옵니다.
    func interruptAll() {
        let running = lock.withLock { Array(processes.values) }
        for p in running where p.isRunning { p.interrupt() }
    }
}

/// 프로세스 종료와 두 파이프의 EOF를 모두 본 뒤에 한 번만 끝냅니다.
/// 자식의 자식(ffmpeg 등)이 파이프를 붙잡고 있으면 EOF가 오지 않을 수 있어, 종료 후 2초가 지나면 그냥 끝냅니다.
private final class RunState: @unchecked Sendable {
    private let lock = NSLock()
    private var process: Process?
    private var openStreams = 2
    private var exitStatus: Int32?
    private var finished = false
    private var tail: [String] = []
    private(set) var cancelled = false
    var onFinish: ((Int32) -> Void)?

    var stderrTail: [String] { lock.withLock { tail } }

    func appendStderr(_ line: String) {
        lock.withLock {
            tail.append(line)
            if tail.count > 20 { tail.removeFirst(tail.count - 20) }
        }
    }

    func started(_ p: Process) {
        let shouldCancel = lock.withLock { () -> Bool in
            process = p
            return cancelled
        }
        if shouldCancel { escalate(p) }
    }

    func streamClosed() {
        lock.lock()
        openStreams -= 1
        lock.unlock()
        finishIfReady(force: false)
    }

    func terminated(status: Int32) {
        lock.withLock { exitStatus = status }
        finishIfReady(force: false)
        DispatchQueue.global().asyncAfter(deadline: .now() + 2) { [self] in finishIfReady(force: true) }
    }

    func cancel() {
        let p = lock.withLock { () -> Process? in
            cancelled = true
            return process
        }
        if let p { escalate(p) }
    }

    private func escalate(_ p: Process) {
        guard p.isRunning else { return }
        p.interrupt()
        DispatchQueue.global().asyncAfter(deadline: .now() + 3) {
            if p.isRunning { p.terminate() }
        }
        DispatchQueue.global().asyncAfter(deadline: .now() + 6) {
            if p.isRunning { kill(p.processIdentifier, SIGKILL) }
        }
    }

    private func finishIfReady(force: Bool) {
        lock.lock()
        guard !finished, let status = exitStatus, openStreams <= 0 || force else {
            lock.unlock()
            return
        }
        finished = true
        let callback = onFinish
        onFinish = nil
        lock.unlock()
        callback?(status)
    }
}

/// 바이트를 모아 줄 단위(\n 또는 \r)로 잘라 넘깁니다. yt-dlp 진행률은 \r로 끝나기도 합니다.
private final class LineReader: @unchecked Sendable {
    private let lock = NSLock()
    private var buffer: [UInt8] = []
    private var all = Data()
    private let collect: Bool
    private let onLine: (String) -> Void

    init(collect: Bool, onLine: @escaping (String) -> Void) {
        self.collect = collect
        self.onLine = onLine
    }

    var collected: Data { lock.withLock { all } }

    func append(_ data: Data) {
        lock.lock()
        defer { lock.unlock() }
        if collect {
            all.append(data)
            return
        }
        buffer.append(contentsOf: data)
        var start = 0
        for i in buffer.indices where buffer[i] == 10 || buffer[i] == 13 {
            if i > start { onLine(String(decoding: buffer[start..<i], as: UTF8.self)) }
            start = i + 1
        }
        if start > 0 { buffer.removeFirst(start) }
        // 줄바꿈 없이 끝없이 쌓이는 출력에 대비한 상한.
        if buffer.count > 1 << 20 { buffer.removeAll() }
    }

    func flush() {
        lock.lock()
        defer { lock.unlock() }
        if !buffer.isEmpty {
            onLine(String(decoding: buffer, as: UTF8.self))
            buffer.removeAll()
        }
    }
}
