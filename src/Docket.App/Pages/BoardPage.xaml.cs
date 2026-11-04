using System.Collections.ObjectModel;
using System.ComponentModel;
using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Docket.App.Pages;

/// <summary>One Kanban column and its cards.</summary>
public sealed class BoardColumn : INotifyPropertyChanged
{
    public BoardColumn(VBucket bucket, IEnumerable<TaskItem> tasks)
    {
        Bucket = bucket;
        Tasks = new ObservableCollection<TaskItem>(tasks);
        Tasks.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CountText)));
    }

    public VBucket Bucket { get; }
    public string Title => Bucket.Title;
    public ObservableCollection<TaskItem> Tasks { get; }
    public string CountText => Bucket.Limit > 0 ? $"{Tasks.Count}/{Bucket.Limit}" : Tasks.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class BoardPage : Page, IRefreshable
{
    private VProject? _project;
    private VView? _view;
    private readonly ObservableCollection<BoardColumn> _columns = [];
    private (TaskItem Item, BoardColumn From)? _dragging;

    public BoardPage()
    {
        InitializeComponent();
        Columns.ItemsSource = _columns;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _project = e.Parameter as VProject;
        Heading.Text = _project?.Title ?? "";
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (App.Client is null || _project is null) return;
        // Re-read the project so a newly added Kanban view is picked up.
        _project = App.Window?.Projects.FirstOrDefault(p => p.Id == _project.Id) ?? _project;
        _view = _project.KanbanView;
        NoBoardText.Visibility = _view is null ? Visibility.Visible : Visibility.Collapsed;
        if (_view is null) { _columns.Clear(); return; }

        LoadingRing.IsActive = true;
        try
        {
            var buckets = await App.Client.GetBoardAsync(_project.Id, _view.Id);
            _columns.Clear();
            foreach (var b in buckets.OrderBy(b => b.Position))
                _columns.Add(new BoardColumn(b, (b.Tasks ?? []).OrderBy(t => t.Position).Select(t => new TaskItem(t))));
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
        {
            App.Window?.ShowError("Couldn't load the board", ex.Message);
        }
        finally { LoadingRing.IsActive = false; }
    }

    private void Column_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (sender is ListView { Tag: BoardColumn from } && e.Items.FirstOrDefault() is TaskItem item)
        {
            _dragging = (item, from);
            e.Data.SetText(item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void Column_DragOver(object sender, DragEventArgs e)
    {
        if (_dragging is not null) e.AcceptedOperation = DataPackageOperation.Move;
    }

    private async void Column_Drop(object sender, DragEventArgs e)
    {
        if (_dragging is not { } drag || sender is not ListView { Tag: BoardColumn to } || App.Client is null || _project is null || _view is null) return;
        _dragging = null;
        if (ReferenceEquals(drag.From, to)) return;

        drag.From.Tasks.Remove(drag.Item);
        to.Tasks.Add(drag.Item);
        try
        {
            await App.Client.MoveToBucketAsync(_project.Id, _view.Id, to.Bucket.Id, drag.Item.Id);
            App.Window?.SetStatus($"Moved to {to.Title}");
            // Moving into or out of the Done column changes the done flag on the server.
            if (to.Bucket.Id == _view.DoneBucketId || drag.From.Bucket.Id == _view.DoneBucketId) await RefreshAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
        {
            to.Tasks.Remove(drag.Item);
            drag.From.Tasks.Add(drag.Item);
            App.Window?.ShowError("Couldn't move the card", ex.Message);
        }
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: TaskItem item } box || App.Client is null) return;
        try
        {
            await App.Client.SetDoneAsync(item.Id, box.IsChecked == true);
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or VikunjaException or TaskCanceledException)
        {
            box.IsChecked = !box.IsChecked;
            App.Window?.ShowError("Couldn't update the task", ex.Message);
        }
    }
}
