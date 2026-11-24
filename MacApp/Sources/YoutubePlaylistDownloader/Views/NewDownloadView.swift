import AppKit
import SwiftUI
import YPDCore

/// 입력한 주소와 조회 결과. 사이드바에서 다른 화면에 다녀와도 그대로 남도록 화면 밖에 둡니다.
@MainActor @Observable
private final class NewDownloadModel {
    static let shared = NewDownloadModel()

    enum Phase {
        case idle, resolving, failed(String), resolved(MediaSource)

        /// 애니메이션 기준값. MediaSource는 비교할 수 없어 단계 이름만 씁니다.
        var key: String {
            switch self {
            case .idle: "idle"
            case .resolving: "resolving"
            case .failed: "failed"
            case .resolved: "resolved"
            }
        }
    }

    var text = "" {
        didSet { if text != oldValue { textChanged() } }
    }
    private(set) var phase: Phase = .idle
    private(set) var looksInvalid = false
    /// 지금 phase가 가리키는 주소. 공백만 바뀐 경우 다시 조회하지 않기 위해 둡니다.
    private var currentURL: String?
    private var lookup: Task<Void, Never>?

    /// 입력이 멈추고 0.5초 뒤에 조회합니다.
    private func textChanged() {
        let url = SourceURL.normalize(text)
        if url != nil, url == currentURL { return }
        start(url, delay: .milliseconds(500))
    }

    /// Return·붙여넣기·다시 시도: 바로 조회합니다. 이미 조회 중이거나 결과가 있으면 그대로 둡니다.
    func lookUpNow() {
        let url = SourceURL.normalize(text)
        if url != nil, url == currentURL {
            switch phase {
            case .resolving, .resolved: return
            case .idle, .failed: break
            }
        }
        start(url, delay: .zero)
    }

    func cancel() {
        lookup?.cancel()
        phase = .idle
    }

    func reset() {
        text = ""
        cancel()
    }

    private func start(_ url: String?, delay: Duration) {
        lookup?.cancel()
        currentURL = url
        phase = .idle
        let isEmpty = text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        if url != nil || isEmpty { looksInvalid = false }
        lookup = Task {
            if delay > .zero { try? await Task.sleep(for: delay) }
            guard !Task.isCancelled else { return }
            guard let url else {
                looksInvalid = !isEmpty
                return
            }
            // 도구가 아직 없으면 조회하지 않습니다. 화면에는 도구 안내가 보입니다.
            guard let tools = ToolManager.shared.paths else { return }
            phase = .resolving
            do {
                let source = try await MediaResolver.resolve(url, tools: tools, environment: ToolManager.shared.environment)
                guard !Task.isCancelled else { return }
                phase = .resolved(source)
            } catch {
                guard !Task.isCancelled, !(error is CancellationError) else { return }
                phase = .failed(error.localizedDescription)
            }
        }
    }
}

struct NewDownloadView: View {
    private let model = NewDownloadModel.shared
    @Environment(ToolManager.self) private var tools
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var showAdded = false

    var body: some View {
        ScrollView {
            VStack(spacing: Design.spacing * 2) {
                VStack(spacing: 8) {
                    LogoView(size: 56)
                    Text("New Download")
                        .font(.title)
                        .fontWeight(.semibold)
                    Text("Paste a link to a YouTube video, playlist or channel")
                        .foregroundStyle(.secondary)
                }
                .multilineTextAlignment(.center)

                LinkField(model: model)

                phaseContent
                    .frame(maxWidth: .infinity)
            }
            .frame(maxWidth: Design.contentWidth)
            .padding(.horizontal, 24)
            .padding(.top, 48)
            .padding(.bottom, 24)
            .frame(maxWidth: .infinity)
        }
        .adaptiveAnimation(model.phase.key)
        .adaptiveAnimation(model.looksInvalid)
        .adaptiveAnimation(showAdded)
        .task(id: showAdded) {
            guard showAdded else { return }
            try? await Task.sleep(for: .seconds(2))
            showAdded = false
        }
        // 실행 직후 도구 확인이 끝나기 전에 붙여 넣은 주소도 조회되게 합니다.
        .onChange(of: tools.isReady) { _, ready in
            if ready { model.lookUpNow() }
        }
    }

    @ViewBuilder
    private var phaseContent: some View {
        let transition = AnyTransition.appear(reduceMotion: reduceMotion)
        switch model.phase {
        case .idle:
            VStack(spacing: Design.spacing) {
                if model.looksInvalid {
                    Text("This doesn’t look like a link")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .transition(transition)
                }
                if showAdded {
                    Label {
                        Text("Added to Downloads")
                    } icon: {
                        Image(systemName: "checkmark.circle.fill").foregroundStyle(.green)
                    }
                    .font(.callout)
                    .transition(transition)
                }
                if !tools.isReady {
                    ToolStatusBanner()
                }
            }
        case .resolving:
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Looking up…").foregroundStyle(.secondary)
                Button("Cancel") { model.cancel() }
                    .controlSize(.small)
            }
            .transition(transition)
        case .failed(let message):
            FailureCard(message: message) { model.lookUpNow() }
                .transition(transition)
        case .resolved(let source):
            ResultCard(source: source) { model.reset() } onAdded: { showAdded = true }
                .transition(transition)
        }
    }
}

