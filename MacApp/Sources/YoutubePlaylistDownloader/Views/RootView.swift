import SwiftUI

struct RootView: View {
    @Environment(Router.self) private var router
    @Environment(DownloadQueue.self) private var queue
    @Environment(\.openWindow) private var openWindow
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    var body: some View {
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
            // .inspector는 AppKit 분할 칸을 애니메이션으로 열어, 그동안 전체 폭이 창보다 커져 사이드바가 밀려납니다
            // (분할 뷰·상세 어디에 붙여도 같음). 그래서 옵션은 상세 안의 평범한 오른쪽 패널로 둡니다.
            HStack(spacing: 0) {
                Group {
                    switch router.screen ?? .newDownload {
                    case .newDownload: NewDownloadView()
                    case .bulk: BulkDownloadView()
                    case .queue: QueueView()
                    }
                }
                .frame(minWidth: 420, maxWidth: .infinity, maxHeight: .infinity)
                if router.showOptions {
                    OptionsInspector()
                        .frame(width: 300)
                        .frame(maxHeight: .infinity)
                        // 구분선을 겹쳐 그려 패널 폭이 정확히 300이 되게 합니다.
                        .overlay(alignment: .leading) { Divider() }
                        .transition(reduceMotion ? .opacity : .move(edge: .trailing))
                }
            }
            // 열든 닫든 최소 폭이 같아(본문 420 + 옵션 300) 열 때 분할 뷰가 칸 폭을 다시 정하지 않습니다.
            .frame(minWidth: 720)
            .clipped()
            // 툴바 버튼과 New Download의 "Options…" 버튼 모두 같은 스프링(동작 줄이기면 페이드)으로 열고 닫습니다.
            .adaptiveAnimation(router.showOptions)
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
        // 사이드바 최대 폭(260) + 구분선(1) + 상세 최소 폭(720)입니다. 사이드바를 끝까지 넓혀도 상세가 들어갑니다.
        .frame(minWidth: 981, minHeight: 520)
        .onAppear { AppDelegate.reopenMainWindow = { openWindow(id: "main") } }
    }
}
