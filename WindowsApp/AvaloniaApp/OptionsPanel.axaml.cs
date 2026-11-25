using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>
/// 다운로드 옵션. 바꾸는 즉시 AppSettings가 저장합니다(WinUI OptionsPanel과 같은 흐름).
/// 설정이 바뀌면 Refresh로 모든 컨트롤을 다시 맞추고, 그동안 컨트롤 이벤트가 다시 저장하지 않게 updating으로 막습니다.
/// </summary>
public partial class OptionsPanel : UserControl
{
    private readonly AppSettings settings = AppSettings.Shared;
    private bool updating;

    private static readonly int[] Bitrates = [96, 128, 160, 192, 256, 320];
    private static readonly (string Code, string Name)[] Languages = LanguageList();
    private static readonly MediaEntry Sample = new("dQw4w9WgXcQ", "", "[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]",
                                                    "Monstercat", null, null, 1);

    public OptionsPanel()
    {
        InitializeComponent();
        Fill(Container, ["MP4", "MKV"]);
        Fill(MaxHeightBox, Resolution.Heights.Select(HeightLabel));
        Fill(SubtitleLanguage, Languages.Select(l => l.Name));
        Fill(AudioFormatBox, Enum.GetValues<AudioFormat>().Select(FormatName));
        Fill(Bitrate, Bitrates.Select(b => L.F("{0} kbps", b)));
        Fill(AudioLanguage, Languages.Select(l => l.Name).Prepend(L.T("Default")));
        Fill(FilterLonger, [L.T("Longer than"), L.T("Shorter than")]);

        var tokens = new MenuFlyout();
        foreach (string token in FilenameBuilder.Tokens)
        {
            var item = new MenuItem { Header = $"{L.T(TokenName(token))}  {token}" };
            item.Click += (_, _) => Change(o => o.FilenamePattern += token);
            tokens.Items.Add(item);
        }
        InsertToken.Flyout = tokens;

        Wire();
        Refresh();
        settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSettings.Options) or nameof(AppSettings.SaveDirectory)) Dispatcher.UIThread.Post(Refresh);
        };
    }

    private void Wire()
    {
        ModeVideo.IsCheckedChanged += (_, _) => { if (ModeVideo.IsChecked == true) Change(o => o.Mode = MediaMode.Video); };
        ModeAudio.IsCheckedChanged += (_, _) => { if (ModeAudio.IsChecked == true) Change(o => o.Mode = MediaMode.Audio); };
        ModeOriginal.IsCheckedChanged += (_, _) => { if (ModeOriginal.IsChecked == true) Change(o => o.Mode = MediaMode.AudioOriginal); };
        Container.SelectionChanged += (_, _) => Change(o => o.VideoContainer = (VideoContainer)Container.SelectedIndex);
        MaxHeightBox.SelectionChanged += (_, _) => Change(o => o.MaxHeight = Resolution.Heights[MaxHeightBox.SelectedIndex]);
        PreferFps.IsCheckedChanged += (_, _) => Change(o => o.PreferHighestFPS = PreferFps.IsChecked == true);
        Subtitles.IsCheckedChanged += (_, _) => Change(o => o.DownloadSubtitles = Subtitles.IsChecked == true);
        SubtitleLanguage.SelectionChanged += (_, _) => Change(o => o.SubtitleLanguage = Languages[SubtitleLanguage.SelectedIndex].Code);
        AudioFormatBox.SelectionChanged += (_, _) => Change(o => o.AudioFormat = (AudioFormat)AudioFormatBox.SelectedIndex);
        SetBitrate.IsCheckedChanged += (_, _) => Change(o => o.SetBitrate = SetBitrate.IsChecked == true);
        Bitrate.SelectionChanged += (_, _) => Change(o => o.BitrateKbps = Bitrates[Bitrate.SelectedIndex]);
        TagAudio.IsCheckedChanged += (_, _) => Change(o => o.TagAudio = TagAudio.IsChecked == true);
        AudioLanguage.SelectionChanged += (_, _) =>
            Change(o => o.AudioLanguage = AudioLanguage.SelectedIndex == 0 ? "" : Languages[AudioLanguage.SelectedIndex - 1].Code);

        ChooseFolder.Click += async (_, _) => await Ui.ChooseSaveFolderAsync(this);
        Subfolders.IsCheckedChanged += (_, _) => Change(o => o.SavePlaylistsInSubfolders = Subfolders.IsChecked == true);
        Pattern.TextChanged += (_, _) => { if (Pattern.Text != settings.Options.FilenamePattern) Change(o => o.FilenamePattern = Pattern.Text ?? ""); };
        ResetPattern.Click += (_, _) => Change(o => o.FilenamePattern = DownloadOptions.DefaultPattern);
        SkipExisting.IsCheckedChanged += (_, _) => Change(o => o.SkipExisting = SkipExisting.IsChecked == true);
        OpenFolder.IsCheckedChanged += (_, _) => Change(o => o.OpenFolderWhenDone = OpenFolder.IsChecked == true);

        UseRange.IsCheckedChanged += (_, _) => Change(o => o.UseRange = UseRange.IsChecked == true);
        // 비운 칸(null)은 원래 값으로 되돌립니다.
        RangeStart.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.RangeStart = (int)Math.Clamp(v, 1, int.MaxValue));
        RangeEnd.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.RangeEnd = (int)Math.Clamp(v, 0, int.MaxValue));
        FilterByLength.IsCheckedChanged += (_, _) => Change(o => o.FilterByLength = FilterByLength.IsChecked == true);
        FilterLonger.SelectionChanged += (_, _) => Change(o => o.FilterLonger = FilterLonger.SelectedIndex == 0);
        FilterMinutes.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.FilterMinutes = Math.Max(0, (double)v));

        ResetOptions.Click += (_, _) => settings.Options = new DownloadOptions();
    }

    private void Change(Action<DownloadOptions> change)
    {
        if (!updating) settings.UpdateOptions(change);
    }

    private void ChangeNumber(decimal? value, Func<decimal, Action<DownloadOptions>> change)
    {
        if (updating) return;
        if (value is { } v) settings.UpdateOptions(change(v));
        else Refresh();
    }

    /// <summary>현재 옵션을 모든 컨트롤에 맞추고, 모드에 따라 줄을 보이거나 숨깁니다.</summary>
    private void Refresh()
    {
        var o = settings.Options;
        updating = true;
        try
        {
            (o.Mode switch { MediaMode.Video => ModeVideo, MediaMode.Audio => ModeAudio, _ => ModeOriginal }).IsChecked = true;
            ModeDescription.Text = L.T(o.Mode switch
            {
                MediaMode.Video => "Video with sound, merged into one file",
                MediaMode.Audio => "Audio converted to the format below",
                _ => "Best audio track as is, without converting",
            });

            bool video = o.Mode == MediaMode.Video, audio = o.Mode == MediaMode.Audio;
            bool lossless = o.AudioFormat.IsLossless();
            MediaHeader.Text = L.T(video ? "Video" : "Audio");
            Container.SelectedIndex = (int)o.VideoContainer;
            MaxHeightBox.SelectedIndex = IndexOf(Resolution.Heights, o.MaxHeight);
            PreferFps.IsChecked = o.PreferHighestFPS;
            Subtitles.IsChecked = o.DownloadSubtitles;
            SubtitleLanguage.SelectedIndex = Array.FindIndex(Languages, l => l.Code == o.SubtitleLanguage);
            AudioFormatBox.SelectedIndex = (int)o.AudioFormat;
            SetBitrate.IsChecked = o.SetBitrate;
            SetBitrateRow.IsEnabled = BitrateRow.IsEnabled = !lossless;
            Bitrate.SelectedIndex = IndexOf(Bitrates, o.BitrateKbps);
            TagAudio.IsChecked = o.TagAudio;
            AudioLanguage.SelectedIndex = o.AudioLanguage.Length == 0 ? 0 : Array.FindIndex(Languages, l => l.Code == o.AudioLanguage) + 1;
            ContainerRow.IsVisible = MaxHeightRow.IsVisible = PreferFpsRow.IsVisible = SubtitlesRow.IsVisible = video;
            SubtitleLanguageRow.IsVisible = video && o.DownloadSubtitles;
            AudioFormatRow.IsVisible = SetBitrateRow.IsVisible = audio;
            BitrateRow.IsVisible = audio && o.SetBitrate;
            TagAudioRow.IsVisible = !video;
            string? footer = video ? L.T("MP4 plays in Media Player and on most devices")
                : audio && lossless ? L.T("Lossless formats ignore bitrate") : null;
            MediaFooter.Text = footer ?? "";
            MediaFooter.IsVisible = footer != null;

            string dir = settings.SaveDirectory;
            FolderName.Text = Ui.FolderDisplayName(dir);
            FolderPath.Text = dir;
            ToolTip.SetTip(FolderPath, dir);
            Subfolders.IsChecked = o.SavePlaylistsInSubfolders;
            // 같은 글을 다시 넣으면 입력 중인 커서가 맨 앞으로 갑니다.
            if (Pattern.Text != o.FilenamePattern) Pattern.Text = o.FilenamePattern;
            ResetPattern.IsVisible = o.FilenamePattern != DownloadOptions.DefaultPattern;
            string example = FilenameBuilder.BaseName(o.FilenamePattern, Sample, "Example Playlist");
            Example.Text = L.F("Example: {0}", example + "." + YTDLPArguments.ExpectedExtensions(o).FirstOrDefault());
            SkipExisting.IsChecked = o.SkipExisting;
            OpenFolder.IsChecked = o.OpenFolderWhenDone;

            UseRange.IsChecked = o.UseRange;
            RangeStart.Value = o.RangeStart;
            RangeEnd.Value = o.RangeEnd;
            RangeStartRow.IsVisible = RangeEndRow.IsVisible = RangeFooter.IsVisible = o.UseRange;
            FilterByLength.IsChecked = o.FilterByLength;
            FilterLonger.SelectedIndex = o.FilterLonger ? 0 : 1;
            FilterMinutes.Value = (decimal)o.FilterMinutes;
            LengthRow.IsVisible = o.FilterByLength;

            foreach (var rows in new[] { MediaRows, FileRows, PlaylistRows }) Ui.UpdateDividers(rows);
        }
        finally
        {
            updating = false;
        }
    }

    private static void Fill(ComboBox combo, IEnumerable<string> items)
    {
        foreach (string item in items) combo.Items.Add(item);
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private static string HeightLabel(int height) => height switch
    {
        2160 => "2160p (4K)",
        4320 => "4320p (8K)",
        _ => $"{height}p",
    };

    private static string FormatName(AudioFormat format) => format switch
    {
        AudioFormat.Mp3 => "MP3",
        AudioFormat.M4a => "M4A (AAC)",
        AudioFormat.Opus => "Opus",
        AudioFormat.Ogg => "Ogg Vorbis",
        AudioFormat.Flac => "FLAC",
        AudioFormat.Wav => "WAV",
        _ => "ALAC",
    };

    private static string TokenName(string token) => token switch
    {
        "$title" => "Title",
        "$index" => "Position in the playlist",
        "$artist" => "Artist",
        "$songtitle" => "Song title",
        "$channel" => "Channel",
        "$videoid" => "Video ID",
        "$playlist" => "Playlist name",
        "$genre" => "Genre",
        _ => token,
    };

    private static (string, string)[] LanguageList() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => c.Name.Length == 2)
            .Select(c => (c.Name, c.DisplayName))
            .OrderBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
