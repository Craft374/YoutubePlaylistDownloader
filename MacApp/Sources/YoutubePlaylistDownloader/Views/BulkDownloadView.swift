import AppKit
import SwiftUI
import UniformTypeIdentifiers
import YPDCore

/// 일괄 추가 상태. 다른 화면으로 가도 진행 중인 작업이 멈추지 않도록 화면 밖에 둡니다.
@MainActor @Observable
private final class BulkDownloadModel {
    static let shared = BulkDownloadModel()

    struct Failure: Identifiable {
        let id = UUID()
        let link: String
        let reason: String
    }

    var text = ""
    private(set) var isRunning = false
    private(set) var done = 0
    private(set) var total = 0
    private(set) var currentLink: String?
    private(set) var addedCount = 0
    private(set) var failures: [Failure] = []
    private var task: Task<Void, Never>?

    var failureReport: String {
        failures.map { "\($0.link) — \($0.reason)" }.joined(separator: "\n")
    }

    func startFromText() {
        let links = SourceURL.lines(text)
        guard !links.isEmpty else { return }
        text = ""
        run(links)
    }

    func retryFailures() {
        run(failures.map(\.link))
    }

    func stop() {
        task?.cancel()
    }

    func dismissFailures() {
        failures = []
    }

    /// 링크를 하나씩 차례로 조회해 대기열에 넣습니다. 동시에 여러 개를 조회하면 유튜브가 막을 수 있습니다.
    private func run(_ links: [String]) {
        guard !isRunning, !links.isEmpty else { return }
        isRunning = true
        done = 0
        total = links.count
        addedCount = 0
        failures = []
        // 시작할 때의 옵션과 폴더로 모든 링크를 받습니다. 조회 중에 옵션을 바꿔도 섞이지 않습니다.
        let options = AppSettings.shared.options
        let directory = AppSettings.shared.saveDirectory
        task = Task {
            for link in links {
                if Task.isCancelled { break }
                currentLink = link
                do {
                    guard let url = SourceURL.normalize(link) else {
                        throw UserFacingError(message: String(localized: "Not a valid link"))
                    }
                    guard let tools = ToolManager.shared.paths else { throw DownloadQueue.EnqueueError.toolsMissing }
                    let source = try await MediaResolver.resolve(url, tools: tools, environment: ToolManager.shared.environment)
                    try DownloadQueue.shared.enqueue(source, options: options, directory: directory)
                    addedCount += 1
                } catch {
                    if Task.isCancelled { break }
                    failures.append(Failure(link: link, reason: error.localizedDescription))
                }
                done += 1
            }
            // 멈추면 아직 넣지 않은 링크를 입력란에 되돌려 놓습니다(그사이 입력한 내용은 뒤에 둡니다).
            if done < links.count {
                text = (links[done...] + SourceURL.lines(text)).joined(separator: "\n")
            }
            currentLink = nil
            isRunning = false
            task = nil
        }
    }
}

struct BulkDownloadView: View {
    private let model = BulkDownloadModel.shared
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: Design.spacing * 2) {
                VStack(spacing: 8) {
                    Text("Bulk Download")
                        .font(.title)
                        .fontWeight(.semibold)
                    Text("Paste one link per line. Each link becomes its own download")
                        .foregroundStyle(.secondary)
                }
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity)

                LinkEditor(model: model)

                let transition = AnyTransition.appear(reduceMotion: reduceMotion)
                if model.isRunning {
                    BulkProgress(model: model)
                        .transition(transition)
                } else {
                    if model.addedCount > 0 {
                        AddedSummary(count: model.addedCount)
                            .transition(transition)
                    }
                    if !model.failures.isEmpty {
                        FailureList(model: model)
                            .transition(transition)
                    }
                }
            }
            .frame(maxWidth: Design.contentWidth)
            .padding(.horizontal, 24)
            .padding(.top, 48)
            .padding(.bottom, 24)
            .frame(maxWidth: .infinity)
        }
        .adaptiveAnimation(model.isRunning)
        .adaptiveAnimation(model.failures.isEmpty)
    }
}

private struct LinkEditor: View {
    @Bindable var model: BulkDownloadModel
    @Environment(ToolManager.self) private var tools

    var body: some View {
        let count = SourceURL.lines(model.text).count
        VStack(alignment: .leading, spacing: Design.spacing) {
            TextEditor(text: $model.text)
                .font(.body)
                .scrollContentBackground(.hidden)
                .frame(minHeight: 220)
                .overlay(alignment: .topLeading) {
                    if model.text.isEmpty {
                        Text(verbatim: "https://www.youtube.com/playlist?list=…")
                            .foregroundStyle(.tertiary)
                            .padding(.leading, 5)
                            .allowsHitTesting(false)
                    }
                }
                .card()

            HStack {
                Text("\(count) links")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Download All") { model.startFromText() }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(count == 0 || model.isRunning || !tools.isReady)
            }
        }
    }
}

private struct BulkProgress: View {
    let model: BulkDownloadModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Adding \(min(model.done + 1, model.total)) of \(model.total)…")
                Spacer()
                Button("Stop") { model.stop() }
            }
            ProgressView(value: Double(model.done), total: Double(max(model.total, 1)))
            if let link = model.currentLink {
                Text(verbatim: link)
                    .font(.caption.monospaced())
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
        }
        .card()
    }
}

private struct AddedSummary: View {
    let count: Int
    @Environment(Router.self) private var router

    var body: some View {
        HStack {
            Label {
                Text("Added \(count) downloads")
            } icon: {
                Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
            }
            Spacer()
            Button("Show Downloads") { router.screen = .queue }
        }
    }
}

private struct FailureList: View {
    let model: BulkDownloadModel

    var body: some View {
        VStack(alignment: .leading, spacing: Design.spacing) {
            Label {
                Text("\(model.failures.count) links could not be added")
                    .fontWeight(.semibold)
            } icon: {
                Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange)
            }

            LazyVStack(alignment: .leading, spacing: 8) {
                ForEach(model.failures) { failure in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(verbatim: failure.link)
                            .font(.caption.monospaced())
                            .textSelection(.enabled)
                        Text(failure.reason)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                }
            }

            HStack {
                Button("Retry") { model.retryFailures() }
                Button("Copy") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(model.failureReport, forType: .string)
                }
                Button("Save…") { save(model.failureReport) }
                Spacer()
                Button("Dismiss") { model.dismissFailures() }
            }
        }
        .card()
    }

    private func save(_ report: String) {
        let panel = NSSavePanel()
        panel.nameFieldStringValue = String(localized: "Links not added.txt")
        panel.allowedContentTypes = [.plainText]
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try report.write(to: url, atomically: true, encoding: .utf8)
            } catch {
                if let window = NSApp.mainWindow { NSAlert(error: error).beginSheetModal(for: window) }
            }
        }
    }
}
