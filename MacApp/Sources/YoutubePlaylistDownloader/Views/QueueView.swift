import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// "Downloads" 화면. 최근 작업이 위에 옵니다.
/// 행마다 자기 작업·항목만 읽게 나눠서, 진행률이 바뀌어도 그 행만 다시 그립니다.
struct QueueView: View {
    @Environment(Router.self) private var router
    @Environment(DownloadQueue.self) private var queue

    var body: some View {
        Group {
            if queue.jobs.isEmpty {
                ContentUnavailableView {
                    Label("No Downloads", systemImage: "arrow.down.circle")
                } description: {
                    Text("Videos you download appear here")
                } actions: {
                    Button("New Download") { router.screen = .newDownload }
                }
            } else {
                List {
                    ForEach(queue.jobs) { job in
                        JobRow(job: job)
                        if job.isExpanded {
                            ForEach(job.items) { item in
                                ItemRow(item: item, job: job)
                            }
                            FailureActionsRow(job: job)
                        }
                    }
                }
                .listStyle(.inset)
                .alternatingRowBackgrounds(.disabled)
            }
        }
        .adaptiveAnimation(queue.jobs.map(\.id))
        .toolbar {
            ToolbarItemGroup(placement: .automatic) {
                QueueToolbarButtons()
            }
        }
    }
}

/// 툴바 버튼은 모든 작업의 상태를 읽으므로 따로 떼어, 목록 전체가 다시 그려지지 않게 합니다.
private struct QueueToolbarButtons: View {
    // 툴바는 따로 호스팅되므로 환경 대신 공유 인스턴스를 씁니다.
    private var queue: DownloadQueue { .shared }

    var body: some View {
        Button {
            queue.clearFinished()
        } label: {
            Label("Clear Finished", systemImage: "checkmark.circle")
        }
        .help("Remove finished downloads from the list")
        .disabled(!queue.jobs.contains { $0.isFinished })

        Button {
            queue.cancelAll()
        } label: {
            Label("Stop All", systemImage: "stop.circle")
        }
        .help("Stop all downloads")
        .disabled(!queue.hasActiveWork)
    }
}

private struct JobRow: View {
    let job: DownloadJob
    @Environment(DownloadQueue.self) private var queue
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
        let finished = job.isFinished
        let failedCount = job.failedCount
        let isExpanded = job.isExpanded
        let canRetry = finished && (failedCount > 0 || job.isCancelled)
        let status = status(finished: finished, failedCount: failedCount)