private struct LinkField: View {
    @Bindable var model: NewDownloadModel
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: 10) {
            Image(systemName: "link")
                .foregroundStyle(.secondary)
            TextField("Paste a link", text: $model.text)
                .textFieldStyle(.plain)
                .focused($focused)
                .onSubmit { model.lookUpNow() }
            if !model.text.isEmpty {
                Button {
                    model.text = ""
                } label: {
                    Image(systemName: "xmark.circle.fill")
                }
                .accessibilityLabel("Clear")
                .buttonStyle(.borderless)
                .foregroundStyle(.tertiary)
                .help("Clear")
            }
            Button {
                guard let string = NSPasteboard.general.string(forType: .string) else { return }
                model.text = string.trimmingCharacters(in: .whitespacesAndNewlines)
                model.lookUpNow()
            } label: {
                Image(systemName: "doc.on.clipboard")
            }
            .accessibilityLabel("Paste from clipboard")
            .buttonStyle(.borderless)
            .help("Paste from clipboard")
        }
        .font(.title3)
        .padding(.horizontal, 14)
        .padding(.vertical, 12)
        .background(.background.secondary, in: RoundedRectangle(cornerRadius: Design.cornerRadius, style: .continuous))
        .overlay {
            RoundedRectangle(cornerRadius: Design.cornerRadius, style: .continuous)
                .strokeBorder(focused ? AnyShapeStyle(.tint) : AnyShapeStyle(.separator), lineWidth: 1)
        }
        .animation(.easeOut(duration: 0.15), value: focused)
        .onAppear { focused = true }
    }
}

private struct FailureCard: View {
    let message: String
    let retry: () -> Void

    var body: some View {
        HStack(alignment: .top, spacing: Design.spacing) {
            Image(systemName: "exclamationmark.triangle.fill")
                .foregroundStyle(.orange)
                .font(.title3)
            VStack(alignment: .leading, spacing: Design.spacing) {
                Text(message)
                    .textSelection(.enabled)
                    .fixedSize(horizontal: false, vertical: true)
                Button("Try Again", action: retry)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .card()
    }
}

private struct ResultCard: View {
    let source: MediaSource
    let onEnqueued: () -> Void
    let onAdded: () -> Void

    @Environment(Router.self) private var router
    @Environment(AppSettings.self) private var settings
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .trailing, spacing: 16) {
            HStack(alignment: .top, spacing: 16) {
                RemoteImage(url: source.thumbnail, width: 208)
                VStack(alignment: .leading, spacing: 6) {
                    KindBadge(kind: source.kind)
                    Text(source.title)
                        .font(.title3)
                        .fontWeight(.semibold)
                        .lineLimit(2)
                    if let uploader = source.uploader {
                        Text(uploader).foregroundStyle(.secondary)
                    }
                    detail
                        .font(.callout)
                        .foregroundStyle(.secondary)
                    filterNote
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            HStack(spacing: Design.spacing) {
                Button("Add to Queue") {
                    if enqueue() { onAdded() }
                }
                .buttonStyle(.bordered)
                Button("Download") {
                    if enqueue() { router.screen = .queue }
                }
                .buttonStyle(.borderedProminent)
            }
            .controlSize(.large)
        }
        .card()
        .alert("Couldn’t add the download",
               isPresented: Binding(get: { errorMessage != nil }, set: { if !$0 { errorMessage = nil } }),
               presenting: errorMessage) { _ in
            Button("OK") {}
        } message: { message in
            Text(message)
        }
    }

    @ViewBuilder
    private var detail: some View {
        if source.kind == .video {
            let parts = [Formatters.duration(source.entries.first?.duration),
                         source.viewCount.map { String(localized: "\($0.formatted(.number)) views") }]
                .compactMap { $0 }
            if !parts.isEmpty { Text(parts.joined(separator: " · ")) }
        } else {
            Text("\(source.entries.count) videos")
        }
    }

    @ViewBuilder
    private var filterNote: some View {
        let total = source.entries.count
        let selected = settings.options.selectEntries(from: source).count
        if selected != total {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text("\(selected) of \(total) will be downloaded with the current range and length filter")
                    .fixedSize(horizontal: false, vertical: true)
                Button("Options…") { router.showOptions = true }
                    .buttonStyle(.link)
            }
            .font(.caption)
            .foregroundStyle(.secondary)
        }
    }

    /// 성공하면 입력을 비웁니다. 실패하면 경고를 띄우고 결과는 그대로 둡니다.
    private func enqueue() -> Bool {
        do {
            try DownloadQueue.shared.enqueue(source)
            onEnqueued()
            return true
        } catch {
            errorMessage = error.localizedDescription
            return false
        }
    }
}

private struct KindBadge: View {
    let kind: MediaSource.Kind

    var body: some View {
        label
            .font(.caption.weight(.medium))
            .foregroundStyle(.tint)
            .padding(.horizontal, 8)
            .padding(.vertical, 3)
            .background(.tint.opacity(0.12), in: Capsule())
    }

    @ViewBuilder
    private var label: some View {
        switch kind {
        case .video: Label("Video", systemImage: "play.rectangle")
        case .playlist: Label("Playlist", systemImage: "list.bullet.rectangle")
        case .channel: Label("Channel", systemImage: "person.crop.square")
        }
    }
}
