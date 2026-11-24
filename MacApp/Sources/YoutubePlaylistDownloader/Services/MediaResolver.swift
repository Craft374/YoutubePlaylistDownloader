import Foundation
import YPDCore

struct UserFacingError: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

enum MediaResolver {
    /// 주소가 가리키는 영상·재생목록·채널의 정보와 항목 목록을 가져옵니다. 취소하면 yt-dlp도 멈춥니다.
    static func resolve(_ url: String, tools: ToolPaths, environment: [String: String]) async throws -> MediaSource {
        let result = try await ProcessRunner.run(tools.ytdlp, YTDLPArguments.resolve(url: url, tools: tools),
                                                 environment: environment, collectStdout: true)
        try Task.checkCancellation()
        guard result.status == 0, !result.stdout.isEmpty else {
            throw UserFacingError(message: result.errorMessage)
        }
        do {
            return try MediaSource.parse(result.stdout)
        } catch {
            throw UserFacingError(message: String(localized: "Could not read the video information."))
        }
    }
}
