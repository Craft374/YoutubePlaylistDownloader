using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using YPD.Core;

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

    private readonly Path path = new()
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
        canvas.Children.Add(new Path
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
