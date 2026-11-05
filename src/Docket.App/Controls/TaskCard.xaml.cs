using Docket.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Docket.App.Controls;

/// <summary>Whatever shows task rows (a list, a board, a table) and handles their buttons.</summary>
public interface ITaskHost
{
    Task ToggleDoneAsync(TaskItem item, bool done);
    Task ToggleImportantAsync(TaskItem item);
    void ToggleExpanded(TaskItem item);
}

/// <summary>One task row, To Do style: chevron for subtasks, check, title, meta line with icons, assignees, star.</summary>
public sealed partial class TaskCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(TaskItem), typeof(TaskCard), new PropertyMetadata(null));

    public TaskItem? Item { get => (TaskItem?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }

    public TaskCard() => InitializeComponent();

    private ITaskHost? Host
    {
        get
        {
            DependencyObject? d = this;
            while (d is not null)
            {
                if (d is ITaskHost h) return h;
                d = VisualTreeHelper.GetParent(d);
            }
            return null;
        }
    }

    private async void Done_Click(object sender, RoutedEventArgs e)
    {
        if (Item is { } t && Host is { } h) await h.ToggleDoneAsync(t, DoneBox.IsChecked == true);
    }

    private async void Star_Click(object sender, RoutedEventArgs e)
    {
        if (Item is { } t && Host is { } h) await h.ToggleImportantAsync(t);
    }

    private void Chevron_Click(object sender, RoutedEventArgs e)
    {
        if (Item is { } t && Host is { } h) h.ToggleExpanded(t);
    }
}
