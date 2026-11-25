using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using YPD.Core;
using Path = System.IO.Path;
using Shape = Avalonia.Controls.Shapes.Path;

namespace YPD.Desktop;

/// <summary>화면 문구: <c>Text="{local:Tr 'New Download'}"</c>. Core의 번역 표(L)를 그대로 씁니다.</summary>
public sealed class Tr(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(key);
}

/// <summary>24×24 격자의 선 아이콘(App.axaml의 Icon* 도형). 크기는 Width·Height로, 색은 Stroke로 정합니다.</summary>
public sealed class Icon : Viewbox
{
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<Icon, IBrush?>(nameof(Stroke));

    private readonly Shape path = new()
    {
        Width = 24,
        Height = 24,
        StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
    };

    public Icon()
    {
        Width = Height = 18;
        Child = path;
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataProperty) path.Data = Data;
        else if (change.Property == StrokeProperty) path.Stroke = Stroke;
    }
}

/// <summary>앱 로고(MacApp LogoView, AppIcon.svg와 같은 도형 6개).</summary>
public sealed class Logo : Viewbox
{
    public Logo()
    {
        Width = Height = 56;
        var canvas = new Canvas { Width = 1024, Height = 1024 };
        var top = Color.Parse("#FF6A5C");
        var bottom = Color.Parse("#E5306B");
        Add(canvas, new Rectangle
        {
            Width = 824, Height = 824, RadiusX = 186, RadiusY = 186,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) },
            },
        }, 100, 100);
        foreach (var (y, width, opacity) in new[] { (276.0, 400.0, 1.0), (404.0, 320.0, 0.78), (532.0, 240.0, 0.56) })
            Add(canvas, new Rectangle { Width = width, Height = 64, RadiusX = 32, RadiusY = 32, Fill = Brushes.White, Opacity = opacity }, 236, y);
        Add(canvas, new Ellipse { Width = 320, Height = 320, Fill = Brushes.White }, 492, 492);
        canvas.Children.Add(new Shape
        {
            Data = Geometry.Parse("M652,568 L652,722 M590,664 L652,726 L714,664"),
            Stroke = new SolidColorBrush(bottom),
            StrokeThickness = 48,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        });
        Child = canvas;
    }

    private static void Add(Canvas canvas, Control shape, double left, double top)
    {
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        canvas.Children.Add(shape);
    }
}

/// <summary>썸네일을 받아 Image에 넣습니다. 같은 주소는 한 번만 받습니다.</summary>
public static class RemoteImage
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Dictionary<Uri, Task<Bitmap?>> Cache = [];

    public static async void Load(Image image, Uri? url)
    {
        image.Source = null;
        image.Tag = url;
        if (url == null) return;
        if (!Cache.TryGetValue(url, out var task)) Cache[url] = task = FetchAsync(url);
        var bitmap = await task;
        if (bitmap == null) Cache.Remove(url); // 실패는 기억하지 않고 다음에 다시 받습니다.
        // 받는 사이 다른 결과로 바뀌었으면 넣지 않습니다.
        if (image.Tag as Uri == url) image.Source = bitmap;
    }

    private static async Task<Bitmap?> FetchAsync(Uri url)
    {
        try
        {
            await using var stream = new MemoryStream(await Http.GetByteArrayAsync(url));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null; // 썸네일이 없어도 자리 표시가 남습니다.
        }
    }
}

/// <summary>
/// 움직임. 손으로 던지는 화면이 없어 튀지 않는(임계 감쇠) 스프링만 씁니다.
/// "동작 줄이기"를 켜면 미끄러짐을 빼고 짧게 나타나기만 합니다.
/// </summary>
public static class Motion
{
    /// <summary>임계 감쇠 스프링 곡선: 1 − (1 + ωt)·e^(−ωt). 응답 시간을 전환 길이의 0.7배로 잡아 끝에서 0.1% 안으로 멈춥니다.</summary>
    public sealed class SpringEasing : Easing
    {
        private const double K = 2 * Math.PI / 0.7;
        private static readonly double End = 1 - (1 + K) * Math.Exp(-K);

        public override double Ease(double progress) => (1 - (1 + K * progress) * Math.Exp(-K * progress)) / End;
    }

    public static readonly Easing Spring = new SpringEasing();

    /// <summary>옵션 패널처럼 화면 구역이 들고 나는 길이(응답 0.35초).</summary>
    public static readonly TimeSpan Move = TimeSpan.FromMilliseconds(500);

    public static bool Reduced => SystemPreferences.ReduceMotion;

