using System.Windows;
using Wpf.Ui.Controls;

namespace Evrakci.App.Views;

public partial class SuffixWindow : FluentWindow
{
    public readonly record struct Result(string Suffix, bool Reset);

    readonly char _apostrophe;
    Result? _result;

    SuffixWindow(string value, char apostrophe, string current, string auto)
    {
        _apostrophe = apostrophe;
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        ValueRun.Text = value;
        Input.Text = current;
        AutoNote.Text = auto.Length > 0 ? $"Otomatik seçim: {apostrophe}{auto}" : "";
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
        UpdatePreview();
    }

    public static Result? Ask(string value, char apostrophe, string current, string auto)
    {
        var w = new SuffixWindow(value, apostrophe, current, auto);
        return w.ShowDialog() == true ? w._result : null;
    }

    void OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdatePreview();

    void UpdatePreview()
    {
        var s = Clean(Input.Text);
        PreviewRun.Text = s.Length == 0 ? "" : _apostrophe + s;
    }

    static string Clean(string? s) => (s ?? "").Trim().TrimStart('\'', '’');

    void OnOk(object sender, RoutedEventArgs e)
    {
        _result = new Result(Clean(Input.Text), false);
        DialogResult = true;
    }

    void OnReset(object sender, RoutedEventArgs e)
    {
        _result = new Result("", true);
        DialogResult = true;
    }
}
