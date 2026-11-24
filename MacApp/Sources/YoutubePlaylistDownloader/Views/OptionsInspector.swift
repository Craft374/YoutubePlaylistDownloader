import SwiftUI
import YPDCore

/// 다운로드 옵션 인스펙터. 바꾸는 즉시 AppSettings가 저장합니다.
struct OptionsInspector: View {
    @Environment(AppSettings.self) private var settings

    var body: some View {
        @Bindable var settings = settings
        Form {
            Section("Format") {
                Picker("Format", selection: $settings.options.mode) {
                    Text("Video").tag(MediaMode.video)
                    Text("Audio").tag(MediaMode.audio)
                    Text("Original Audio").tag(MediaMode.audioOriginal)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                Text(modeDescription)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            switch settings.options.mode {
            case .video: VideoSection(options: $settings.options)
            case .audio: AudioSection(options: $settings.options)
            case .audioOriginal:
                Section("Audio") {
                    Toggle("Add tags and cover art", isOn: $settings.options.tagAudio)
                        .help("Artist and title are read from the video title")
                    AudioLanguagePicker(selection: $settings.options.audioLanguage)
                }
            }

            FilesSection(options: $settings.options)
            PlaylistSection(options: $settings.options)

            Section {
                Button("Reset Options") { settings.options = DownloadOptions() }
                    .frame(maxWidth: .infinity, alignment: .trailing)
            }
        }
        .formStyle(.grouped)
        .adaptiveAnimation(settings.options.mode)
    }

    private var modeDescription: LocalizedStringKey {
        switch settings.options.mode {
        case .video: "Video with sound, merged into one file"
        case .audio: "Audio converted to the format below"
        case .audioOriginal: "Best audio track as is, without converting"
        }
    }
}

private struct VideoSection: View {
    @Binding var options: DownloadOptions

    var body: some View {
        Section {
            Picker("Container", selection: $options.videoContainer) {
                Text(verbatim: "MP4").tag(VideoContainer.mp4)
                Text(verbatim: "MKV").tag(VideoContainer.mkv)
            }
            Picker("Max resolution", selection: $options.maxHeight) {
                ForEach(Resolution.heights, id: \.self) { height in
                    Text(verbatim: Self.label(height)).tag(height)
                }
            }
            Toggle("Prefer higher frame rate", isOn: $options.preferHighestFPS)
            Toggle("Download subtitles", isOn: $options.downloadSubtitles)
            if options.downloadSubtitles {
                Picker("Subtitle language", selection: $options.subtitleLanguage) {
                    ForEach(Language.all) { Text(verbatim: $0.name).tag($0.code) }
                }
            }
            AudioLanguagePicker(selection: $options.audioLanguage)
        } header: {
            Text("Video")
        } footer: {
            Text("MP4 plays in QuickTime and on iPhone")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
    }

    private static func label(_ height: Int) -> String {
        switch height {
        case 2160: "2160p (4K)"
        case 4320: "4320p (8K)"
        default: "\(height)p"
        }
    }
}

private struct AudioSection: View {
    @Binding var options: DownloadOptions

    var body: some View {
        Section {
            Picker("Format", selection: $options.audioFormat) {
                ForEach(AudioFormat.allCases, id: \.self) { Text(verbatim: Self.name($0)).tag($0) }
            }
            Group {
                Toggle("Set bitrate", isOn: $options.setBitrate)
                if options.setBitrate {
                    Picker("Bitrate", selection: $options.bitrateKbps) {
                        ForEach([96, 128, 160, 192, 256, 320], id: \.self) { Text("\($0) kbps").tag($0) }
                    }
                }
            }
            .disabled(options.audioFormat.isLossless)
            Toggle("Add tags and cover art", isOn: $options.tagAudio)
                .help("Artist and title are read from the video title")
            AudioLanguagePicker(selection: $options.audioLanguage)
        } header: {
            Text("Audio")
        } footer: {
            if options.audioFormat.isLossless {
                Text("Lossless formats ignore bitrate")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private static func name(_ format: AudioFormat) -> String {
        switch format {
        case .mp3: "MP3"
        case .m4a: "M4A (AAC)"
        case .opus: "Opus"
        case .ogg: "Ogg Vorbis"
        case .flac: "FLAC"
        case .wav: "WAV"
        case .alac: "ALAC"
        }
    }
}

private struct FilesSection: View {
    @Binding var options: DownloadOptions

    private static let sample = MediaEntry(videoID: "dQw4w9WgXcQ", url: "",
                                           title: "[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]",
                                           channel: "Monstercat", duration: nil, thumbnail: nil, index: 1)

    var body: some View {
        Section("Files") {
            SaveFolderRow()
            Toggle("Save each playlist in its own folder", isOn: $options.savePlaylistsInSubfolders)
            VStack(alignment: .leading, spacing: 6) {
                LabeledContent("File name") {
                    HStack(spacing: 6) {
                        TextField("File name", text: $options.filenamePattern)
                            .labelsHidden()
                            .font(.body.monospaced())
                        if options.filenamePattern != DownloadOptions.defaultPattern {
                            Button {
                                options.filenamePattern = DownloadOptions.defaultPattern
                            } label: {
                                Image(systemName: "arrow.counterclockwise")
                            }
                            .buttonStyle(.borderless)
                            .help("Reset to $title")
                            .accessibilityLabel("Reset")
                        }
                        Menu {
                            ForEach(FilenameBuilder.tokens, id: \.self) { token in
                                Button {
                                    options.filenamePattern += token
                                } label: {
                                    Text(Self.tokenName(token)) + Text(verbatim: "  \(token)")
                                }
                            }
                        } label: {
                            Image(systemName: "plus.circle")
                        }
                        .menuStyle(.borderlessButton)
                        .menuIndicator(.hidden)
                        .fixedSize()
                        .help("Insert a token")
                        .accessibilityLabel("Insert a token")
                    }
                }
                Text("Example: \(example)")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
                    .truncationMode(.middle)
            }
            Toggle("Skip files that already exist", isOn: $options.skipExisting)
            Toggle("Open folder when done", isOn: $options.openFolderWhenDone)
        }
    }

    private var example: String {
        let name = FilenameBuilder.baseName(pattern: options.filenamePattern, entry: Self.sample, collectionTitle: "Example Playlist")
        let ext = YTDLPArguments.expectedExtensions(options).first ?? ""
        return name + "." + ext
    }

    private static func tokenName(_ token: String) -> LocalizedStringKey {
        switch token {
        case "$title": "Title"
        case "$index": "Position in the playlist"
        case "$artist": "Artist"
        case "$songtitle": "Song title"
        case "$channel": "Channel"
        case "$videoid": "Video ID"
        case "$playlist": "Playlist name"
        case "$genre": "Genre"
        default: LocalizedStringKey(token)
        }
    }
}

private struct PlaylistSection: View {
    @Binding var options: DownloadOptions

    var body: some View {
        Section {
            Toggle("Only download a range", isOn: $options.useRange)
            if options.useRange {
                LabeledContent("From") {
                    NumberField(title: "From", value: $options.rangeStart)
                }
                LabeledContent("To") {
                    NumberField(title: "To", value: $options.rangeEnd)
                }
            }
            Toggle("Only download videos by length", isOn: $options.filterByLength)
            if options.filterByLength {
                HStack {
                    Picker("Length", selection: $options.filterLonger) {
                        Text("Longer than").tag(true)
                        Text("Shorter than").tag(false)
                    }
                    .pickerStyle(.menu)
                    .labelsHidden()
                    .fixedSize()
                    Spacer()
                    TextField("Minutes", value: $options.filterMinutes, format: .number)
                        .labelsHidden()
                        .multilineTextAlignment(.trailing)
                        .frame(width: 70)
                    Text("min")
                        .foregroundStyle(.secondary)
                }
            }
        } header: {
            Text("Playlist")
        } footer: {
            if options.useRange {
                Text("Leave To at 0 to download until the end")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        // 잘못된 값은 저장 직후 바로잡습니다. 값이 바뀌어야 입력란도 다시 그려집니다.
        .onChange(of: options.rangeStart) { if options.rangeStart < 1 { options.rangeStart = 1 } }
        .onChange(of: options.rangeEnd) { if options.rangeEnd < 0 { options.rangeEnd = 0 } }
        .onChange(of: options.filterMinutes) { if options.filterMinutes < 0 { options.filterMinutes = 0 } }
    }
}

private struct NumberField: View {
    let title: LocalizedStringKey
    @Binding var value: Int

    var body: some View {
        TextField(title, value: $value, format: .number)
            .labelsHidden()
            .multilineTextAlignment(.trailing)
            .frame(width: 70)
    }
}

private struct AudioLanguagePicker: View {
    @Binding var selection: String

    var body: some View {
        Picker("Audio language", selection: $selection) {
            Text("Default").tag("")
            ForEach(Language.all) { Text(verbatim: $0.name).tag($0.code) }
        }
    }
}

private struct Language: Identifiable, Sendable {
    let code: String
    let name: String
    var id: String { code }

    /// 처음 열 때 한 번만 만듭니다.
    static let all: [Language] = Locale.LanguageCode.isoLanguageCodes
        .map(\.identifier)
        .filter { $0.count == 2 }
        .compactMap { code in Locale.current.localizedString(forLanguageCode: code).map { Language(code: code, name: $0) } }
        .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
}
