import Foundation
import Observation
import YPDCore

/// yt-dlp, ffmpeg, JavaScript 런타임(deno/node)의 위치를 찾고 yt-dlp를 최신으로 유지합니다.
/// 찾는 순서: 앱이 업데이트한 yt-dlp → 앱 번들에 들어 있는 도구 → Homebrew 경로.
@MainActor @Observable
final class ToolManager {
    static let shared = ToolManager()

    private(set) var paths: ToolPaths?
    private(set) var ytdlpVersion: String?
    private(set) var hasFFmpeg = false
    private(set) var jsRuntimeName: String?
    private(set) var isChecking = true
    private(set) var isUpdating = false
    /// 마지막 업데이트 결과(성공·실패 문구). 설정 화면에 보여 줍니다.
    private(set) var updateStatus: String?

    var isReady: Bool { paths != nil }

    nonisolated private static var fm: FileManager { .default }
    nonisolated static let supportDirectory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return base.appendingPathComponent("YoutubePlaylistDownloader", isDirectory: true)
    }()
    nonisolated static var managedYTDLPRoot: URL { supportDirectory.appendingPathComponent("yt-dlp", isDirectory: true) }
    nonisolated static var bundledToolsDirectory: URL? { Bundle.main.resourceURL?.appendingPathComponent("tools", isDirectory: true) }
    nonisolated static let systemDirectories = ["/opt/homebrew/bin", "/usr/local/bin"].map { URL(fileURLWithPath: $0, isDirectory: true) }

    /// 자식 프로세스 PATH에 넣을 폴더(ffprobe 등 yt-dlp가 이름으로 찾는 도구용).
    var toolDirectories: [URL] {
        var dirs: [URL] = []
        if let ffmpeg = paths?.ffmpeg { dirs.append(ffmpeg.deletingLastPathComponent()) }
        if let js = paths?.jsRuntime { dirs.append(js.path.deletingLastPathComponent()) }
        return dirs + Self.systemDirectories
    }

    var environment: [String: String] { ProcessRunner.environment(toolDirectories: toolDirectories) }

    /// 실행 파일 위치를 다시 찾고 yt-dlp 버전을 읽습니다. 디스크만 보므로 빠르지만 메인 스레드 밖에서 합니다.
    func refresh() async {
        isChecking = true
        let found = await Task.detached(priority: .userInitiated) { Self.locate() }.value
        paths = found.paths
        hasFFmpeg = found.paths?.ffmpeg != nil
        jsRuntimeName = found.paths?.jsRuntime?.name
        if let paths = found.paths {
            ytdlpVersion = await Self.readVersion(paths.ytdlp, environment: environment)
        } else {
            ytdlpVersion = nil
        }
        isChecking = false
    }

    /// 하루에 한 번 조용히 yt-dlp 업데이트를 확인합니다. 유튜브 쪽 변경으로 다운로드가 깨지는 일을 줄입니다.
    func autoUpdateIfNeeded(settings: AppSettings) async {
        guard settings.autoUpdateYTDLP, Date().timeIntervalSince(settings.lastYTDLPCheck) > 24 * 3600 else { return }
        try? await Task.sleep(for: .seconds(3)) // 시작 직후의 다른 작업과 겹치지 않게 조금 늦춥니다.
        await updateYTDLP(settings: settings, quiet: true)
    }

    func updateYTDLP(settings: AppSettings, quiet: Bool = false) async {
        guard !isUpdating else { return }
        isUpdating = true
        defer { isUpdating = false }
        if !quiet { updateStatus = String(localized: "Checking for updates…") }
        do {
            let latest = try await Self.latestYTDLPTag()
            settings.lastYTDLPCheck = Date()
            if let current = ytdlpVersion, current == latest {
                if !quiet { updateStatus = String(localized: "yt-dlp is up to date (\(latest)).") }
                return
            }
            if !quiet { updateStatus = String(localized: "Downloading yt-dlp \(latest)…") }
            try await Self.install(tag: latest)
            await refresh()
            updateStatus = String(localized: "yt-dlp was updated to \(latest).")
        } catch {
            if !quiet || paths == nil {
                updateStatus = String(localized: "Update failed: \(error.localizedDescription)")
            }
        }
    }

    // MARK: - 찾기

    private struct Located: Sendable { let paths: ToolPaths? }

    nonisolated private static func locate() -> Located {
        let bundled = bundledToolsDirectory
        let bundledYTDLP = executable(bundled?.appendingPathComponent("yt-dlp/yt-dlp_macos"))
        // 업데이트한 버전이 번들 버전보다 새로울 때만 씁니다. 앱을 새로 설치하면 번들이 더 새로울 수 있습니다.
        var managed = newestManagedYTDLP()
        if let m = managed, bundledYTDLP != nil, let bundledTag = bundledYTDLPVersion(),
           m.deletingLastPathComponent().lastPathComponent.compare(bundledTag, options: .numeric) != .orderedDescending {
            managed = nil
        }
        let ytdlp = managed ?? bundledYTDLP
            ?? systemDirectories.lazy.compactMap { executable($0.appendingPathComponent("yt-dlp")) }.first
        guard let ytdlp else { return Located(paths: nil) }

        func find(_ name: String) -> URL? {
            executable(bundled?.appendingPathComponent(name))
                ?? systemDirectories.lazy.compactMap { executable($0.appendingPathComponent(name)) }.first
        }
        var js: (String, URL)?
        if let deno = find("deno") { js = ("deno", deno) } else if let node = find("node") { js = ("node", node) }
        return Located(paths: ToolPaths(ytdlp: ytdlp, ffmpeg: find("ffmpeg"), jsRuntime: js))
    }

    /// 빌드 스크립트가 번들 yt-dlp 옆에 남긴 버전(태그).
    nonisolated private static func bundledYTDLPVersion() -> String? {
        guard let url = bundledToolsDirectory?.appendingPathComponent("yt-dlp/VERSION"),
              let text = try? String(contentsOf: url, encoding: .utf8) else { return nil }
        let tag = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return tag.isEmpty ? nil : tag
    }

    nonisolated private static func executable(_ url: URL?) -> URL? {
        guard let url, fm.isExecutableFile(atPath: url.path) else { return nil }
        return url
    }

    /// 업데이트한 yt-dlp는 버전별 폴더에 둡니다. 실행 중인 다운로드가 쓰는 파일을 지우지 않기 위해서입니다.
    nonisolated private static func newestManagedYTDLP() -> URL? {
        guard let dirs = try? fm.contentsOfDirectory(at: managedYTDLPRoot, includingPropertiesForKeys: nil) else { return nil }
        return dirs.filter { !$0.lastPathComponent.hasPrefix(".") }
            .sorted { $0.lastPathComponent.compare($1.lastPathComponent, options: .numeric) == .orderedDescending }
            .lazy.compactMap { executable($0.appendingPathComponent("yt-dlp_macos")) }.first
    }

    /// 지난 실행에서 남은 오래된 버전 폴더를 지웁니다. 아무것도 실행 중이지 않을 때(앱 시작 시)만 부릅니다.
    nonisolated static func removeOldManagedVersions() {
        guard let newest = newestManagedYTDLP()?.deletingLastPathComponent(),
              let dirs = try? fm.contentsOfDirectory(at: managedYTDLPRoot, includingPropertiesForKeys: nil) else { return }
        for dir in dirs where dir.standardizedFileURL != newest.standardizedFileURL {
            try? fm.removeItem(at: dir)
        }
    }

    nonisolated private static func readVersion(_ ytdlp: URL, environment: [String: String]) async -> String? {
        guard let result = try? await ProcessRunner.run(ytdlp, ["--version"], environment: environment, collectStdout: true),
              result.status == 0 else { return nil }
        let text = String(decoding: result.stdout, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
        return text.isEmpty ? nil : text
    }

    // MARK: - 업데이트

    nonisolated private static func latestYTDLPTag() async throws -> String {
        var request = URLRequest(url: URL(string: "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest")!)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = 20
        let (data, response) = try await URLSession.shared.data(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200,
              let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              !tag.isEmpty, !tag.contains("/"), !tag.contains("..")
        else { throw URLError(.badServerResponse) }
        return tag
    }

    nonisolated private static func install(tag: String) async throws {
        // 같은 버전이 이미 있으면 다시 받지 않습니다. 실행 중인 다운로드가 쓰는 폴더를 지우지 않기 위해서이기도 합니다.
        let target = managedYTDLPRoot.appendingPathComponent(tag, isDirectory: true)
        if executable(target.appendingPathComponent("yt-dlp_macos")) != nil { return }
        let url = URL(string: "https://github.com/yt-dlp/yt-dlp/releases/download/\(tag)/yt-dlp_macos.zip")!
        let (zip, response) = try await URLSession.shared.download(from: url)
        defer { try? fm.removeItem(at: zip) }
        guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw URLError(.badServerResponse) }

        try fm.createDirectory(at: managedYTDLPRoot, withIntermediateDirectories: true)
        let staging = managedYTDLPRoot.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: staging) }
        let unzip = try await ProcessRunner.run(URL(fileURLWithPath: "/usr/bin/ditto"),
                                                ["-x", "-k", zip.path, staging.path],
                                                environment: ProcessRunner.environment(toolDirectories: []))
        guard unzip.status == 0, executable(staging.appendingPathComponent("yt-dlp_macos")) != nil else {
            throw CocoaError(.fileReadCorruptFile)
        }
        if fm.fileExists(atPath: target.path) { try fm.removeItem(at: target) }
        try fm.moveItem(at: staging, to: target)
    }
}
