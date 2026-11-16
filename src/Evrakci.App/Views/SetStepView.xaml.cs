using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Evrakci.App.Views;

public partial class SetStepView : UserControl
{
    public SetStepView() => InitializeComponent();

    /// <summary>Opens a button's menu on a normal click (the "⋯" and "Belge ekle" buttons).</summary>
    void OnMore(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        if (menu.DataContext is null || button.DataContext != menu.DataContext) menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }
}
