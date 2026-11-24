import AppKit
import SwiftUI

struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralSettings()
                .tabItem { Label("General", systemImage: "gearshape") }
            ComponentsSettings()
                .tabItem { Label("Components", systemImage: "shippingbox") }
        }
        .frame(width: 480)
    }
}

private struct GeneralSettings: View {
    @Environment(AppSettings.self) private var settings

    var body: some View {
        @Bindable var settings = settings
        Form {
            Section {
                Picker("Appearance", selection: $settings.appearance) {
                    Text("System").tag(Appearance.system)
                    Text("Light").tag(Appearance.light)
                    Text("Dark").tag(Appearance.dark)
                }
                .pickerStyle(.segmented)
            }
            Section {
                Picker("Language", selection: $settings.language) {
                    Text("System").tag(AppLanguage.system)
                    Text(verbatim: "English").tag(AppLanguage.en)
                    Text(verbatim: "한국어").tag(AppLanguage.ko)
                }
            } footer: {
                Text("Takes effect the next time you open the app")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Section("Save to") {
                SaveFolderRow()
            }
            Section {
                Stepper("Simultaneous downloads: \(settings.maxConcurrentDownloads)",
                        value: $settings.maxConcurrentDownloads,
                        in: AppSettings.concurrencyRange)
                Toggle("Ask before quitting while downloading", isOn: $settings.confirmQuitWhileDownloading)
            }
        }
        .formStyle(.grouped)
        // 설정 창이 내용 높이에 맞춰 커지도록 합니다.
        .fixedSize(horizontal: false, vertical: true)
    }
}

private struct ComponentsSettings: View {
    @Environment(AppSettings.self) private var settings
    @Environment(ToolManager.self) private var tools

    var body: some View {
        @Bindable var settings = settings
        Form {
            Section {
                ComponentRow(name: "yt-dlp",
                             value: tools.ytdlpVersion.map { Text(verbatim: $0) } ?? (tools.isReady ? Text("Installed") : Text("Not installed")),
                             path: tools.paths?.ytdlp.path,
                             state: tools.isReady ? .ok : .missing,
                             isChecking: tools.isChecking)
                ComponentRow(name: "ffmpeg",
                             value: tools.hasFFmpeg ? Text("Installed") : Text("Not found"),
                             path: tools.paths?.ffmpeg?.path,
                             state: tools.hasFFmpeg ? .ok : .missing,
                             isChecking: tools.isChecking)
                ComponentRow(name: "JavaScript runtime",
                             value: tools.jsRuntimeName.map { Text(verbatim: $0) } ?? Text("Not found"),
                             path: tools.paths?.jsRuntime?.path.path,
                             state: tools.jsRuntimeName != nil ? .ok : .warning,
                             isChecking: tools.isChecking,
                             note: tools.jsRuntimeName == nil ? Text("Needed for some YouTube formats") : nil)
            } footer: {
                if !tools.isChecking, !tools.hasFFmpeg || tools.jsRuntimeName == nil {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Install missing components with Homebrew:")
                        Text(verbatim: "brew install ffmpeg deno")
                            .font(.caption.monospaced())
                            .textSelection(.enabled)
                    }
                    .font(.caption)
                    .foregroundStyle(.secondary)
                }
            }

            Section("Updates") {
                VStack(alignment: .leading, spacing: 4) {
                    HStack(spacing: 8) {
                        Button {
                            Task { await tools.updateYTDLP(settings: settings) }
                        } label: {
                            tools.isReady ? Text("Update yt-dlp") : Text("Install yt-dlp")
                        }
                        .disabled(tools.isUpdating)
                        if tools.isUpdating {
                            ProgressView().controlSize(.small)
                        }
                    }
                    if let status = tools.updateStatus {
                        Text(status)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                }
                Toggle("Keep yt-dlp up to date automatically", isOn: $settings.autoUpdateYTDLP)
                Button("Check Again") { Task { await tools.refresh() } }
                    .disabled(tools.isChecking)
            }
        }
        .formStyle(.grouped)
        .fixedSize(horizontal: false, vertical: true)
    }
}

private struct ComponentRow: View {
    enum State { case ok, missing, warning }

    let name: LocalizedStringKey
    let value: Text
    let path: String?
    let state: State
    let isChecking: Bool
    var note: Text?

    var body: some View {
        LabeledContent {
            HStack(spacing: 6) {
                value.foregroundStyle(.secondary)
                if isChecking {
                    ProgressView().controlSize(.small)
                } else {
                    Image(systemName: symbol)
                        .foregroundStyle(color)
                        .accessibilityHidden(true)
                }
            }
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                Text(name)
                if let path {
                    Text(verbatim: path)
                        .font(.caption.monospaced())
                        .foregroundStyle(.tertiary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .help(path)
                }
                if let note {
                    note.font(.caption).foregroundStyle(.orange)
                }
            }
        }
    }

    private var symbol: String {
        switch state {
        case .ok: "checkmark.circle.fill"
        case .missing: "xmark.circle.fill"
        case .warning: "exclamationmark.circle.fill"
        }
    }

    private var color: Color {
        switch state {
        case .ok: .green
        case .missing: .red
        case .warning: .orange
        }
    }
}

/// 저장 폴더 표시와 변경. 설정 창과 옵션 인스펙터에서 같이 씁니다.
struct SaveFolderRow: View {
    @Environment(AppSettings.self) private var settings

    var body: some View {
        let dir = settings.saveDirectory
        LabeledContent {
            HStack(spacing: 8) {
                Button("Choose…") { choose(from: dir) }
                Button {
                    NSWorkspace.shared.open(dir)
                } label: {
                    Image(systemName: "arrow.up.forward.app")
                }
                .buttonStyle(.borderless)
                .help("Show in Finder")
                .accessibilityLabel("Show in Finder")
            }
        } label: {
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text(verbatim: FileManager.default.displayName(atPath: dir.path))
                    Text(verbatim: (dir.path as NSString).abbreviatingWithTildeInPath)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                        .truncationMode(.middle)
                }
            } icon: {
                Image(systemName: "folder")
                    .foregroundStyle(.tint)
            }
            .help(dir.path)
        }
    }

    private func choose(from dir: URL) {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.prompt = String(localized: "Choose")
        panel.directoryURL = dir
        Task {
            if await panel.begin() == .OK, let url = panel.url {
                settings.saveDirectory = url
            }
        }
    }
}
