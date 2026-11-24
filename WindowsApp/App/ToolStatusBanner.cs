using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YPD.Core;

namespace YPD.App;

/// <summary>필요한 도구가 없을 때만 보이는 안내(InfoBar). 모두 있으면 접혀서 자리를 차지하지 않습니다.</summary>
public sealed partial class ToolStatusBanner : UserControl
{
    private readonly ToolManager tools = ToolManager.Shared;
    private readonly InfoBar bar = new() { IsClosable = false, IsOpen = true };
    private readonly Button install = new();

    /// <summary>사이드바 아래와 화면 안 어디에 두든 같은 모양입니다. InfoBar가 좁은 폭에서는 글을 줄바꿈합니다.</summary>
    public ToolStatusBanner()
    {
        install.Click += async (_, _) => await tools.UpdateYTDLPAsync(AppSettings.Shared);
        bar.ActionButton = install;
        Content = bar;
        Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            tools.PropertyChanged -= OnToolsChanged;
            tools.PropertyChanged += OnToolsChanged;
            Update();
        };
        Unloaded += (_, _) => tools.PropertyChanged -= OnToolsChanged;
    }

    private void OnToolsChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        (InfoBarSeverity, string)? problem =
            tools.IsChecking ? null
            : !tools.IsReady ? (InfoBarSeverity.Warning, L.T("yt-dlp is not installed."))
            : !tools.HasFFmpeg ? (InfoBarSeverity.Warning, L.T("ffmpeg was not found. Merging and converting will fail."))
            : tools.JsRuntimeName == null ? (InfoBarSeverity.Informational, L.T("No JavaScript runtime (deno) was found. Some YouTube formats may be missing."))
            : null;
        Visibility = Ui.Visible(problem != null);
        if (problem is not var (severity, message)) return;
        bar.Severity = severity;
        bar.Message = message;
        install.Visibility = Ui.Visible(!tools.IsReady);
        install.IsEnabled = !tools.IsUpdating;
        install.Content = tools.IsUpdating
            ? new ProgressRing { IsActive = true, Width = 16, Height = 16 }
            : L.T("Install yt-dlp");
    }
}
