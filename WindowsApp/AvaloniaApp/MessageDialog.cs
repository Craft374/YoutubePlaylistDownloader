using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>
/// 작은 확인 창(제목, 설명, 선택 체크 상자, 버튼). 기본 버튼은 Enter, 취소는 Esc입니다.
/// 되돌릴 수 없는 일(다운로드 중 종료)과 오류 알림에만 씁니다.
/// </summary>
public sealed class MessageDialog : Window
{
    private bool result;

    private MessageDialog(string title, string message, string primary, string? cancel, CheckBox? check)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        CanMinimize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("YpdWindowBackground"));

        var ok = new Button { Content = primary, Classes = { "brand" }, IsDefault = true, MinWidth = 88 };
        ok.Click += (_, _) => { result = true; Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancel != null)
        {
            var no = new Button { Content = cancel, IsCancel = true, MinWidth = 88 };
            no.Click += (_, _) => Close();
            buttons.Children.Add(no);
        }
        buttons.Children.Add(ok);

        var body = new StackPanel { Spacing = 8, Margin = new Thickness(24, 22, 24, 20), MaxWidth = 380 };
        body.Children.Add(new TextBlock { Text = title, Classes = { "headline" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new SelectableTextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "secondary" } });
        if (check != null) body.Children.Add(check);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(buttons);
        Content = body;
        // 취소 버튼이 없는 알림도 Esc로 닫습니다.
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Opened += (_, _) => ok.Focus();
    }

    /// <summary>오류 알림(확인 버튼 하나).</summary>
    public static Task ShowAsync(Window owner, string title, string message) =>
        new MessageDialog(title, message, L.T("OK"), null, null).ShowDialog(owner);

    /// <summary>다운로드 중 종료 확인. "다시 묻지 않기"는 Mac처럼 어느 버튼을 눌러도 적용합니다.</summary>
    public static async Task<bool> ConfirmQuitAsync(Window owner)
    {
        var dontAsk = new CheckBox { Content = L.T("Don’t ask again"), Margin = new Thickness(0, 6, 0, 0) };
        var dialog = new MessageDialog(L.T("Downloads are in progress"),
                                       L.T("Quitting will stop all downloads. Unfinished files will not be saved."),
                                       L.T("Quit"), L.T("Cancel"), dontAsk);
        await dialog.ShowDialog(owner);
        if (dontAsk.IsChecked == true) AppSettings.Shared.ConfirmQuitWhileDownloading = false;
        return dialog.result;
    }
}
