using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using YPD.Core;

namespace YPD.App;

/// <summary>
/// XAML 파일 없는 WinUI 앱. 컨트롤 형식 정보는 WinUI가 제공하는 메타데이터 공급자에 넘깁니다
/// (XAML 컴파일러가 만들어 주던 부분입니다).
/// </summary>
public sealed partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider metadata = new();
    private static MainWindow? window;

    public App()
    {
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI exception", e.Exception);
            AppStartup.KillAll();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        Resources.MergedDictionaries.Add(Ui.CreateThemeStyles());
        // 언어를 정하고 대기열이 UI 스레드를 잡도록, 창을 만들기 전에 부릅니다.
        AppStartup.Initialize();
        window = new MainWindow();
        window.Activate();
        _ = StartServicesAsync();
    }

    /// <summary>정리, 도구 확인, 대기열 시작, 자동 업데이트. 시작을 막지 않습니다.</summary>
    private static async Task StartServicesAsync()
    {
        try
        {
            await AppStartup.RunAsync();
        }
        catch (Exception e)
        {
            CrashLog.Write("Startup", e);
        }
    }

    /// <summary>두 번째 실행이 보낸 활성화. 백그라운드 스레드에서 오므로 UI 스레드로 넘깁니다.</summary>
    public static void BringToFront() => window?.DispatcherQueue.TryEnqueue(() => window.BringToFront());

    public IXamlType GetXamlType(Type type) => metadata.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => metadata.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => metadata.GetXmlnsDefinitions();
}
