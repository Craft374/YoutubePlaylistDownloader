import SwiftUI

/// 화면 전체에서 같이 쓰는 간격·모양·움직임 값. 값을 한곳에 모아 화면마다 제각각이 되지 않게 합니다.
enum Design {
    static let spacing: CGFloat = 12
    static let cornerRadius: CGFloat = 12
    static let thumbnailCorner: CGFloat = 8
    /// 본문 영역 최대 폭. 넓은 창에서도 글줄이 너무 길어지지 않게 합니다.
    static let contentWidth: CGFloat = 640

    /// 기본 스프링: 튀지 않고(감쇠 1.0) 0.35초 안에 자리 잡습니다. 진행 중에 다시 바뀌어도 현재 값에서 이어집니다.
    static let spring = Animation.spring(response: 0.35, dampingFraction: 1.0)
}

extension View {
    /// "동작 줄이기"가 켜져 있으면 스프링 대신 짧은 페이드로 바꿉니다.
    func adaptiveAnimation<V: Equatable>(_ value: V) -> some View {
        modifier(AdaptiveAnimation(value: value))
    }

    /// 카드: 시스템 배경 위에 살짝 떠 있는 면. 라이트·다크 모드에서 자동으로 색이 바뀝니다.
    func card() -> some View {
        padding(16)
            .background(.background.secondary, in: RoundedRectangle(cornerRadius: Design.cornerRadius, style: .continuous))
            .overlay(RoundedRectangle(cornerRadius: Design.cornerRadius, style: .continuous).strokeBorder(.separator.opacity(0.5)))
    }
}

private struct AdaptiveAnimation<V: Equatable>: ViewModifier {
    let value: V
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    func body(content: Content) -> some View {
        content.animation(reduceMotion ? .easeInOut(duration: 0.15) : Design.spring, value: value)
    }
}

/// 나타나고 사라지는 요소용 전환: 동작 줄이기가 켜져 있으면 페이드만 합니다.
extension AnyTransition {
    static func appear(reduceMotion: Bool) -> AnyTransition {
        reduceMotion ? .opacity : .opacity.combined(with: .scale(scale: 0.98, anchor: .top))
    }
}

enum Formatters {
    static func speed(_ bytesPerSecond: Double?) -> String? {
        guard let bytesPerSecond, bytesPerSecond > 0 else { return nil }
        return ByteCountFormatter.string(fromByteCount: Int64(bytesPerSecond), countStyle: .file) + "/s"
    }

    static func duration(_ seconds: Double?) -> String? {
        guard let seconds, seconds > 0 else { return nil }
        let f = DateComponentsFormatter()
        f.allowedUnits = seconds >= 3600 ? [.hour, .minute, .second] : [.minute, .second]
        f.zeroFormattingBehavior = .pad
        return f.string(from: seconds)
    }
}