    /// <summary>나타나는 카드: 6px 아래에서 떠오르며 보입니다. 동작 줄이기면 나타나기만 합니다.</summary>
    public static void Appear(Control control)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Reduced ? 150 : 350),
            Easing = Reduced ? new LinearEasing() : Spring,
            Children =
            {
                Frame(0, 0, Reduced ? null : "translateY(6px)"),
                Frame(1, 1, Reduced ? null : "none"),
            },
        };
        _ = animation.RunAsync(control);
    }

    /// <summary>화면 전환: 짧게 겹쳐 나타납니다(미끄러지지 않음).</summary>
    public static void FadeIn(Control control) => _ = new Animation
    {
        Duration = TimeSpan.FromMilliseconds(160),
        Children = { Frame(0, 0, null), Frame(1, 1, null) },
    }.RunAsync(control);

    /// <summary>보이게 바뀔 때만 나타나는 움직임을 붙입니다.</summary>
    public static void SetVisible(Control control, bool visible)
    {
        if (control.IsVisible == visible) return;
        control.IsVisible = visible;
        if (visible) Appear(control);
    }

    private static KeyFrame Frame(double cue, double opacity, string? transform)
    {
        var frame = new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(Visual.OpacityProperty, opacity) } };
        if (transform != null) frame.Setters.Add(new Setter(Visual.RenderTransformProperty, TransformOperations.Parse(transform)));
        return frame;
    }
}

/// <summary>
/// OS 손쉬운 사용 설정(동작 줄이기, 투명도 줄이기). 시작할 때 한 번 읽습니다.
/// shortcut: 앱이 떠 있는 동안 설정을 바꾸면 다시 열어야 적용됩니다. 실행 중 반영이 필요하면 OS 알림을 구독하십시오.
/// </summary>
public static class SystemPreferences
{
    public static bool ReduceMotion { get; } = Read(windows: () => !WindowsFlag(0x1042), mac: "reduceMotion"); // SPI_GETCLIENTAREAANIMATION
    public static bool ReduceTransparency { get; } = Read(windows: () => OperatingSystem.IsWindows() && !WindowsTransparency(), mac: "reduceTransparency");

    private static bool Read(Func<bool> windows, string mac)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return windows();
            if (!OperatingSystem.IsMacOS()) return false;
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/defaults", ["read", "com.apple.universalaccess", mac])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process == null) return false;
            string value = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return value == "1";
        }
        catch (Exception)
        {
            return false; // 읽지 못하면 기본(움직임·투명도 켬)으로 둡니다.
        }
    }

    private static bool WindowsFlag(uint action) => SystemParametersInfoW(action, 0, out int value, 0) && value != 0;

    /// <summary>설정 → 개인 설정 → 색 → 투명 효과.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool WindowsTransparency() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is not 0;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SystemParametersInfoW(uint action, uint param, out int value, uint winIni);
}

/// <summary>여러 화면이 같이 쓰는 작은 동작.</summary>
public static class Ui
{
    /// <summary>카드 안에서 보이는 줄 사이에만 구분선을 긋습니다(맨 위 줄은 카드 테두리와 겹치므로 뺍니다).</summary>
    public static void UpdateDividers(Panel rows)
    {
        bool first = true;
        foreach (var row in rows.Children)
        {
            if (!row.IsVisible) continue;
            row.Classes.Set("divided", !first);
            first = false;
        }
    }

    public static async Task CopyAsync(Visual anchor, string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(anchor)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
        }
        catch (Exception)
        {
            // 클립보드를 쓰지 못하면 다시 누르면 됩니다.
        }
    }

    /// <summary>글을 .txt로 저장합니다. 실패하면 이유를 대화 상자로 알립니다.</summary>
    public static async Task SaveTextAsync(Visual anchor, string suggestedName, string text, string? folder = null)
    {
        try
        {
            if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { } storage) return;
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = suggestedName,
                DefaultExtension = "txt",
                FileTypeChoices = [new FilePickerFileType(L.T("Text document")) { Patterns = ["*.txt"] }],
                SuggestedStartLocation = folder != null && Directory.Exists(folder) ? await storage.TryGetFolderFromPathAsync(folder) : null,
            });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }
        catch (Exception e)
        {
            if (TopLevel.GetTopLevel(anchor) is Window owner) await MessageDialog.ShowAsync(owner, L.T("Couldn’t save the file"), e.Message);
        }
    }

    /// <summary>저장 폴더를 고릅니다.</summary>
    public static async Task ChooseSaveFolderAsync(Visual anchor)
    {
        var settings = AppSettings.Shared;
        try
        {
            if (TopLevel.GetTopLevel(anchor)?.StorageProvider is not { } storage) return;
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = L.T("Save to"),
                SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(settings.SaveDirectory),
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { Length: > 0 } path) settings.SaveDirectory = path;
        }
        catch (Exception e)
        {
            if (TopLevel.GetTopLevel(anchor) is Window owner) await MessageDialog.ShowAsync(owner, L.T("Save to"), e.Message);
        }
    }

    /// <summary>폴더 이름("Videos"). 드라이브 루트면 경로 그대로입니다.</summary>
    public static string FolderDisplayName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;

    /// <summary>작업 폴더를 엽니다. 파일 하나짜리 작업이면 그 파일을 선택해 보여 줍니다.</summary>
    public static void ShowInExplorer(DownloadJob job, DownloadItem? item = null)
    {
        item ??= job.Items.Count == 1 ? job.Items[0] : null;
        if (item?.FilePath is { } file && File.Exists(file)) Explorer.RevealFile(file);
        else Explorer.OpenFolder(job.Directory);
    }
}
