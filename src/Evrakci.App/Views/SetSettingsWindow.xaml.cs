using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Evrakci.Core.Sets;
using Evrakci.Core.Text;
using Evrakci.Core.Values;
using Wpf.Ui.Controls;

namespace Evrakci.App.Views;

public partial class SetSettingsWindow : FluentWindow
{
    public sealed class CurrencyRow
    {
        public string Code { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Main { get; set; } = "";
        public string Sub { get; set; } = "";
    }

    public sealed class DocumentRow
    {
        public required DocumentEntry Entry { get; init; }
        public string Template => Entry.Template;
        public string? Output { get; set; }
    }

    public sealed class FieldRow
    {
        public required string Name { get; init; }
        public string? Label { get; set; }
        public string? Group { get; set; }
        public string? Default { get; set; }
        public string? Hint { get; set; }
        public required List<string> Choices { get; init; }
        public string TypeChoice { get; set; } = "";
        public bool TypeEditable { get; init; }
        public string? TypeNote { get; init; }
    }

    readonly TemplateSet _set;
    readonly SetDefinition _def;
    readonly List<CurrencyRow> _currencies;
    readonly List<DocumentRow> _documents;
    readonly List<FieldRow> _fields;
    SetDefinition? _result;

    SetSettingsWindow(TemplateSet set)
    {
        _set = set;
        // Work on a copy so "Vazgeç" leaves the open set untouched.
        _def = JsonSerializer.Deserialize<SetDefinition>(JsonSerializer.SerializeToUtf8Bytes(set.Definition, SetDefinition.JsonOptions),
            SetDefinition.JsonOptions)!;
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        SetName.Text = _def.Name ?? set.Name;
        SetDescription.Text = _def.Description ?? "";
        OpenDelim.Text = _def.Delimiters.Open;
        CloseDelim.Text = _def.Delimiters.Close;
        OutputFolder.Text = _def.OutputFolder ?? "";
        DateFormat.Text = _def.Formats.Date;
        MoneyFormat.Text = _def.Formats.Money;
        PhoneFormat.Text = _def.Formats.Phone;

        _currencies = _def.Currencies.Select(kv => new CurrencyRow { Code = kv.Key, Symbol = kv.Value.Symbol, Main = kv.Value.Main, Sub = kv.Value.Sub }).ToList();
        CurrencyGrid.ItemsSource = _currencies;

        _documents = set.Documents.Select(d => new DocumentRow { Entry = d.Entry, Output = d.Entry.Output }).ToList();
        DocumentGrid.ItemsSource = _documents;

        _fields = set.Fields.Select(f =>
        {
            var inferred = f.IsComputed ? FieldType.Para : FieldTypes.Infer(f.Key, f.UsesWords);
            var auto = $"Otomatik ({FieldTypes.DisplayName(inferred)})";
            var choices = new List<string> { auto };
            choices.AddRange(FieldTypes.All.Select(FieldTypes.DisplayName));
            var locked = f.TypeSource == TypeSource.Template;
            return new FieldRow
            {
                Name = f.Name,
                Label = f.Definition?.Label,
                Group = f.Definition?.Group,
                Default = f.Definition?.Default,
                Hint = f.Definition?.Hint,
                Choices = locked ? [FieldTypes.DisplayName(f.Type)] : choices,
                TypeChoice = locked ? FieldTypes.DisplayName(f.Type)
                    : FieldTypes.Parse(f.Definition?.Type) is { } t ? FieldTypes.DisplayName(t) : auto,
                TypeEditable = !locked && !f.IsComputed,
                TypeNote = locked ? "Tip şablonda yazılı." : f.IsComputed ? "Hesaplanan alanlar Para tipindedir." : null,
            };
        }).ToList();
        FieldGrid.ItemsSource = _fields;
        UpdateExamples();
    }

    /// <summary>Returns the edited definition, or null when cancelled.</summary>
    public static SetDefinition? Edit(TemplateSet set)
    {
        var w = new SetSettingsWindow(set);
        return w.ShowDialog() == true ? w._result : null;
    }

