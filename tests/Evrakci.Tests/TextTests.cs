using Evrakci.Core.Templates;
using Evrakci.Core.Text;
using Evrakci.Core.Values;

namespace Evrakci.Tests;

public class NumberToWordsTests
{
    [Theory]
    [InlineData("44000.25", "Kırk Dört Bin Türk Lirası Yirmi Beş Kuruş")]
    [InlineData("64000", "Altmış Dört Bin Türk Lirası")]
    [InlineData("315523.56", "Üç Yüz On Beş Bin Beş Yüz Yirmi Üç Türk Lirası Elli Altı Kuruş")]
    [InlineData("61866.66", "Altmış Bir Bin Sekiz Yüz Altmış Altı Türk Lirası Altmış Altı Kuruş")]
    [InlineData("1000", "Bin Türk Lirası")]
    [InlineData("1100", "Bin Yüz Türk Lirası")]
    [InlineData("101", "Yüz Bir Türk Lirası")]
    [InlineData("1000000", "Bir Milyon Türk Lirası")]
    [InlineData("2001000", "İki Milyon Bin Türk Lirası")]
    [InlineData("0", "Sıfır Türk Lirası")]
    [InlineData("0.5", "Elli Kuruş")]
    [InlineData("0.05", "Beş Kuruş")]
    public void Money(string amount, string expected) =>
        Assert.Equal(expected, NumberToWords.Money(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), "Türk Lirası", "Kuruş"));

    [Fact]
    public void RejectsThirdDecimal() =>
        Assert.Throws<ArgumentException>(() => NumberToWords.Money(1.005m, "Türk Lirası", "Kuruş"));
}