        HStack(spacing: Design.spacing) {
            RemoteImage(url: job.thumbnail, width: 112)

            VStack(alignment: .leading, spacing: 4) {
                Text(job.title)
                    .font(.headline)
                    .lineLimit(1)
                    .truncationMode(.tail)
                Text(status)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .monospacedDigit()
                    .lineLimit(1)
                if !finished, job.items.count > 1, let current = job.currentTitle {
                    Text(current)
                        .font(.caption)
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                }
                ProgressView(value: job.progress)
                    .progressViewStyle(.linear)
                    .controlSize(.small)
                    .tint(tint(finished: finished, failedCount: failedCount))
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            HStack(spacing: 8) {
                if job.items.count > 1 || failedCount > 0 || isExpanded {
                    Button(action: toggle) {
                        Label(isExpanded ? "Hide Items" : "Show Items", systemImage: "chevron.right")
                            .rotationEffect(.degrees(isExpanded ? 90 : 0))
                    }
                    .help(isExpanded ? "Hide items" : "Show items")
                }
                Button(action: openFolder) {
                    Label("Show in Finder", systemImage: "folder")
                }
                .help("Show in Finder")
                if !finished {
                    Button { queue.cancel(job) } label: {
                        Label("Stop", systemImage: "stop.circle")
                    }
                    .help("Stop")
                }
                if canRetry {
                    Button { queue.retryFailed(job) } label: {
                        Label("Retry Failed", systemImage: "arrow.clockwise")
                    }
                    .help("Retry failed downloads")
                }
                if finished {
                    Button { queue.remove(job) } label: {
                        Label("Remove", systemImage: "xmark.circle")
                    }
                    .help("Remove from list")
                }
            }
            .labelStyle(.iconOnly)
            .buttonStyle(.borderless)
            .imageScale(.large)
        }
        .padding(.vertical, 6)
        .contentShape(Rectangle())
        .onTapGesture(count: 2, perform: openFolder)
        .contextMenu {
            Button("Show in Finder", action: openFolder)
            if canRetry { Button("Retry Failed") { queue.retryFailed(job) } }
            if !finished { Button("Stop") { queue.cancel(job) } }
            Divider()
            Button("Remove") { queue.remove(job) }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(job.title + ", " + status)
    }

    private func status(finished: Bool, failedCount: Int) -> String {
        if finished {
            if job.isCancelled { return String(localized: "Cancelled") }
            return failedCount == 0 ? String(localized: "Done") : String(localized: "Done · \(failedCount) failed")
        }
        guard job.isRunning else { return String(localized: "Waiting…") }
        var parts: [String] = []
        if job.items.count > 1 { parts.append(String(localized: "\(job.completedCount) of \(job.items.count)")) }
        if job.isProcessing {
            parts.append(String(localized: "Processing…"))
        } else if let speed = Formatters.speed(job.speed) {
            parts.append(speed)
        }
        return parts.isEmpty ? String(localized: "Downloading…") : parts.joined(separator: " · ")
    }

    private func tint(finished: Bool, failedCount: Int) -> Color? {
        guard finished else { return nil }
        if failedCount > 0 { return .orange }
        return job.isCancelled ? nil : .green
    }

    private func toggle() {
        withAnimation(reduceMotion ? .easeInOut(duration: 0.15) : Design.spring) { job.isExpanded.toggle() }
    }

    private func openFolder() { NSWorkspace.shared.open(job.directory) }
}

private struct ItemRow: View {
    let item: DownloadItem
    let job: DownloadJob

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 8) {
                icon.frame(width: 16)
                Text(verbatim: "\(item.entry.index).")
                    .monospacedDigit()
                    .foregroundStyle(.tertiary)
                Text(item.entry.title)
                    .lineLimit(1)
                Spacer(minLength: 8)
                if item.state.isRunning {
                    Text(verbatim: "\(Int(item.progress * 100))%")
                        .monospacedDigit()
                        .foregroundStyle(.secondary)
                }
            }
            if case let .failed(message) = item.state {
                Text(message)
                    .font(.caption)
                    .foregroundStyle(.red)
                    .lineLimit(3)
                    .textSelection(.enabled)
                    .padding(.leading, 24)
            }
        }
        .padding(.leading, 24)
        .contentShape(Rectangle())
        // 끝난 항목만 더블 클릭을 받습니다. 실패 메시지의 텍스트 선택을 가로채지 않게 합니다.
        .gesture(TapGesture(count: 2).onEnded(reveal), including: item.state == .done ? .all : .subviews)
        .contextMenu {
            if item.state == .done { Button("Show in Finder", action: reveal) }
        }
        .accessibilityElement(children: .combine)
    }

    @ViewBuilder private var icon: some View {
        switch item.state {
        case .queued:
            Image(systemName: "circle.dotted").foregroundStyle(.tertiary).accessibilityLabel("Queued")
        case .downloading:
            ProgressView(value: item.progress)
                .progressViewStyle(.circular)
                .controlSize(.mini)
                .accessibilityLabel("Downloading")
        case .processing:
            Image(systemName: "gearshape").foregroundStyle(.secondary).accessibilityLabel("Processing")
        case .done:
            Image(systemName: "checkmark.circle.fill").foregroundStyle(.green).accessibilityLabel("Done")
        case .skipped:
            Image(systemName: "arrow.uturn.forward.circle").foregroundStyle(.secondary)
                .help("Already exists")
                .accessibilityLabel("Already exists")
        case .failed:
            Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.orange).accessibilityLabel("Failed")
        case .cancelled:
            Image(systemName: "minus.circle").foregroundStyle(.secondary).accessibilityLabel("Cancelled")
        }
    }

    private func reveal() {
        if let url = item.fileURL {
            NSWorkspace.shared.activateFileViewerSelecting([url])
        } else {
            NSWorkspace.shared.open(job.directory)
        }
    }
}

/// 실패한 항목 목록을 복사하거나 저장합니다. 끝났고 실패가 있을 때만 행이 생깁니다.
private struct FailureActionsRow: View {
    let job: DownloadJob

    var body: some View {
        if job.isFinished, job.failedCount > 0 {
            HStack(spacing: 8) {
                Button("Copy Error List", action: copy)
                Button("Save Error List…", action: save)
            }
            .controlSize(.small)
            .padding(.leading, 24)
            .padding(.vertical, 4)
        }
    }

    private var report: String {
        job.failedItems.map { item in
            guard case let .failed(reason) = item.state else { return "" }
            return "\(item.entry.index). \(item.entry.title) — \(reason)"
        }
        .joined(separator: "\n")
    }

    private func copy() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(report, forType: .string)
    }

    private func save() {
        let text = report
        let panel = NSSavePanel()
        panel.nameFieldStringValue = String(localized: "Videos not downloaded.txt")
        panel.allowedContentTypes = [.plainText]
        panel.directoryURL = job.directory
        panel.begin { response in
            guard response == .OK, let url = panel.url else { return }
            do {
                try text.write(to: url, atomically: true, encoding: .utf8)
            } catch {
                if let window = NSApp.mainWindow { NSAlert(error: error).beginSheetModal(for: window) }
            }
        }
    }
}
