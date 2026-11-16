using System.Collections.ObjectModel;
using Evrakci.Core.Cases;
using Evrakci.Core.Sets;
using Evrakci.Core.Values;

namespace Evrakci.App.ViewModels;

public sealed class FieldGroupViewModel(string name)
{
    public string Name { get; } = name;
    public ObservableCollection<FieldViewModel> Fields { get; } = new();
}

public sealed class FieldViewModel : ObservableObject
{
    readonly Action<FieldViewModel> _changed;
    readonly Action<FieldViewModel>? _rememberChanged;
    string _raw = "";
    bool _blank;
    bool _remember;
    string _currency;
    bool _touched;
    bool _showAllErrors;
    string? _error;
    string? _output;
    string? _note;

    readonly Func<string, string> _labelOf;

    public FieldViewModel(FieldInfo field, FieldInput input, Action<FieldViewModel> changed, Func<string, string> labelOf,
        bool remember = false, Action<FieldViewModel>? rememberChanged = null)
    {
        Field = field;
        _changed = changed;
        _labelOf = labelOf;
        _remember = remember;
        _rememberChanged = rememberChanged;
        _raw = input.Raw;
        _blank = input.Blank;
        _currency = input.Currency ?? field.Definition?.Currency ?? ValueCodec.DefaultCurrency;
    }

    public FieldInfo Field { get; }
    public string Key => Field.Key;
    public string Label => Field.Label;
    public string Name => Field.Name;
    public string? Hint => Field.Definition?.Hint;
    public FieldType Type => Field.Type;
    public string TypeName => FieldTypes.DisplayName(Field.Type);
    public bool IsComputed => Field.IsComputed;
    public bool IsMoney => Type == FieldType.Para && !IsComputed;
    public bool IsDate => Type == FieldType.Tarih;
    public bool IsPlainInput => !IsComputed && !IsMoney && !IsDate;
    public bool CanBeBlank => !IsComputed;
    public static IReadOnlyList<string> Currencies { get; } = ["TRY", "USD", "EUR"];

    public string Placeholder => Type switch
    {
        FieldType.Para => "ör. 44.000,25",
        FieldType.Tarih => "gg.aa.yyyy",
        FieldType.Tckn => "11 haneli T.C. kimlik no",
        FieldType.Iban => "TR00 0000 0000 0000 0000 0000 00",
        FieldType.Telefon => "ör. 0532 123 45 67",
        FieldType.Sayi => "ör. 12 ya da 12,5",
        _ => Hint ?? "",
    };

    public string Raw
    {
        get => _raw;
        set
        {
            if (Set(ref _raw, value ?? ""))
            {
                Touched = true;
                _changed(this);
            }
        }
    }

    public bool IsBlank
    {
        get => _blank;
        set
        {
            if (Set(ref _blank, value))
            {
                Touched = true;
                Raise(nameof(IsEditable));
                _changed(this);
            }
        }
    }

    public string Currency
    {
        get => _currency;
        set
        {
            if (Set(ref _currency, value)) _changed(this);
        }
    }

    public bool IsEditable => !_blank && !IsComputed;

    /// <summary>"Hatırla": the value is kept and pre-filled in every new case of every set.</summary>
    public bool Remember
    {
        get => _remember;
        set
        {
            if (Set(ref _remember, value)) _rememberChanged?.Invoke(this);
        }
    }

    public bool CanRemember => !IsComputed;

    public bool Touched
    {
        get => _touched;
        private set { if (Set(ref _touched, value)) Raise(nameof(VisibleError)); }
    }

    public bool ShowAllErrors
    {
        get => _showAllErrors;
        set { if (Set(ref _showAllErrors, value)) Raise(nameof(VisibleError)); }
    }

    public string? Error
    {
        get => _error;
        private set { if (Set(ref _error, value)) Raise(nameof(VisibleError)); }
    }

    /// <summary>Errors appear once the user has touched the field or tried to continue.</summary>
    public string? VisibleError => (_touched || _showAllErrors || IsComputed) ? _error : null;

    /// <summary>How the value will be written into the documents.</summary>
    public string? Output
    {
        get => _output;
        private set => Set(ref _output, value);
    }

    public string? Note
    {
        get => _note;
        private set => Set(ref _note, value);
    }

    public FieldInput ToInput() => new() { Raw = _raw, Blank = _blank, Currency = IsMoney ? _currency : null };

    public void Load(FieldInput input)
    {
        _raw = input.Raw;
        _blank = input.Blank;
        if (input.Currency is not null) _currency = input.Currency;
        _touched = false;
        Raise(nameof(Raw));
        Raise(nameof(IsBlank));
        Raise(nameof(Currency));
        Raise(nameof(IsEditable));
        Raise(nameof(VisibleError));
    }

    public void Apply(ResolvedField? r)
    {
        Error = r?.Error;
        if (r is null) { Output = null; Note = null; return; }
        if (r.Blank)
        {
            Output = Field.Definition?.RemoveParagraphIfBlank == true ? "Boş — bu satır belgelerden çıkarılacak" : "Boş bırakılacak";
            Note = null;
            return;
        }
        Output = r.Display is null ? null
            : r.Words is not null ? $"{r.Display}  ·  {r.Words}"
            : r.Display != _raw.Trim() || IsComputed ? r.Display : null;
        Note = r.FromOtherField ? $"Boş bırakıldığı için “{OtherFieldLabel()}” alanından alındı." : null;
        if (Note is null && string.IsNullOrWhiteSpace(_raw) && !string.IsNullOrWhiteSpace(Field.Definition?.DefaultFrom) && r.Error is not null)
            Note = $"Boş bırakılırsa “{OtherFieldLabel()}” ile aynı olur.";
    }

    string OtherFieldLabel() => _labelOf(Field.Definition?.DefaultFrom ?? "");
}
