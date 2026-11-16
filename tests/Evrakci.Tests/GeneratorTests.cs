using System.Security.Cryptography;
using Evrakci.Core.Cases;
using Evrakci.Core.Output;
using Evrakci.Core.Sets;
using Evrakci.Core.Text;

namespace Evrakci.Tests;

/// <summary>Writing documents from a ready-made set filled with made-up values.</summary>
public class GeneratorTests
{
    static (TemplateSet Set, Dictionary<string, FieldInput> Inputs) Kira()
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, "Kira"));
        var inputs = BuiltInSetTests.Fill(set);
        inputs[Tr.Key("ANLAŞMA TUTARI")].Raw = "30000";
        inputs[Tr.Key("TAKSİT1 TUTARI")].Raw = "30000";
        return (set, inputs);
    }

    static Resolution Valid(TemplateSet set, Dictionary<string, FieldInput> inputs)
    {
        var res = Resolver.Resolve(set, inputs);
        Assert.True(res.IsValid, string.Join("; ", res.Errors.Concat(res.Fields.Values.Where(f => f.Error is not null).Select(f => $"{f.Field.Name}: {f.Error}"))));
        return res;
    }

    [Fact]
    public void EmptyCaseIsNotValid()
    {
        var set = TemplateSet.Open(Path.Combine(TestPaths.BuiltInSets, "Kira"));
        Assert.False(Resolver.Resolve(set, Resolver.NewCase(set)).IsValid);
    }

    [Fact]
    public void PaymentsThatDoNotAddUpBlockGeneration()
    {
        var (set, inputs) = Kira();
        Valid(set, inputs);
        inputs[Tr.Key("TAKSİT1 TUTARI")].Raw = "25000";
        var res = Resolver.Resolve(set, inputs);
        Assert.Contains(res.Errors, e => e.Contains("anlaşma tutarına eşit değil"));
    }

    [Fact]
    public void WritesOutputsWithoutTouchingTemplates()
    {
        var (set, inputs) = Kira();
        var res = Valid(set, inputs);
        var hashes = set.Documents.ToDictionary(d => d.Path, d => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(d.Path))));
        var outDir = TestPaths.TempDir();
        var plan = Generator.Plan(set, res, outDir);
        Assert.All(plan, p => Assert.Equal(Path.Combine(outDir, "ÖRNEK TARAF1 ADI - Kira"), Path.GetDirectoryName(p.Path)));
        Generator.Write(plan, res, new SuffixOverrides(), overwrite: false);
        Assert.All(plan, p => Assert.True(File.Exists(p.Path)));
        // A second run must not overwrite silently.
        Assert.Throws<Evrakci.Core.EvrakciException>(() => Generator.Write(plan, res, new SuffixOverrides(), overwrite: false));
        Generator.Write(plan, res, new SuffixOverrides(), overwrite: true);
        foreach (var d in set.Documents)
            Assert.Equal(hashes[d.Path], Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(d.Path))));
        Directory.Delete(outDir, true);
    }

    [Fact]
    public void RefusesToWriteOverTemplates()
    {
        var (set, inputs) = Kira();
        var res = Valid(set, inputs);
        set.Definition.OutputFolder = null;
        Assert.Throws<Evrakci.Core.EvrakciException>(() => Generator.Plan(set, res, set.Folder));
    }

    [Fact]
    public void SuffixOverrideIsApplied()
    {
        var (set, inputs) = Kira();
        var res = Valid(set, inputs);
        var (doc, occ) = set.Documents
            .SelectMany(d => d.Template!.Occurrences.Select(o => (Doc: d, Occ: o)))
            .First(x => x.Occ.Suffix is not null);
        var ov = new SuffixOverrides();
        ov.Set(doc.Entry.Template, occ.Occurrence, "qzx");
        Assert.DoesNotContain("qzx", DocxText.All(Generator.Render(doc, res, new SuffixOverrides())));
        Assert.Contains("’qzx", DocxText.All(Generator.Render(doc, res, ov)));
    }
}
