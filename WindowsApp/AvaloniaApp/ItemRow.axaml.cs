using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using YPD.Core;

namespace YPD.Desktop;

/// <summary>항목 한 줄. JobRow처럼 화면에 붙어 있을 때만 자기 항목을 구독합니다.</summary>
public partial class ItemRow : UserControl
{
    private DownloadItem? item;
    private bool attached;
    private readonly ContextMenu menu = new();

    public ItemRow()
    {
        InitializeComponent();
        var reveal = new MenuItem { Header = Explorer.ShowLabel };
        reveal.Click += (_, _) => Reveal();
        menu.Items.Add(reveal);
        // 끝난 항목만 두 번 눌러 파일을 보여 줍니다.
        DoubleTapped += (_, e) =>
        {
            if (item?.State != ItemState.Done) return;
            e.Handled = true;
            Reveal();
        };
        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            Subscribe();
            Update();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            attached = false;
            Unsubscribe();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        var next = DataContext as DownloadItem;
        if (next == item) return;
        Unsubscribe();
        item = next;
        if (next == null) return;
        Subscribe();
        Update();
    }

    private void Subscribe()
    {
        if (item == null || !attached) return;
        item.PropertyChanged -= OnItemChanged;
        item.PropertyChanged += OnItemChanged;
    }

    private void Unsubscribe()
    {
        if (item != null) item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        if (item is not { } i) return;
        var state = i.State;
        IndexText.Text = $"{i.Entry.Index}.";
        TitleText.Text = i.Entry.Title;
        ToolTip.SetTip(TitleText, i.Entry.Title);
        PercentText.Text = $"{(int)(i.Progress * 100)}%";
        PercentText.IsVisible = state.IsRunning();
        ErrorText.Text = i.Error ?? "";
        ErrorText.IsVisible = state == ItemState.Failed;
        var (icon, color, label) = state switch
        {
            ItemState.Queued => ("IconCircle", "tertiary", "Queued"),
            ItemState.Downloading => ("IconDownload", "accent", "Downloading"),
            ItemState.Processing => ("IconSliders", "", "Processing"),
            ItemState.Done => ("IconCheckCircle", "success", "Done"),
            ItemState.Skipped => ("IconMinusCircle", "", "Already exists"),
            ItemState.Failed => ("IconAlert", "warning", "Failed"),
            _ => ("IconXCircle", "", "Cancelled"),
        };
        StateIcon.Data = this.FindResource(icon) as Geometry;
        StateIcon.Classes.Set("tertiary", color == "tertiary");
        StateIcon.Classes.Set("success", color == "success");
        StateIcon.Classes.Set("warning", color == "warning");
        if (color == "accent") StateIcon.Stroke = this.FindResource("YpdAccent") as IBrush;
        else StateIcon.ClearValue(Icon.StrokeProperty);
        ToolTip.SetTip(StateIcon, L.T(label));
        AutomationProperties.SetName(this, $"{i.Entry.Index}. {i.Entry.Title}, {L.T(label)}");
        ContextMenu = state == ItemState.Done ? menu : null;
    }

    private void Reveal()
    {
        if (item != null && this.FindAncestorOfType<JobRow>()?.DataContext is DownloadJob job) Ui.ShowInExplorer(job, item);
    }
}