public class ValueTests
{
    [Theory]
    [InlineData("44.000,25", "44000.25")]
    [InlineData("44000,25", "44000.25")]
    [InlineData("44000.25", "44000.25")]
    [InlineData("44.000", "44000")]
    [InlineData("1.234.567,8", "1234567.8")]
    [InlineData("64.000,00.-TL", "64000")]
    [InlineData("9.000,00 TL", "9000")]
    [InlineData("₺ 12", "12")]
    public void ParsesMoney(string raw, string expected)
    {
        var r = ValueCodec.ParseMoney(raw, "TRY");
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), r.Value!.Number);
    }

    [Theory]
    [InlineData("12,345")]      // no silent rounding
    [InlineData("1.23.4")]
    [InlineData("abc")]
    [InlineData("-5")]
    public void RejectsBadMoney(string raw) => Assert.False(ValueCodec.ParseMoney(raw, "TRY").IsOk);

    [Fact]
    public void FormatsMoneyWithSetPattern()
    {
        var set = new Evrakci.Core.Sets.SetDefinition();
        set.Formats.Money = "{tutar}.-{simge}";
        Assert.Equal("64.000,00.-TL", ValueCodec.FormatMoney(64000m, "TRY", set));
        set.Formats.Money = "{tutar} {simge}";
        Assert.Equal("44.000,25 TL", ValueCodec.FormatMoney(44000.25m, "TRY", set));
        Assert.Equal("1.250,00 EUR", ValueCodec.FormatMoney(1250m, "EUR", set));
    }

    [Theory]
    [InlineData("12345678950", true)]
    [InlineData("23456789138", true)]
    [InlineData("34567891238", true)]
    [InlineData("12345678951", false)]
    [InlineData("02345678950", false)]
    [InlineData("1234567890", false)]
    public void Tckn(string s, bool ok) => Assert.Equal(ok, Validators.IsValidTckn(s));

    [Fact]
    public void Iban()
    {
        var ok = ValueCodec.Parse(FieldType.Iban, "tr84 0001 0000 0001 2345 6789 01", null);
        Assert.True(ok.IsOk, ok.Error);
        Assert.Equal("TR84 0001 0000 0001 2345 6789 01", ValueCodec.Format(ok.Value!, new()));
        Assert.False(ValueCodec.Parse(FieldType.Iban, "TR84 0001 0000 0001 2345 6789 02", null).IsOk);
        Assert.False(ValueCodec.Parse(FieldType.Iban, "DE89 3704 0044 0532 0130 00", null).IsOk);
    }

    [Theory]
    [InlineData("+90 532 123 45 67", "05321234567")]
    [InlineData("0 (555) 123 45 67", "05551234567")]
    [InlineData("5551234567", "05551234567")]
    [InlineData("00905551234567", "05551234567")]
    [InlineData("0312 444 44 44", "03124444444")]
    public void Phone(string raw, string digits) => Assert.Equal(digits, Validators.NormalizeTrPhone(raw));

    [Theory]
    [InlineData("123")]
    [InlineData("06551234567")]
    [InlineData("055512345671")]
    public void BadPhone(string raw) => Assert.Null(Validators.NormalizeTrPhone(raw));

    [Fact]
    public void PhoneFormat()
    {
        Assert.Equal("0 (555) 123 45 67", ValueCodec.FormatPhone("05551234567", "0 (###) ### ## ##"));
        Assert.Equal("0555 123 45 67", ValueCodec.FormatPhone("05551234567", "0### ### ## ##"));
    }

    [Theory]
    [InlineData("1.10.2026")]
    [InlineData("01.10.2026")]
    [InlineData("01/10/2026")]
    [InlineData("01102026")]
    [InlineData("2026-10-01")]
    [InlineData("1 Ekim 2026")]
    public void Dates(string raw)
    {
        var r = ValueCodec.Parse(FieldType.Tarih, raw, null);
        Assert.True(r.IsOk, r.Error);
        Assert.Equal(new DateOnly(2026, 10, 1), r.Value!.Date);
    }

    [Fact]
    public void DateFormatUsesTurkishMonths() =>
        Assert.Equal("1 Ekim 2026", ValueCodec.FormatDate(new DateOnly(2026, 10, 1), "d MMMM yyyy"));

    [Fact]
    public void TitleAndUpper()
    {
        Assert.Equal("İSTANBUL", Tr.Upper("istanbul"));
        Assert.Equal("Küçükçekmece", Tr.Title("KÜÇÜKÇEKMECE"));
        Assert.Equal("Iğdır", Tr.Title("IĞDIR"));
    }

    [Theory]
    [InlineData("TARAF1 ADI", "Ürün Adedi", FieldType.Metin)]
    [InlineData("TARAF1 TCKN", "", FieldType.Tckn)]
    [InlineData("BAŞVURU TARİHİ", "", FieldType.Tarih)]
    [InlineData("TARAF1 TELEFON", "", FieldType.Telefon)]
    [InlineData("KİRA BEDELİ", "", FieldType.Para)]
    [InlineData("ÜCRET ÖDEME TARİHİ", "", FieldType.Tarih)]
    public void InfersTypes(string key, string _, FieldType expected) => Assert.Equal(expected, FieldTypes.Infer(key, false));
}

public class NameTests
{
    [Fact]
    public void KeysIgnoreCaseAndSpacesTurkishStyle()
    {
        Assert.Equal(Tr.Key("TARAF1 ADI"), Tr.Key("taraf1  adı"));
        Assert.NotEqual(Tr.Key("TARAF1 ADI"), Tr.Key("taraf1 adi"));   // i → İ in Turkish
        Assert.Equal(Tr.Key("İL"), Tr.Key("il"));
    }

    [Theory]
    [InlineData("TARAF1 ADI", "TARAF 1 ADI", true)]
    [InlineData("TARAF1 ADI", "TARAF1 ADİ", true)]
    [InlineData("TAKSİT1 TUTARI", "TAKSİT2 TUTARI", false)]
    [InlineData("TAKSİT TUTARI", "TAKSİT1 TUTARI", false)]
    [InlineData("TARAF1 İL", "TARAF1 İLÇE", false)]
    [InlineData("YETKİLİ TELEFON", "ANLAŞMA YETKİLİSİ TELEFON", false)]
    public void LooksAlike(string a, string b, bool expected) => Assert.Equal(expected, Tr.LooksAlike(Tr.Key(a), Tr.Key(b)));

