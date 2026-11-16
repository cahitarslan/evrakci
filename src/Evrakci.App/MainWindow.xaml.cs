using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Evrakci.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Evrakci.App;

public partial class MainWindow : FluentWindow
{
    readonly MainViewModel _vm = new();
    bool _closingConfirmed;

    public MainWindow()
    {
        DataContext = _vm;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Services.ThemeService.Apply(_vm.Theme, this);
            await _vm.StartBackgroundWorkAsync();
        };
        // A template saved in Word while the app was in the background shows up in the form at once.
        Activated += async (_, _) => await _vm.CheckSetChangedAsync();

        InputBindings.Add(new KeyBinding(_vm.NewFileCommand, Key.N, ModifierKeys.Control));
    }

    public async void OpenFromCommandLine(string path) => await _vm.OpenFromCommandLineAsync(path);

    /// <summary>Opens a header button's menu on a normal click.</summary>
    void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { ContextMenu: { } menu } button) return;
        menu.DataContext = _vm;
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closingConfirmed)
        {
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        if (await _vm.CanCloseAsync())
        {
            _closingConfirmed = true;
            await Dispatcher.InvokeAsync(Close);
        }
    }
}
