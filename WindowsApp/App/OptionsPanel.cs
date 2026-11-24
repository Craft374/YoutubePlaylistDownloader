using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YPD.Core;

namespace YPD.App;

/// <summary>
/// 다운로드 옵션 패널(Mac OptionsInspector). 바꾸는 즉시 AppSettings가 저장합니다.
/// 설정이 바뀌면 Refresh로 모든 컨트롤을 다시 맞춥니다. 그동안 컨트롤 이벤트가 다시 저장하지 않게 updating으로 막습니다.
/// </summary>
public sealed partial class OptionsPanel : UserControl
{
    private readonly AppSettings settings = AppSettings.Shared;
    private bool updating;

    private static readonly int[] Bitrates = [96, 128, 160, 192, 256, 320];
    private static readonly (string Code, string Name)[] Languages = LanguageList();
    private static readonly MediaEntry Sample = new("dQw4w9WgXcQ", "", "[Dubstep] Infected Mushroom - Spitfire [Monstercat Release]",
                                                    "Monstercat", null, null, 1);

    // 형식
    private readonly RadioButtons mode = new();
    private readonly TextBlock modeDescription = Ui.Caption();
    // 동영상·오디오(모드에 따라 보이는 것만 다름. 요소는 부모가 하나뿐이라 한 목록에 두고 숨깁니다.)
    private readonly TextBlock mediaHeader = Ui.SectionHeader("");
    private readonly ComboBox container = Combo(L.T("Container"), new[] { "MP4", "MKV" });
    private readonly ComboBox maxHeight = Combo(L.T("Max resolution"), Resolution.Heights.Select(HeightLabel));
    private readonly CheckBox preferFPS = Check(L.T("Prefer higher frame rate"));
    private readonly CheckBox subtitles = Check(L.T("Download subtitles"));
    private readonly ComboBox subtitleLanguage = Combo(L.T("Subtitle language"), Languages.Select(l => l.Name));
    private readonly ComboBox audioFormat = Combo(L.T("Format"), Enum.GetValues<AudioFormat>().Select(FormatName));
    private readonly CheckBox setBitrate = Check(L.T("Set bitrate"));
    private readonly ComboBox bitrate = Combo(L.T("Bitrate"), Bitrates.Select(b => L.F("{0} kbps", b)));
    private readonly CheckBox tagAudio = Check(L.T("Add tags and cover art"));
    private readonly ComboBox audioLanguage = Combo(L.T("Audio language"), Languages.Select(l => l.Name).Prepend(L.T("Default")));
    private readonly TextBlock mediaFooter = Ui.Caption();
    // 파일
    private readonly CheckBox subfolders = Check(L.T("Save each playlist in its own folder"));
    private readonly TextBox pattern = new() { FontFamily = Ui.Monospace, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button resetPattern;
    private readonly TextBlock example = Ui.Caption();
    private readonly CheckBox skipExisting = Check(L.T("Skip files that already exist"));
    private readonly CheckBox openFolder = Check(L.T("Open folder when done"));
    // 재생목록
    private readonly CheckBox useRange = Check(L.T("Only download a range"));
    private readonly NumberBox rangeStart = Number(L.T("From"), 1);
    private readonly NumberBox rangeEnd = Number(L.T("To"), 0);
    private readonly StackPanel rangeRow;
    private readonly TextBlock rangeFooter = Ui.Caption(L.T("Leave To at 0 to download until the end"));
    private readonly CheckBox filterByLength = Check(L.T("Only download videos by length"));
    private readonly ComboBox filterLonger = Combo(null, new[] { L.T("Longer than"), L.T("Shorter than") });
    private readonly NumberBox filterMinutes = Number(null, 0);
    private readonly StackPanel lengthRow;

    public OptionsPanel()
    {
        foreach (string name in new[] { "Video", "Audio", "Original Audio" }) mode.Items.Add(L.T(name));
        ToolTipService.SetToolTip(tagAudio, L.T("Artist and title are read from the video title"));
        pattern.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L.T("File name"));
        resetPattern = Ui.IconButton("\uE7A7", L.T("Reset to $title"), (_, _) => Change(o => o.FilenamePattern = DownloadOptions.DefaultPattern));
        filterMinutes.Width = 90;
        filterMinutes.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L.T("Minutes"));
        filterMinutes.SmallChange = 1;
        rangeStart.Width = rangeEnd.Width = 110;
        rangeRow = Ui.Row(Design.Spacing, rangeStart, rangeEnd);
        filterLonger.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L.T("Length"));
        var minutesLabel = Ui.Text(L.T("min"), "YpdSecondaryText");
        minutesLabel.VerticalAlignment = VerticalAlignment.Center;
        lengthRow = Ui.Row(8, filterLonger, filterMinutes, minutesLabel);

        var reset = new Button { Content = L.T("Reset Options"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        reset.Click += (_, _) => settings.Options = new DownloadOptions();

        var stack = Ui.Column(Design.Spacing,
            Ui.SectionHeader(L.T("Format")), mode, modeDescription,
            mediaHeader, container, maxHeight, preferFPS, subtitles, subtitleLanguage,
            audioFormat, setBitrate, bitrate, tagAudio, audioLanguage, mediaFooter,
            Ui.SectionHeader(L.T("Files")), new SaveFolderRow(), subfolders, FileNameEditor(), example, skipExisting, openFolder,
            Ui.SectionHeader(L.T("Playlist")), useRange, rangeRow, rangeFooter, filterByLength, lengthRow,
            reset);
        stack.Padding = new Thickness(16, 0, 16, 16);
        Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Wire();
        Refresh();
        settings.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppSettings.Options)) Refresh(); };
    }

    /// <summary>파일 이름 입력란 + 초기화 + 토큰 넣기 메뉴.</summary>
    private Grid FileNameEditor()
    {
        var tokens = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (string token in FilenameBuilder.Tokens)
        {
            var item = new MenuFlyoutItem { Text = $"{L.T(TokenName(token))}  {token}" };
            item.Click += (_, _) => Change(o => o.FilenamePattern += token);
            tokens.Items.Add(item);
        }
        var insert = new DropDownButton { Content = Ui.Icon("\uE710"), Flyout = tokens, Padding = new Thickness(8, 6, 8, 6) };
        insert.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L.T("Insert a token"));
        ToolTipService.SetToolTip(insert, L.T("Insert a token"));

        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = Ui.Text(L.T("File name"));
        Grid.SetColumnSpan(label, 3);
        Grid.SetRow(pattern, 1);
        Grid.SetRow(resetPattern, 1);
        Grid.SetColumn(resetPattern, 1);
        Grid.SetRow(insert, 1);
        Grid.SetColumn(insert, 2);
        grid.Children.Add(label);
        grid.Children.Add(pattern);
        grid.Children.Add(resetPattern);
        grid.Children.Add(insert);
        return grid;
    }

    private void Wire()
    {
        mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex >= 0) Change(o => o.Mode = (MediaMode)mode.SelectedIndex); };
        container.SelectionChanged += (_, _) => Change(o => o.VideoContainer = (VideoContainer)container.SelectedIndex);
        maxHeight.SelectionChanged += (_, _) => Change(o => o.MaxHeight = Resolution.Heights[maxHeight.SelectedIndex]);
        preferFPS.Click += (_, _) => Change(o => o.PreferHighestFPS = preferFPS.IsChecked == true);
        subtitles.Click += (_, _) => Change(o => o.DownloadSubtitles = subtitles.IsChecked == true);
        subtitleLanguage.SelectionChanged += (_, _) => Change(o => o.SubtitleLanguage = Languages[subtitleLanguage.SelectedIndex].Code);
        audioFormat.SelectionChanged += (_, _) => Change(o => o.AudioFormat = (AudioFormat)audioFormat.SelectedIndex);
        setBitrate.Click += (_, _) => Change(o => o.SetBitrate = setBitrate.IsChecked == true);
        bitrate.SelectionChanged += (_, _) => Change(o => o.BitrateKbps = Bitrates[bitrate.SelectedIndex]);
        tagAudio.Click += (_, _) => Change(o => o.TagAudio = tagAudio.IsChecked == true);
        audioLanguage.SelectionChanged += (_, _) =>
            Change(o => o.AudioLanguage = audioLanguage.SelectedIndex == 0 ? "" : Languages[audioLanguage.SelectedIndex - 1].Code);
        subfolders.Click += (_, _) => Change(o => o.SavePlaylistsInSubfolders = subfolders.IsChecked == true);
        // TextChanged는 Refresh가 끝난 뒤에 와서 updating으로 막히지 않습니다. 같은 글이면 넘깁니다.
        pattern.TextChanged += (_, _) => { if (pattern.Text != settings.Options.FilenamePattern) Change(o => o.FilenamePattern = pattern.Text); };
        skipExisting.Click += (_, _) => Change(o => o.SkipExisting = skipExisting.IsChecked == true);
        openFolder.Click += (_, _) => Change(o => o.OpenFolderWhenDone = openFolder.IsChecked == true);
        useRange.Click += (_, _) => Change(o => o.UseRange = useRange.IsChecked == true);
        // 잘못된 값은 바로잡아 저장합니다. 비운 칸(NaN)은 원래 값으로 되돌립니다.
        rangeStart.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.RangeStart = Math.Max(1, (int)v));
        rangeEnd.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.RangeEnd = Math.Max(0, (int)v));
        filterByLength.Click += (_, _) => Change(o => o.FilterByLength = filterByLength.IsChecked == true);
        filterLonger.SelectionChanged += (_, _) => Change(o => o.FilterLonger = filterLonger.SelectedIndex == 0);
        filterMinutes.ValueChanged += (_, e) => ChangeNumber(e.NewValue, v => o => o.FilterMinutes = Math.Max(0, v));
    }

    private void Change(Action<DownloadOptions> change)
    {
        if (!updating) settings.UpdateOptions(change);
    }

    private void ChangeNumber(double value, Func<double, Action<DownloadOptions>> change)
    {
        if (updating) return;
        if (double.IsFinite(value)) settings.UpdateOptions(change(value));
        else Refresh();
    }

    /// <summary>현재 옵션을 모든 컨트롤에 맞추고, 조건에 따라 보이거나 끕니다.</summary>
    private void Refresh()
    {
        var o = settings.Options;
        updating = true;
        try
        {
            mode.SelectedIndex = (int)o.Mode;
            modeDescription.Text = L.T(o.Mode switch
            {
                MediaMode.Video => "Video with sound, merged into one file",
                MediaMode.Audio => "Audio converted to the format below",
                _ => "Best audio track as is, without converting",
            });

            bool video = o.Mode == MediaMode.Video, audio = o.Mode == MediaMode.Audio;
            bool lossless = o.AudioFormat.IsLossless();
            mediaHeader.Text = L.T(video ? "Video" : "Audio");
            container.SelectedIndex = (int)o.VideoContainer;
            maxHeight.SelectedIndex = IndexOf(Resolution.Heights, o.MaxHeight);
            preferFPS.IsChecked = o.PreferHighestFPS;
            subtitles.IsChecked = o.DownloadSubtitles;
            subtitleLanguage.SelectedIndex = Array.FindIndex(Languages, l => l.Code == o.SubtitleLanguage);
            audioFormat.SelectedIndex = (int)o.AudioFormat;
            setBitrate.IsChecked = o.SetBitrate;
            setBitrate.IsEnabled = bitrate.IsEnabled = !lossless;
            bitrate.SelectedIndex = IndexOf(Bitrates, o.BitrateKbps);
            tagAudio.IsChecked = o.TagAudio;
            audioLanguage.SelectedIndex = o.AudioLanguage.Length == 0 ? 0 : Array.FindIndex(Languages, l => l.Code == o.AudioLanguage) + 1;
            foreach (var c in new UIElement[] { container, maxHeight, preferFPS, subtitles }) c.Visibility = Ui.Visible(video);
            subtitleLanguage.Visibility = Ui.Visible(video && o.DownloadSubtitles);
            audioFormat.Visibility = setBitrate.Visibility = Ui.Visible(audio);
            bitrate.Visibility = Ui.Visible(audio && o.SetBitrate);
            tagAudio.Visibility = Ui.Visible(!video);
            string? footer = video ? L.T("MP4 plays in Media Player and on most devices")
                : audio && lossless ? L.T("Lossless formats ignore bitrate") : null;
            mediaFooter.Text = footer ?? "";
            mediaFooter.Visibility = Ui.Visible(footer != null);

            subfolders.IsChecked = o.SavePlaylistsInSubfolders;
            // 같은 글을 다시 넣으면 입력 중인 커서가 맨 앞으로 갑니다.
            if (pattern.Text != o.FilenamePattern) pattern.Text = o.FilenamePattern;
            resetPattern.Visibility = Ui.Visible(o.FilenamePattern != DownloadOptions.DefaultPattern);
            string name = FilenameBuilder.BaseName(o.FilenamePattern, Sample, "Example Playlist");
            example.Text = L.F("Example: {0}", name + "." + YTDLPArguments.ExpectedExtensions(o).FirstOrDefault());
            skipExisting.IsChecked = o.SkipExisting;
            openFolder.IsChecked = o.OpenFolderWhenDone;

            useRange.IsChecked = o.UseRange;
            rangeStart.Value = o.RangeStart;
            rangeEnd.Value = o.RangeEnd;
            rangeRow.Visibility = rangeFooter.Visibility = Ui.Visible(o.UseRange);
            filterByLength.IsChecked = o.FilterByLength;
            filterLonger.SelectedIndex = o.FilterLonger ? 0 : 1;
            filterMinutes.Value = o.FilterMinutes;
            lengthRow.Visibility = Ui.Visible(o.FilterByLength);
        }
        finally
        {
            updating = false;
        }
    }

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == value) return i;
        return -1;
    }

    private static ComboBox Combo(string? header, IEnumerable<string> items)
    {
        var combo = new ComboBox { Header = header, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string item in items) combo.Items.Add(item);
        return combo;
    }

    private static CheckBox Check(string text) => new() { Content = text };

    private static NumberBox Number(string? header, double minimum) => new()
    {
        Header = header,
        Minimum = minimum,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
    };

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

    /// <summary>두 글자 ISO 언어 코드와 그 이름(시스템 언어로), 이름순.</summary>
    private static (string, string)[] LanguageList() =>
        CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .Where(c => c.Name.Length == 2)
            .Select(c => (c.Name, c.DisplayName))
            .OrderBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