    [Fact]
    public void ParsesVariableSyntax()
    {
        var a = VariableRef.Parse("Miktar1.Yazıyla");
        Assert.Equal("Miktar1", a.Name);
        Assert.Equal(VariableModifier.Words, a.Modifier);
        var b = VariableRef.Parse(" Doğum : Tarih ");
        Assert.Equal("Doğum", b.Name);
        Assert.Equal(FieldType.Tarih, b.InlineType);
        var c = VariableRef.Parse("Miktar1:Para.YAZIYLA");
        Assert.Equal(FieldType.Para, c.InlineType);
        Assert.Equal(VariableModifier.Words, c.Modifier);
        var d = VariableRef.Parse("T.C. NO");
        Assert.Equal("T.C. NO", d.Name);
        Assert.Equal(VariableModifier.None, d.Modifier);
        Assert.Equal("Bilinmez", VariableRef.Parse("X:Bilinmez").UnknownTypeText);
    }

    [Fact]
    public void MatcherFindsSuffixAndCustomDelimiters()
    {
        var m = new VariableMatcher("{{{", "}}}");
        var found = m.Find("Sayın {{{AD}}}’nın ve {{{SOYAD}}} ile '{{{X}}}' {{{ yarım");
        Assert.Equal(3, found.Count);
        Assert.Equal("nın", found[0].Suffix);
        Assert.Equal('’', found[0].Apostrophe);
        Assert.Null(found[1].Suffix);
        Assert.Null(found[2].Suffix);
        Assert.Equal(1, m.CountUnclosed("Sayın {{{AD}}}’nın ve {{{SOYAD}}} ile '{{{X}}}' {{{ yarım", found));

        var custom = new VariableMatcher("[[", "]]");
        Assert.Equal("AD", Assert.Single(custom.Find("Merhaba [[AD]]'a")).Ref.Name);
    }
}

public class SuffixTests
{
    [Theory]
    [InlineData("CAHİT ARSLAN", "nın", "ın")]
    [InlineData("AYŞE FİLİZ", "nın", "in")]
    [InlineData("Ahmet", "da", "te")]
    [InlineData("KORAY KOVAN", "a", "a")]
    [InlineData("ZÜLFİKİR YALÇINKAYA", "a", "ya")]
    [InlineData("ZÜLFİKİR YALÇINKAYA", "ın", "nın")]
    [InlineData("TAYFUN BUĞDAY", "ın", "ın")]
    [InlineData("379.523,56.-TL", "nin", "nin")]
    [InlineData("44.000,25 TL", "a", "ye")]
    [InlineData("SGK", "a", "ya")]
    [InlineData("A.Ş.", "ın", "nin")]
    [InlineData("02.10.2026", "de", "da")]
    [InlineData("5", "da", "te")]
    [InlineData("10", "a", "a")]
    [InlineData("Ankara", "deki", "daki")]
    [InlineData("İzmir", "daki", "deki")]
    [InlineData("Ali", "la", "yle")]
    [InlineData("Oya", "le", "yla")]
    [InlineData("Ahmet", "yla", "le")]
    [InlineData("Mehmet", "ı", "i")]
    [InlineData("Ayşe", "ı", "yi")]
    [InlineData("Ümit", "nın", "in")]
    [InlineData("Oğuz", "nın", "un")]
    [InlineData("Ömür", "nın", "ün")]
    [InlineData("Bursa", "tan", "dan")]
    [InlineData("Kars", "dan", "tan")]
    [InlineData("Ahmet", "dır", "tir")]
    [InlineData("Ayşe", "nınki", "ninki")]
    [InlineData("AHMET", "NIN", "İN")]
    public void Adapts(string value, string templateSuffix, string expected) =>
        Assert.Equal(expected, TurkishSuffix.Adapt(value, templateSuffix));
}
