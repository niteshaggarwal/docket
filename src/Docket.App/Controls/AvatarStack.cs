using Docket.App.Services;
using Docket.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Docket.App.Controls;

/// <summary>Small overlapping initials avatars for a task's assignees.</summary>
public sealed partial class AvatarStack : ContentControl
{
    public static readonly DependencyProperty UsersProperty = DependencyProperty.Register(
        nameof(Users), typeof(IReadOnlyList<VUser>), typeof(AvatarStack), new PropertyMetadata(null, (d, _) => ((AvatarStack)d).Build()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(AvatarStack), new PropertyMetadata(20.0, (d, _) => ((AvatarStack)d).Build()));

    public IReadOnlyList<VUser>? Users { get => (IReadOnlyList<VUser>?)GetValue(UsersProperty); set => SetValue(UsersProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public AvatarStack()
    {
        IsTabStop = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    private void Build()
    {
        var users = Users ?? [];
        Content = users.Count == 0 ? null : Ui.AvatarStack(users, Size);
        Visibility = users.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
