import SwiftUI

struct RootView: View {
    @Environment(Router.self) private var router
    @Environment(DownloadQueue.self) private var queue
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        @Bindable var router = router
        NavigationSplitView {
            // 선택 해제(⌘-클릭)는 무시해 항상 한 화면이 선택되어 있게 합니다.
            List(selection: Binding(get: { router.screen }, set: { if let screen = $0 { router.screen = screen } })) {
                Label("New Download", systemImage: "link")
                    .tag(Screen.newDownload)
                Label("Bulk Download", systemImage: "text.badge.plus")
                    .tag(Screen.bulk)
                Label("Downloads", systemImage: "arrow.down.circle")
                    .badge(queue.activeJobCount)
                    .tag(Screen.queue)
            }
            .navigationSplitViewColumnWidth(min: 180, ideal: 200, max: 260)
            .safeAreaInset(edge: .bottom) {
                ToolStatusBanner(compact: true)
                    .padding(10)
            }
        } detail: {
            Group {
                switch router.screen ?? .newDownload {
                case .newDownload: NewDownloadView()
                case .bulk: BulkDownloadView()
                case .queue: QueueView()
                }
            }
            .frame(minWidth: 420, maxWidth: .infinity, maxHeight: .infinity)
            // 인스펙터를 상세 영역 안에 둡니다. 분할 뷰 전체에 붙이면 열리는 동안 전체 폭이 창보다 커져
            // 사이드바가 창 밖으로 밀렸다가 돌아옵니다. 이렇게 두면 상세 영역만 좁아집니다.
            .inspector(isPresented: $router.showOptions) {
                OptionsInspector()
                    .inspectorColumnWidth(min: 300, ideal: 330, max: 420)
            }
            .toolbar {
                ToolbarItem(placement: .primaryAction) {
                    Button {
                        router.showOptions.toggle()
                    } label: {
                        Label("Download Options", systemImage: "slider.horizontal.3")
                    }
                    .help("Show or hide download options")
                }
            }
        }
        // 사이드바(180) + 본문(420) + 옵션(300)이 들어가는 최소 폭입니다.
        .frame(minWidth: 900, minHeight: 520)
        .onAppear { AppDelegate.reopenMainWindow = { openWindow(id: "main") } }
    }
}
