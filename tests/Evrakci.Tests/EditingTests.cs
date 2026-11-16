using Evrakci.Core;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates.Docx;
using Evrakci.Core.Text;

namespace Evrakci.Tests;

public class EditingTests
{
    static TemplateSet NewSet(params (string Name, DocxBuilder Doc)[] docs)
    {
        var dir = TestPaths.TempDir();
        foreach (var (name, doc) in docs) File.WriteAllBytes(Path.Combine(dir, name), doc.Build());
        return TemplateSet.Open(dir);
    }

    [Fact]
    public void MakesVariableFromTextInAllDocumentsAndRecordsSettings()
    {
        var set = NewSet(
            ("a.docx", new DocxBuilder().Para("Başvurucu **KORAY** KOVAN’ın talebi; KORAY KOVANLAR değil.").Field("Adı", "KORAY KOVAN")),
            ("b.docx", new DocxBuilder().Para("Sayın KORAY KOVAN, {{{TARİH}}} tarihinde.")));

        var found = TemplateEditing.FindText(set, "KORAY KOVAN");
        // "KORAY KOVANLAR" is not a whole-word match; the bold split in a.docx is still found.
        Assert.Equal(3, found.Count);

        var changes = TemplateEditing.MakeVariable(set, found, "Taraf1 Adı", new FieldDefinition { Type = "Metin", Label = "Başvurucu", Group = "Taraflar" });
        Assert.Equal(3, changes.Count);   // two templates and set.json

        var reopened = TemplateSet.Open(set.Folder);
        var field = reopened.Field("TARAF1 ADI");
        Assert.NotNull(field);
        Assert.Equal(3, field!.Occurrences);
        Assert.Equal("Başvurucu", field.Label);
        Assert.Contains("{{{Taraf1 Adı}}}’ın talebi", DocxText.All(File.ReadAllBytes(Path.Combine(set.Folder, "a.docx"))));

        TemplateEditing.Restore(changes);
        var restored = TemplateSet.Open(set.Folder);
        Assert.Null(restored.Field("TARAF1 ADI"));
        Assert.False(File.Exists(restored.SetFilePath));
    }

    [Fact]
    public void ReplacesOnlyTheChosenPlace()
    {
        var set = NewSet(("a.docx", new DocxBuilder().Para("bir iki bir")));
        var p = set.Documents[0].Template!.SourceParagraphs.Single(x => x.Text == "bir iki bir");
        TemplateEditing.Replace([new TextLocation(set.Documents[0], p.Id, 8, 3)], "üç");
        Assert.Equal("bir iki üç", DocxText.Paragraphs(File.ReadAllBytes(set.Documents[0].Path))[0]);
    }

    [Fact]
    public void RenamesVariableEverywhereKeepingTypeAndWords()
    {
        var set = NewSet(("a.docx", new DocxBuilder().Para("{{{TUTAR:Para}}} ({{{tutar.Yazıyla}}}) ve {{{TUTAR}}}’ı")));
        set.DefinitionFor("TUTAR").Label = "Tutar";
        set.Definition.Fields["TOPLAM"] = new FieldDefinition { Computed = "TUTAR + TUTAR" };
        set.Definition.Save(set.SetFilePath);
        set = TemplateSet.Open(set.Folder);

        TemplateEditing.RenameVariable(set, "TUTAR", "BEDEL");
        var text = DocxText.All(File.ReadAllBytes(set.Documents[0].Path));
        Assert.Equal("{{{BEDEL:Para}}} ({{{BEDEL.Yazıyla}}}) ve {{{BEDEL}}}’ı", text);
        var def = SetDefinition.Load(set.SetFilePath);
        Assert.True(def.Fields.ContainsKey("BEDEL"));
        Assert.False(def.Fields.ContainsKey("TUTAR"));
        Assert.Equal("BEDEL + BEDEL", def.Fields["TOPLAM"].Computed);
    }

    [Fact]
    public void RejectsBadVariableNames()
    {
        var set = NewSet(("a.docx", new DocxBuilder().Para("x")));
        Assert.NotNull(TemplateEditing.CheckName(set, "  "));
        Assert.NotNull(TemplateEditing.CheckName(set, "A:B"));
        Assert.NotNull(TemplateEditing.CheckName(set, "A}}}B"));
        Assert.Null(TemplateEditing.CheckName(set, "TARAF-1 ADI (SOYADI)"));
    }

    [Fact]
    public void LibraryCopiesRenamesAndManagesDocuments()
    {
        var lib = new TemplateLibrary(TestPaths.TempDir());
        var builtIn = Path.Combine(lib.BuiltInRoot, "Ticari");
        Directory.CreateDirectory(lib.BuiltInRoot);
        CopyDir(Path.Combine(TestPaths.BuiltInSets, "Ticari"), builtIn);

        var copy = lib.Copy(builtIn, "Benim Ticari Setim");
        Assert.True(lib.IsOwn(copy));
        Assert.Equal(2, lib.List().Count);
        Assert.Contains(lib.List(), e => e.Name == "Benim Ticari Setim" && e.Category == "Ticari" && !e.BuiltIn);
        Assert.Throws<EvrakciException>(() => lib.Rename(builtIn, "x"));

        var renamed = lib.Rename(copy, "Yeni Ad");
        Assert.EndsWith("Yeni Ad", renamed);
        var set = TemplateSet.Open(renamed);
        Assert.Equal("Yeni Ad", set.Name);

        var extra = Path.Combine(TestPaths.TempDir(), "Ek.docx");
        File.WriteAllBytes(extra, new DocxBuilder().Para("{{{TARAF1 ADI}}} ek").Build());
        TemplateLibrary.AddDocument(set, extra);
        set = TemplateSet.Open(renamed);
        Assert.Equal(8, set.Documents.Count);
        TemplateLibrary.RemoveDocument(set, "Ek.docx");
        TemplateLibrary.RenameDocument(TemplateSet.Open(renamed), "02 İlk Toplantıya Davet.docx", "Davet Mektubu");
        set = TemplateSet.Open(renamed);
        Assert.Equal(7, set.Documents.Count);
        Assert.Contains(set.Documents, d => d.Entry.Template == "Davet Mektubu.docx");
        Assert.DoesNotContain(set.Issues, i => i.Severity == IssueSeverity.Error);
    }

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.EnumerateFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }
}