    void OnExampleChanged(object sender, RoutedEventArgs e) => UpdateExamples();

    void UpdateExamples()
    {
        if (DateExample is null || MoneyExample is null || PhoneExample is null || DelimExample is null) return;
        var sample = new SetDefinition();
        sample.Formats.Date = DateFormat.Text;
        sample.Formats.Money = MoneyFormat.Text;
        DateExample.Text = "Örnek: " + ValueCodec.FormatDate(new DateOnly(2026, 10, 15), DateFormat.Text);
        MoneyExample.Text = "Örnek: " + ValueCodec.FormatMoney(44000.25m, "TRY", sample);
        PhoneExample.Text = (PhoneFormat.Text ?? "").Count(c => c == '#') == 10
            ? "Örnek: " + ValueCodec.FormatPhone("05321234567", PhoneFormat.Text)
            : "Biçimde tam 10 adet # olmalı.";
        DelimExample.Text = $"Şablonda yazımı: {OpenDelim.Text}TARAF1 ADI{CloseDelim.Text}";
    }

    string? Validate()
    {
        if (string.IsNullOrWhiteSpace(OpenDelim.Text) || string.IsNullOrWhiteSpace(CloseDelim.Text))
            return "Ayraçlar boş olamaz.";
        if (OpenDelim.Text.Trim().Length < 2 || CloseDelim.Text.Trim().Length < 2)
            return "Ayraçlar en az iki karakter olmalı; tek karakter normal metinle karışır.";
        if ((PhoneFormat.Text ?? "").Count(c => c == '#') != 10) return "Telefon biçiminde tam 10 adet # olmalı.";
        if (!(MoneyFormat.Text ?? "").Contains("{tutar}")) return "Para biçiminde {tutar} bulunmalı.";
        try { _ = new DateOnly(2026, 1, 1).ToString(DateFormat.Text, Tr.Culture); }
        catch (FormatException) { return "Tarih biçimi geçersiz."; }
        return null;
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        FieldGrid.CommitEdit(DataGridEditingUnit.Row, true);
        DocumentGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CurrencyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (Validate() is { } problem)
        {
            Problem.Text = problem;
            return;
        }

        _def.Name = Blank(SetName.Text);
        _def.Description = Blank(SetDescription.Text);
        _def.Delimiters.Open = OpenDelim.Text.Trim();
        _def.Delimiters.Close = CloseDelim.Text.Trim();
        _def.OutputFolder = Blank(OutputFolder.Text);
        _def.Formats.Date = DateFormat.Text.Trim();
        _def.Formats.Money = MoneyFormat.Text;
        _def.Formats.Phone = PhoneFormat.Text;
        foreach (var c in _currencies)
            _def.Currencies[c.Code] = new CurrencyNames { Symbol = c.Symbol.Trim(), Main = c.Main.Trim(), Sub = c.Sub.Trim() };

        // An explicit documents list keeps the chosen order and output names.
        _def.Documents = _documents.Select(d => new DocumentEntry { Template = d.Template, Output = Blank(d.Output), Extra = d.Entry.Extra }).ToList();

        foreach (var row in _fields)
        {
            var key = Tr.Key(row.Name);
            var existing = _def.Fields.FirstOrDefault(kv => Tr.Key(kv.Key) == key);
            var def = existing.Value;
            var type = row.TypeEditable && !row.TypeChoice.StartsWith("Otomatik") ? row.TypeChoice : null;
            bool anything = Blank(row.Label) is not null || Blank(row.Group) is not null || Blank(row.Default) is not null ||
                            Blank(row.Hint) is not null || type is not null;
            if (def is null)
            {
                if (!anything) continue;
                def = new FieldDefinition();
                _def.Fields[row.Name] = def;
            }
            def.Label = Blank(row.Label);
            def.Group = Blank(row.Group);
            def.Default = Blank(row.Default);
            def.Hint = Blank(row.Hint);
            if (row.TypeEditable) def.Type = type;
        }

        _result = _def;
        DialogResult = true;
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
