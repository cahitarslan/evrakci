using Evrakci.Core.Cases;
using Evrakci.Core.Output;
using Evrakci.Core.Sets;
using Evrakci.Core.Text;
using Evrakci.Core.Values;

namespace Evrakci.Tests;

/// <summary>The ready-made sets in Sablonlar\Hazır must open cleanly and produce complete documents.</summary>
public class BuiltInSetTests
{
    public static TheoryData<string> Sets()
    {
        var data = new TheoryData<string>();
        foreach (var d in Directory.EnumerateDirectories(TestPaths.BuiltInSets)) data.Add(Path.GetFileName(d));
        return data;
    }

    [Fact]
    public void AllKindsArePresent() => Assert.Equal(9, Directory.EnumerateDirectories(TestPaths.BuiltInSets).Count());

    [Theory, MemberData(nameof(Sets))]
    public void OpensWithoutErrorsAndEveryFieldIsConfigured(string folder)
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, folder));
        Assert.DoesNotContain(set.Issues, i => i.Severity != IssueSeverity.Info);
        Assert.False(string.IsNullOrWhiteSpace(set.Definition.Category));
        Assert.Equal(7, set.Documents.Count);
        // Every variable has its type and label in set.json, and every set.json field is used.
        Assert.All(set.Fields, f => Assert.NotNull(f.Definition?.Label));
        Assert.All(set.Fields, f => Assert.Equal(TypeSource.Settings, f.TypeSource));
        Assert.DoesNotContain(set.Issues, i => i.Message.Contains("hiçbir şablonda kullanılmıyor"));
    }

    internal static Dictionary<string, FieldInput> Fill(TemplateSet set)
    {
        var inputs = Resolver.NewCase(set);
        foreach (var f in set.Fields.Where(f => !f.IsComputed))
        {
            var input = inputs[f.Key];
            if (input.Blank || !string.IsNullOrWhiteSpace(input.Raw) || !string.IsNullOrWhiteSpace(f.Definition?.DefaultFrom)) continue;
            input.Raw = f.Type switch
            {
                FieldType.Tarih => f.Key.StartsWith("TAKSİT") ? "10.12.2026" : f.Key.Contains("GİRİŞ") ? "01.01.2020"
                    : f.Key.Contains("FESİH") || f.Key.Contains("SÖZLEŞMESİ") || f.Key.Contains("SATIN") ? "01.09.2026" : "05.10.2026",
                FieldType.Para => "10.000",
                FieldType.Tckn => "10000000146",
                FieldType.Iban => "TR33 0006 1005 1978 6457 8413 26",
                FieldType.Telefon => "0532 123 45 67",
                _ => f.Definition?.Pattern switch
                {
                    "^\\d{4}$" => "2026",
                    "^\\d+$" => "12345",
                    "^\\d{10,11}$" => "1234567890",
                    "^\\d{1,2}[:.]\\d{2}$" => "14:00",
                    { } p when p.Contains('@') => "ornek@ornek.com",
                    _ => "Örnek " + Tr.Lower(f.Name),
                },
            };
        }
        return inputs;
    }

    [Theory, MemberData(nameof(Sets))]
    public void ProducesEveryDocumentWithoutLeftOverVariables(string folder)
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, folder));
        var inputs = Fill(set);
        // The agreed amount must match the payments.
        var amount = set.Field("ANLAŞMA TUTARI")!;
        var expected = amount.IsComputed
            ? TemplateSet.ComputedTerms(amount.Definition!.Computed).Count(t => !inputs[Tr.Key(t.Name)].Blank) * 10_000m
            : 10_000m;
        inputs[Tr.Key("TAKSİT1 TUTARI")].Raw = expected.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var res = Resolver.Resolve(set, inputs);
        Assert.True(res.IsValid, string.Join("; ", res.Fields.Values.Where(f => f.Error is not null).Select(f => $"{f.Field.Name}: {f.Error}").Concat(res.Errors)));

        var dir = TestPaths.TempDir();
        var plan = Generator.Plan(set, res, dir);
        Assert.Equal(7, plan.Count);
        Generator.Write(plan, res, new SuffixOverrides(), overwrite: false);
        foreach (var p in plan)
        {
            var text = DocxText.All(File.ReadAllBytes(p.Path));
            Assert.DoesNotContain("{{{", text);
            Assert.DoesNotContain("}}}", text);
            Assert.Contains("ÖRNEK TARAF1 ADI", text);
        }
        // Optional lines left blank are removed.
        var agreement = DocxText.All(File.ReadAllBytes(plan.Single(p => p.Path.Contains("Anlaşma Belgesi")).Path));
        Assert.DoesNotContain("Vekili", agreement);
        Assert.DoesNotContain("tarihinde  (", agreement);
    }

    /// <summary>Kinds with texts from real samples: the meeting is in person or by video conference, never both.</summary>
    [Theory]
    [InlineData("Kira"), InlineData("Ticari"), InlineData("Tüketici"), InlineData("İşçi - İşveren")]
    public void MeetingIsInPersonOrByVideoConference(string folder)
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, folder));
        var inputs = Fill(set);
        inputs[Tr.Key("TAKSİT1 TUTARI")].Raw = "10000";
        var docs = set.Documents.Where(d => d.Entry.Template.Contains("Davet") || d.Entry.Template.Contains("İlk Oturum") ||
                                            d.Entry.Template.Contains("Anlaşamama"))
            .Select(d => d.Entry.Template).ToHashSet();
        string Texts(Dictionary<string, FieldInput> values)
        {
            var res = Resolver.Resolve(set, values, Resolver.ActiveFields(set, docs));
            Assert.True(res.IsValid, string.Join("; ", res.Errors.Concat(res.Fields.Values.Where(f => f.Error is not null).Select(f => $"{f.Field.Name}: {f.Error}"))));
            var plan = Generator.Plan(set, res, TestPaths.TempDir(), docs);
            Generator.Write(plan, res, new SuffixOverrides(), overwrite: false);
            return string.Join("\n", plan.Select(p => DocxText.All(File.ReadAllBytes(p.Path))));
        }

        // In person (default): the meeting place is written, no video conference text, no e-signature note.
        var inPerson = Texts(inputs);
        Assert.Contains("adresindeki toplantıya geldiler", inPerson);
        Assert.DoesNotContain("telekonferans", inPerson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("e-imzalıdır", inPerson);
        Assert.DoesNotContain("{{{", inPerson);

        // Video conference, signed electronically.
        inputs[Tr.Key("TOPLANTI YERİ")].Blank = true;
        inputs[Tr.Key("TELEKONFERANS")] = new FieldInput { Raw = "taraf vekillerinin talebi doğrultusunda telekonferans yoluyla" };
        inputs[Tr.Key("TELEKONFERANS KATILIM")] = new FieldInput { Raw = "başvurucu vekili Av. Ayşe YILMAZ 0 (532) 000 00 00 no’lu hattan" };
        inputs[Tr.Key("İMZA ŞEKLİ")] = new FieldInput { Raw = "hep birlikte elektronik imza marifetiyle" };
        inputs[Tr.Key("E-İMZA NOTU")] = new FieldInput { Raw = "e-imzalıdır!" };
        var video = Texts(inputs);
        Assert.Contains("telekonferans yoluyla gerçekleşecektir", video);
        Assert.Contains("taraflar telekonferans görüşmeye alındı", video);
        Assert.Contains("elektronik imza marifetiyle imza altına alınmıştır", video);
        Assert.Contains("e-imzalıdır!", video);
        Assert.DoesNotContain("adresindeki toplantıya geldiler", video);
        Assert.DoesNotContain("TOPLANTI YERİ", video);

        // Both filled: refused.
        inputs[Tr.Key("TOPLANTI YERİ")].Blank = false;
        inputs[Tr.Key("TOPLANTI YERİ")].Raw = "Örnek adres";
        var both = Resolver.Resolve(set, inputs, Resolver.ActiveFields(set, docs));
        Assert.Contains(both.Errors, e => e.Contains("telekonferans"));
    }

    [Fact]
    public void ChosenDocumentsLimitTheFieldsThatMustBeFilled()
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, "Ticari"));
        var docs = new HashSet<string>(set.Documents.Where(d => d.Entry.Template.Contains("Anlaşamama")).Select(d => d.Entry.Template));
        var active = Resolver.ActiveFields(set, docs);
        Assert.DoesNotContain(Tr.Key("ANLAŞMA TUTARI"), active);
        Assert.DoesNotContain(Tr.Key("ARABULUCU IBAN"), active);
        Assert.Contains(Tr.Key("GÖRÜŞME SÜRESİ"), active);
        Assert.Contains(Tr.Key("TARAF1 ADI"), active);

        var inputs = Fill(set);
        inputs[Tr.Key("ANLAŞMA TUTARI")].Raw = "";            // not needed for this document
        var res = Resolver.Resolve(set, inputs, active);
        Assert.True(res.IsValid);
        var plan = Generator.Plan(set, res, TestPaths.TempDir(), docs);
        Assert.Single(plan);
    }
}
