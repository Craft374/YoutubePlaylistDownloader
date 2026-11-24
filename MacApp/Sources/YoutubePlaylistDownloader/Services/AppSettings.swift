import AppKit
import Foundation
import Observation
import YPDCore

enum Appearance: String, CaseIterable, Identifiable {
    case system, light, dark
    var id: String { rawValue }
}

/// 화면 언어. 시스템 언어 대신 고를 수 있습니다(다음 실행부터 적용).
enum AppLanguage: String, CaseIterable, Identifiable {
    case system, en, ko
    var id: String { rawValue }
}

/// 앱 설정과 다운로드 옵션. 바뀌는 즉시 UserDefaults에 저장합니다.
@MainActor @Observable
final class AppSettings {
    static let shared = AppSettings()
    private let defaults = UserDefaults.standard

    var appearance: Appearance {
        didSet {
            defaults.set(appearance.rawValue, forKey: "appearance")
            applyAppearance()
        }
    }
    var language: AppLanguage {
        didSet {
            defaults.set(language.rawValue, forKey: "appLanguage")
            // macOS는 앱을 시작할 때 이 값으로 번역 파일을 고릅니다.
            if language == .system {
                defaults.removeObject(forKey: "AppleLanguages")
            } else {
                defaults.set([language.rawValue], forKey: "AppleLanguages")
            }
        }
    }
    var saveDirectory: URL {
        didSet { defaults.set(saveDirectory.path, forKey: "saveDirectory") }
    }
    var maxConcurrentDownloads: Int {
        didSet {
            defaults.set(maxConcurrentDownloads, forKey: "maxConcurrentDownloads")
            DownloadQueue.shared.pump()
        }
    }
    var confirmQuitWhileDownloading: Bool {
        didSet { defaults.set(confirmQuitWhileDownloading, forKey: "confirmQuitWhileDownloading") }
    }
    var autoUpdateYTDLP: Bool {
        didSet { defaults.set(autoUpdateYTDLP, forKey: "autoUpdateYTDLP") }
    }
    var lastYTDLPCheck: Date {
        didSet { defaults.set(lastYTDLPCheck, forKey: "lastYTDLPCheck") }
    }
    var options: DownloadOptions {
        didSet {
            if let data = try? JSONEncoder().encode(options) { defaults.set(data, forKey: "downloadOptions") }
        }
    }

    static let concurrencyRange = 1...6

    /// 앱 전체(창, 설정, 경고창)에 적용합니다. "시스템"이면 macOS 설정을 따릅니다.
    func applyAppearance() {
        switch appearance {
        case .system: NSApp.appearance = nil
        case .light: NSApp.appearance = NSAppearance(named: .aqua)
        case .dark: NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }

    private init() {
        appearance = Appearance(rawValue: defaults.string(forKey: "appearance") ?? "") ?? .system
        language = AppLanguage(rawValue: defaults.string(forKey: "appLanguage") ?? "") ?? .system
        let movies = FileManager.default.urls(for: .moviesDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Movies")
        saveDirectory = defaults.string(forKey: "saveDirectory").map { URL(fileURLWithPath: $0, isDirectory: true) } ?? movies
        let concurrency = defaults.object(forKey: "maxConcurrentDownloads") as? Int ?? 2
        maxConcurrentDownloads = min(max(concurrency, Self.concurrencyRange.lowerBound), Self.concurrencyRange.upperBound)
        confirmQuitWhileDownloading = defaults.object(forKey: "confirmQuitWhileDownloading") as? Bool ?? true
        autoUpdateYTDLP = defaults.object(forKey: "autoUpdateYTDLP") as? Bool ?? true
        lastYTDLPCheck = defaults.object(forKey: "lastYTDLPCheck") as? Date ?? .distantPast
        options = defaults.data(forKey: "downloadOptions").flatMap { try? JSONDecoder().decode(DownloadOptions.self, from: $0) } ?? DownloadOptions()
    }
}
