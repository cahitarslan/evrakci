namespace Evrakci.Core.Values;

/// <summary>A parsed, validated value in canonical form.</summary>
public sealed record FieldValue
{
    public required FieldType Type { get; init; }
    /// <summary>Metin text, TCKN digits, compact IBAN or 11 phone digits.</summary>
    public string? Text { get; init; }
    /// <summary>Para / Sayı amount.</summary>
    public decimal? Number { get; init; }
    public DateOnly? Date { get; init; }
    /// <summary>ISO code for Para (TRY, USD, EUR).</summary>
    public string? Currency { get; init; }
}

public readonly record struct ParseResult(FieldValue? Value, string? Error)
{
    public static ParseResult Ok(FieldValue v) => new(v, null);
    public static ParseResult Fail(string error) => new(null, error);
    public bool IsOk => Value is not null;
}
