using System.Text.RegularExpressions;
using System.Windows;
using Evrakci.Core.Sets;
using Evrakci.Core.Text;
using Evrakci.Core.Values;
using Wpf.Ui.Controls;

namespace Evrakci.App.Views;

public partial class MakeVariableWindow : FluentWindow
{
    public sealed record Result(string Name, FieldDefinition? Settings, EditScope Scope);

    readonly TemplateSet _set;
    Result? _result;

    MakeVariableWindow(TemplateSet set, string text, int inDocument, int inSet)
    {
        _set = set;
        InitializeComponent();
        Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        Selected.Text = text;

        foreach (var f in set.Fields.Where(f => !f.IsComputed)) NameBox.Items.Add(f.Name);
        foreach (var t in FieldTypes.All) TypeBox.Items.Add(FieldTypes.DisplayName(t));
        foreach (var g in set.Fields.Select(f => f.Group).Distinct()) GroupBox.Items.Add(g);

        var guessType = GuessType(text);
        var name = text.Length <= 40 && !text.Contains('\n') ? Tr.Upper(Tr.CollapseSpaces(text)) : "";
        NameBox.Text = guessType == FieldType.Metin ? name : "";
        TypeBox.SelectedItem = FieldTypes.DisplayName(guessType);
        GroupBox.Text = set.Fields.Select(f => f.Group).FirstOrDefault() ?? "Diğer alanlar";

        ScopeDoc.Content = $"Bu belgedeki aynı metinlerin tümünde ({inDocument} yer)";
        ScopeSet.Content = $"Setteki bütün belgelerde ({inSet} yer)";
        ScopeDoc.IsEnabled = inDocument > 1;
        ScopeSet.IsEnabled = inSet > inDocument;
        (inSet > 1 && inSet > inDocument ? ScopeSet : inDocument > 1 ? ScopeDoc : ScopeHere).IsChecked = true;

        NameBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) => UpdateExisting()));
        NameBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateExisting);
        Loaded += (_, _) => NameBox.Focus();
        UpdateExisting();
    }

    public static Result? Ask(TemplateSet set, string text, int inDocument, int inSet)
    {
        var w = new MakeVariableWindow(set, text, inDocument, inSet);
        return w.ShowDialog() == true ? w._result : null;
    }

    /// <summary>A sensible type for the selected sample text.</summary>
    static FieldType GuessType(string text)
    {
        var t = text.Trim();
        if (Regex.IsMatch(t, @"^\d{1,2}[./]\d{1,2}[./]\d{4}$")) return FieldType.Tarih;
        if (Regex.IsMatch(t, @"^\d{11}$")) return FieldType.Tckn;
        if (Regex.IsMatch(t.Replace(" ", ""), @"^TR\d{24}$", RegexOptions.IgnoreCase)) return FieldType.Iban;
        if (Regex.IsMatch(t, @"^[\d.]+(,\d{1,2})?\s*(\.-)?\s*(TL|TRY|USD|EUR|₺)$", RegexOptions.IgnoreCase)) return FieldType.Para;
        if (Regex.IsMatch(t, @"^(\+90|0)?\s*\(?5\d{2}\)?[\s-]*\d{3}[\s-]*\d{2}[\s-]*\d{2}$")) return FieldType.Telefon;
        return FieldType.Metin;
    }

    FieldInfo? Existing => _set.Field(NameBox.Text ?? "");

    void UpdateExisting()
    {
        var f = Existing;
        bool exists = f is not null;
        LabelBox.IsEnabled = TypeBox.IsEnabled = GroupBox.IsEnabled = !exists;
        ExistingNote.Text = exists
            ? $"Var olan değişken: “{f!.Label}” ({FieldTypes.DisplayName(f.Type)}). Seçilen metin bu değişkenle değiştirilir."
            : "Yeni değişken; formda ayrı bir alan olarak sorulur.";
        if (exists)
        {
            LabelBox.Text = f!.Label;
            TypeBox.SelectedItem = FieldTypes.DisplayName(f.Type);
            GroupBox.Text = f.Group;
        }
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        var name = Tr.CollapseSpaces(NameBox.Text ?? "");
        if (TemplateEditing.CheckName(_set, name) is { } err)
        {
            Error.Text = err;
            Error.Visibility = Visibility.Visible;
            return;
        }
        var existing = Existing;
        if (existing is not null) name = existing.Name;
        var settings = existing is not null ? null : new FieldDefinition
        {
            Type = TypeBox.SelectedItem as string,
            Label = string.IsNullOrWhiteSpace(LabelBox.Text) ? null : LabelBox.Text.Trim(),
            Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? null : GroupBox.Text.Trim(),
        };
        var scope = ScopeSet.IsChecked == true ? EditScope.Set : ScopeDoc.IsChecked == true ? EditScope.Document : EditScope.Here;
        _result = new Result(name, settings, scope);
        DialogResult = true;
    }
}
