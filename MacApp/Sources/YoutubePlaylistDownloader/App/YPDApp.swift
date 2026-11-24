import AppKit
import SwiftUI

@main
struct YPDApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    private let router = Router.shared

    var body: some Scene {
        // 창 하나짜리 앱입니다. 창을 닫으면 앱도 끝납니다(진행 중인 다운로드가 있으면 먼저 묻습니다).
        Window("YouTube Playlist Downloader", id: "main") {
            RootView()
                .environment(router)
                .environment(AppSettings.shared)
                .environment(ToolManager.shared)
                .environment(DownloadQueue.shared)
        }
        .defaultSize(width: 1080, height: 700)
        .windowToolbarStyle(.unified)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(after: .sidebar) {
                Button("New Download") { router.screen = .newDownload }
                    .keyboardShortcut("1", modifiers: .command)
                Button("Bulk Download") { router.screen = .bulk }
                    .keyboardShortcut("2", modifiers: .command)
                Button("Downloads") { router.screen = .queue }
                    .keyboardShortcut("3", modifiers: .command)
                Divider()
            }
        }

        Settings {
            SettingsView()
                .environment(AppSettings.shared)
                .environment(ToolManager.shared)
        }
    }
}

enum Screen: Hashable {
    case newDownload, bulk, queue
}

@MainActor @Observable
final class Router {
    static let shared = Router()
    var screen: Screen? = .newDownload
    var showOptions = false
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    /// 메인 창을 다시 여는 동작. RootView가 나타날 때 채워 둡니다(종료를 취소했을 때 창을 되살리는 데 씁니다).
    @MainActor static var reopenMainWindow: (() -> Void)?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // 시작을 막는 동기 작업은 하지 않습니다. 정리와 도구 확인은 백그라운드에서 합니다.
        // 정리를 먼저 끝내야 방금 시작한 다운로드·업데이트의 임시 폴더를 지우지 않습니다(다운로드는 도구 확인 뒤에만 시작됩니다).
        MainActor.assumeIsolated {
            AppSettings.shared.applyAppearance()
            Task {
                await Task.detached(priority: .utility) {
                    try? FileManager.default.removeItem(at: DownloadQueue.tempRoot)
                    ToolManager.removeOldManagedVersions()
                }.value
                await ToolManager.shared.refresh()
                DownloadQueue.shared.pump()
                await ToolManager.shared.autoUpdateIfNeeded(settings: AppSettings.shared)
            }
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        MainActor.assumeIsolated {
            let queue = DownloadQueue.shared
            if queue.hasActiveWork, AppSettings.shared.confirmQuitWhileDownloading {
                let alert = NSAlert()
                alert.messageText = String(localized: "Downloads are in progress")
                alert.informativeText = String(localized: "Quitting will stop all downloads. Unfinished files will not be saved.")
                alert.addButton(withTitle: String(localized: "Quit"))
                alert.addButton(withTitle: String(localized: "Cancel"))
                alert.showsSuppressionButton = true
                alert.suppressionButton?.title = String(localized: "Don’t ask again")
                let response = alert.runModal()
                if alert.suppressionButton?.state == .on { AppSettings.shared.confirmQuitWhileDownloading = false }
                guard response == .alertFirstButtonReturn else {
                    // 창 닫기 버튼으로 온 경우 창은 이미 닫혀 있습니다. 다운로드가 계속되므로 창을 다시 엽니다.
                    DispatchQueue.main.async { AppDelegate.reopenMainWindow?() }
                    return .terminateCancel
                }
            }
            // 자식 프로세스에 중지 신호만 보내고 기다리지 않습니다. 종료가 빨라집니다.
            ProcessRegistry.shared.interruptAll()
            return .terminateNow
        }
    }
}
