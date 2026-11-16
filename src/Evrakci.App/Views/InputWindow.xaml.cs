using System.Windows;
using Wpf.Ui.Controls;

namespace Evrakci.App.Views;

public enum EditScope { Here, Document, Set }

/// <summary>Asks for one line of text, optionally with a choice list and a "where to apply" choice.</summary>
public partial class InputWindow : FluentWindow
{
    public sealed record Result(string Text, EditScope Scope, string? Choice);

    readonly Func<string, string?>? _validate;
    Result? _result;

    InputWindow(string title, string heading, string? message, string label, string initial, string ok,
        Func<string, string?>? validate)
    {
        _validate = validate;
        InitializeComponent();
        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        Title = title;
        Bar.Title = title;
        Heading.Text = heading;
        Message.Text = message ?? "";
        Message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        InputLabel.Text = label;
        Input.Text = initial;
        OkButton.Content = ok;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public static string? Ask(string title, string heading, string label, string initial = "", string? message = null,
        string ok = "Tamam", Func<string, string?>? validate = null)
    {
        var w = new InputWindow(title, heading, message, label, initial, ok, validate);
        return w.ShowDialog() == true ? w._result!.Text : null;
    }

    /// <summary>Text plus a choice from a list (e.g. a set name and its category).</summary>
    public static (string Text, string Choice)? AskWithChoice(string title, string heading, string label, string initial,
        IEnumerable<string> choices, string selected, string? message = null, string ok = "Tamam", Func<string, string?>? validate = null)
    {
        var w = new InputWindow(title, heading, message, label, initial, ok, validate);
        w.Choice.Visibility = Visibility.Visible;
        w.Choice.IsEditable = true;
        foreach (var c in choices) w.Choice.Items.Add(c);
        w.Choice.Text = selected;
        return w.ShowDialog() == true ? (w._result!.Text, w._result.Choice ?? selected) : null;
    }

    /// <summary>Text plus where to apply it; counts of matching places are shown next to the options.</summary>
    public static Result? AskWithScope(string title, string heading, string label, string initial, int inDocument, int inSet,
        string? message = null, string ok = "Uygula", Func<string, string?>? validate = null)
    {
        var w = new InputWindow(title, heading, message, label, initial, ok, validate);
        w.ScopePanel.Visibility = Visibility.Visible;
        w.ScopeDoc.Content = $"Bu belgedeki aynı metinlerin tümünde ({inDocument} yer)";
        w.ScopeSet.Content = $"Setteki bütün belgelerde ({inSet} yer)";
        w.ScopeDoc.IsEnabled = inDocument > 1;
        w.ScopeSet.IsEnabled = inSet > inDocument;
        return w.ShowDialog() == true ? w._result : null;
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        var text = Input.Text ?? "";
        if (_validate?.Invoke(text) is { } error)
        {
            Error.Text = error;
            Error.Visibility = Visibility.Visible;
            return;
        }
        var scope = ScopeSet.IsChecked == true ? EditScope.Set : ScopeDoc.IsChecked == true ? EditScope.Document : EditScope.Here;
        _result = new Result(text, scope, Choice.Visibility == Visibility.Visible ? Choice.Text : null);
        DialogResult = true;
    }
}
