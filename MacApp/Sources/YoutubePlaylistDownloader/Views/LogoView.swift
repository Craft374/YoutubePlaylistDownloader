import SwiftUI

/// 앱 로고(Resources/AppIcon.svg와 같은 모양). 도형 6개: 배경, 목록 막대 3개, 원, 아래 화살표.
struct LogoView: View {
    var size: CGFloat = 64

    static let top = Color(red: 1.0, green: 0.416, blue: 0.361)     // #FF6A5C
    static let bottom = Color(red: 0.898, green: 0.188, blue: 0.420) // #E5306B

    var body: some View {
        Canvas { context, canvasSize in
            // SVG 좌표(1024 기준)를 그대로 씁니다.
            let s = canvasSize.width / 1024
            func rect(_ x: CGFloat, _ y: CGFloat, _ w: CGFloat, _ h: CGFloat) -> CGRect {
                CGRect(x: x * s, y: y * s, width: w * s, height: h * s)
            }
            let background = Path(roundedRect: rect(100, 100, 824, 824), cornerRadius: 186 * s, style: .continuous)
            context.fill(background, with: .linearGradient(Gradient(colors: [Self.top, Self.bottom]),
                                                           startPoint: CGPoint(x: 0, y: 100 * s),
                                                           endPoint: CGPoint(x: 0, y: 924 * s)))
            let bars: [(CGFloat, CGFloat, Double)] = [(276, 400, 1), (404, 320, 0.78), (532, 240, 0.56)]
            for (y, width, opacity) in bars {
                context.fill(Path(roundedRect: rect(236, y, width, 64), cornerRadius: 32 * s), with: .color(.white.opacity(opacity)))
            }
            context.fill(Path(ellipseIn: rect(492, 492, 320, 320)), with: .color(.white))
            var arrow = Path()
            arrow.move(to: CGPoint(x: 652 * s, y: 568 * s))
            arrow.addLine(to: CGPoint(x: 652 * s, y: 722 * s))
            arrow.move(to: CGPoint(x: 590 * s, y: 664 * s))
            arrow.addLine(to: CGPoint(x: 652 * s, y: 726 * s))
            arrow.addLine(to: CGPoint(x: 714 * s, y: 664 * s))
            context.stroke(arrow, with: .color(Self.bottom),
                           style: StrokeStyle(lineWidth: 48 * s, lineCap: .round, lineJoin: .round))
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}
