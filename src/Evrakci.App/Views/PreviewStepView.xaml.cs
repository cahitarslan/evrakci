using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Evrakci.App.ViewModels;

namespace Evrakci.App.Views;

public partial class PreviewStepView : UserControl
{
    MainViewModel? _vm;

    public PreviewStepView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        Render();
    }

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPreview)) Render();
    }

    void Render()
    {
        if (_vm?.CurrentPreview is not { } doc)
        {
            Viewer.Document = null;
            return;
        }
        var offset = (Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer)?.VerticalOffset;
        var renderer = new PreviewRenderer(this, (segment, value) => _vm.EditSuffix(segment, value));
        Viewer.Document = renderer.Render(doc);
        // Keep the reading position after a suffix edit.
        if (offset is > 0)
            Dispatcher.BeginInvoke(() =>
                (Viewer.Template?.FindName("PART_ContentHost", Viewer) as ScrollViewer)?.ScrollToVerticalOffset(offset.Value),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
