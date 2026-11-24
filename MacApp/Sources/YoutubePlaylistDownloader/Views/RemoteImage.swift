import ImageIO
import SwiftUI

/// 썸네일 캐시. 받은 이미지는 화면 크기에 맞게 메인 스레드 밖에서 줄여 두고 재사용합니다.
/// (AsyncImage는 캐시가 없어 목록을 스크롤할 때마다 다시 받고 다시 디코딩합니다.)
actor ImageCache {
    static let shared = ImageCache()
    // 같은 주소라도 크기가 다르면 따로 저장합니다(작은 이미지를 크게 늘려 흐려지지 않게).
    private let cache = NSCache<NSString, CGImage>()
    private var inFlight: [String: Task<CGImage?, Never>] = [:]

    init() { cache.countLimit = 400 }

    func image(for url: URL, maxPixel: CGFloat) async -> CGImage? {
        let key = "\(url.absoluteString)#\(Int(maxPixel))"
        if let cached = cache.object(forKey: key as NSString) { return cached }
        if let task = inFlight[key] { return await task.value }
        let task = Task.detached(priority: .utility) { () -> CGImage? in
            guard let (data, response) = try? await URLSession.shared.data(from: url),
                  (response as? HTTPURLResponse)?.statusCode ?? 200 < 400,
                  let source = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
            let options: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceShouldCacheImmediately: true,
                kCGImageSourceThumbnailMaxPixelSize: maxPixel,
            ]
            return CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary)
        }
        inFlight[key] = task
        let image = await task.value
        inFlight[key] = nil
        if let image { cache.setObject(image, forKey: key as NSString) }
        return image
    }
}

/// 16:9 썸네일. 불러오는 동안에는 자리만 잡아 두고, 다 받으면 부드럽게 나타납니다.
struct RemoteImage: View {
    let url: URL?
    var width: CGFloat
    var cornerRadius: CGFloat = Design.thumbnailCorner

    @State private var image: CGImage?
    @Environment(\.displayScale) private var scale

    var body: some View {
        ZStack {
            Rectangle().fill(.quaternary)
            if let image {
                Image(decorative: image, scale: 1)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
                    .transition(.opacity)
            } else {
                Image(systemName: "play.rectangle")
                    .font(.system(size: width * 0.16, weight: .light))
                    .foregroundStyle(.tertiary)
            }
        }
        .frame(width: width, height: width * 9 / 16)
        .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
        .animation(.easeOut(duration: 0.2), value: image != nil)
        .task(id: url) {
            image = nil
            guard let url else { return }
            image = await ImageCache.shared.image(for: url, maxPixel: width * max(scale, 2))
        }
        .accessibilityHidden(true)
    }
}
