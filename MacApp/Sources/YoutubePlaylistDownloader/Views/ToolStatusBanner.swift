import SwiftUI

/// 필요한 도구가 없을 때만 보이는 안내. 모두 있으면 아무것도 그리지 않습니다.
struct ToolStatusBanner: View {
    var compact = false
    @Environment(ToolManager.self) private var tools
    @Environment(AppSettings.self) private var settings

    private var problem: (String, String)? {
        if tools.isChecking { return nil }
        if !tools.isReady { return ("exclamationmark.triangle.fill", String(localized: "yt-dlp is not installed.")) }
        if !tools.hasFFmpeg { return ("exclamationmark.triangle.fill", String(localized: "ffmpeg was not found. Merging and converting will fail.")) }
        if tools.jsRuntimeName == nil { return ("exclamationmark.circle.fill", String(localized: "No JavaScript runtime (deno) was found. Some YouTube formats may be missing.")) }
        return nil
    }

    var body: some View {
        if let (icon, message) = problem {
            VStack(alignment: .leading, spacing: 8) {
                Label {
                    Text(message).fixedSize(horizontal: false, vertical: true)
                } icon: {
                    Image(systemName: icon).foregroundStyle(.orange)
                }
                .font(compact ? .caption : .callout)

                if !tools.isReady {
                    Button {
                        Task { await tools.updateYTDLP(settings: settings) }
                    } label: {
                        if tools.isUpdating {
                            ProgressView().controlSize(.small)
                        } else {
                            Text("Install yt-dlp")
                        }
                    }
                    .controlSize(compact ? .small : .regular)
                    .disabled(tools.isUpdating)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(compact ? 10 : 14)
            .background(.orange.opacity(0.1), in: RoundedRectangle(cornerRadius: Design.cornerRadius, style: .continuous))
            .transition(.opacity)
        }
    }
}
