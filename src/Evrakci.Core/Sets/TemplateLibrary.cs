using Evrakci.Core.Templates;
using Evrakci.Core.Text;

namespace Evrakci.Core.Sets;

/// <summary>A set in the library, as the set list shows it.</summary>
public sealed record LibraryEntry(string Folder, string Name, string Category, string? Description, bool BuiltIn, int Documents);

/// <summary>
/// The user's template library: ready-made sets shipped with the app ("Hazır Şablonlar") and the
/// user's own sets ("Setlerim"), each a folder with templates and a set.json. Both kinds can be
/// edited; ready-made sets can be restored from the app, own sets can be renamed and deleted.
/// </summary>
public sealed class TemplateLibrary(string root)
{
    public const string BuiltInFolderName = "Hazır Şablonlar";
    public const string UserFolderName = "Setlerim";
    public const string OtherCategory = "Diğer";

    /// <summary>Display order of the categories of the ready-made sets.</summary>
    public static readonly string[] CategoryOrder =
    [
        "İşçi – İşveren", "Ticari", "Tüketici", "Kira", "Ortaklığın Giderilmesi", "Kat Mülkiyeti", "Komşu Hakkı",
        "Tarımsal Üretim Sözleşmesi", "İhtiyari (Genel)",
    ];

    public string Root { get; } = Path.GetFullPath(root);
    public string BuiltInRoot => Path.Combine(Root, BuiltInFolderName);
    public string UserRoot => Path.Combine(Root, UserFolderName);

    public bool IsBuiltIn(string folder) => IsUnder(folder, BuiltInRoot);
    public bool IsOwn(string folder) => IsUnder(folder, UserRoot);

    static bool IsUnder(string folder, string parent)
    {
        var f = Path.GetFullPath(folder).TrimEnd('\\', '/');
        var p = parent.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return f.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    public List<LibraryEntry> List()
    {
        var list = new List<LibraryEntry>();
        foreach (var (dir, builtIn) in new[] { (BuiltInRoot, true), (UserRoot, false) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var folder in Directory.EnumerateDirectories(dir))
            {
                var entry = Describe(folder, builtIn);
                if (entry is not null) list.Add(entry);
            }
        }
        return list
            .OrderBy(e => CategoryRank(e.Category))
            .ThenBy(e => e.Category, StringComparer.Create(Tr.Culture, true))
            .ThenBy(e => e.BuiltIn ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.Create(Tr.Culture, true))
            .ToList();
    }

    public static int CategoryRank(string category)
    {
        int i = Array.IndexOf(CategoryOrder, category);
        return i >= 0 ? i : category == OtherCategory ? int.MaxValue : CategoryOrder.Length;
    }

    static LibraryEntry? Describe(string folder, bool builtIn)
    {
        var file = Path.Combine(folder, SetDefinition.FileName);
        int docs = Directory.EnumerateFiles(folder).Count(TemplateLoader.IsTemplateFile);
        if (!File.Exists(file)) return docs == 0 ? null : new LibraryEntry(folder, Path.GetFileName(folder), OtherCategory, null, builtIn, docs);
        try
        {
            var def = SetDefinition.Load(file);
            return new LibraryEntry(folder, string.IsNullOrWhiteSpace(def.Name) ? Path.GetFileName(folder) : def.Name!,
                string.IsNullOrWhiteSpace(def.Category) ? OtherCategory : def.Category!, def.Description, builtIn,
                def.Documents is { Count: > 0 } d ? d.Count : docs);
        }
        catch (EvrakciException)
        {
            return new LibraryEntry(folder, Path.GetFileName(folder), OtherCategory, "set.json okunamadı.", builtIn, docs);
        }
    }

    /// <summary>Copies a set into "Setlerim" under a new name and returns the new folder.</summary>
    public string Copy(string sourceFolder, string newName, string? category = null)
    {
        newName = CheckSetName(newName);
        var target = UniqueFolder(UserRoot, newName);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(sourceFolder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$") || name.StartsWith('.')) continue;
            File.Copy(file, Path.Combine(target, name));
        }
        var setFile = Path.Combine(target, SetDefinition.FileName);
        var def = File.Exists(setFile) ? SetDefinition.Load(setFile) : new SetDefinition();
        def.Name = newName;
        if (!string.IsNullOrWhiteSpace(category)) def.Category = category;
        def.Save(setFile);
        return target;
    }

    /// <summary>Creates an empty own set (set.json only) and returns its folder.</summary>
    public string Create(string name, string category)
    {
        name = CheckSetName(name);
        var target = UniqueFolder(UserRoot, name);
        Directory.CreateDirectory(target);
        new SetDefinition { Name = name, Category = string.IsNullOrWhiteSpace(category) ? OtherCategory : category.Trim(), Documents = new() }
            .Save(Path.Combine(target, SetDefinition.FileName));
        return target;
    }

    /// <summary>Renames an own set (folder and set.json) and returns the new folder.</summary>
    public string Rename(string folder, string newName, string? category = null)
    {
        if (!IsOwn(folder)) throw new EvrakciException("Yalnızca “Setlerim” altındaki setler yeniden adlandırılabilir. Hazır şablonun kopyasını oluşturun.");
        newName = CheckSetName(newName);
        var setFile = Path.Combine(folder, SetDefinition.FileName);
        var def = File.Exists(setFile) ? SetDefinition.Load(setFile) : new SetDefinition();
        def.Name = newName;
        if (!string.IsNullOrWhiteSpace(category)) def.Category = category.Trim();
        def.Save(setFile);
        var clean = Tr.SanitizeFileName(newName);
        if (string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), clean, StringComparison.OrdinalIgnoreCase)) return folder;
        var target = UniqueFolder(UserRoot, newName);
        try { Directory.Move(folder, target); }
        catch (IOException) { throw new EvrakciException("Klasör yeniden adlandırılamadı. İçindeki bir belge açık olabilir."); }
        return target;
    }

    /// <summary>Copies a .docx / .udf into the set and adds it to the document list. Returns the template name.</summary>
    public static string AddDocument(TemplateSet set, string file)
    {
        if (!TemplateLoader.IsTemplateFile(file)) throw new EvrakciException("Yalnızca .docx ve .udf belgeleri eklenebilir.");
        // Check it opens before copying anything.
        TemplateLoader.Load(file, set.Matcher);
        var name = Path.GetFileName(file);
        var target = Path.Combine(set.Folder, name);
        if (!string.Equals(Path.GetFullPath(file), target, StringComparison.OrdinalIgnoreCase))
        {
            for (int n = 2; File.Exists(target); n++)
                target = Path.Combine(set.Folder, $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}");
            File.Copy(file, target);
        }
        var def = set.Definition;
        def.Documents ??= set.Documents.Select(d => d.Entry).ToList();
        var template = Path.GetFileName(target);
        if (!def.Documents.Any(d => string.Equals(d.Template, template, StringComparison.OrdinalIgnoreCase)))
            def.Documents.Add(new DocumentEntry { Template = template });
        def.Save(set.SetFilePath);
        return template;
    }

    /// <summary>Removes a document from the set's list. The caller decides what happens to the file.</summary>
    public static void RemoveDocument(TemplateSet set, string template)
    {
        var def = set.Definition;
        def.Documents ??= set.Documents.Select(d => d.Entry).ToList();
        def.Documents.RemoveAll(d => string.Equals(d.Template, template, StringComparison.OrdinalIgnoreCase));
        def.Save(set.SetFilePath);
    }

    /// <summary>Moves a document up or down in the set's list.</summary>
    public static void MoveDocument(TemplateSet set, string template, int delta)
    {
        var def = set.Definition;
        def.Documents ??= set.Documents.Select(d => d.Entry).ToList();
        int i = def.Documents.FindIndex(d => string.Equals(d.Template, template, StringComparison.OrdinalIgnoreCase));
        int j = i + delta;
        if (i < 0 || j < 0 || j >= def.Documents.Count) return;
        (def.Documents[i], def.Documents[j]) = (def.Documents[j], def.Documents[i]);
        def.Save(set.SetFilePath);
    }

    /// <summary>Renames a template file of the set (keeping its extension) and returns the new template name.</summary>
    public static string RenameDocument(TemplateSet set, string template, string newName)
    {
        var ext = Path.GetExtension(template);
        var clean = Tr.SanitizeFileName(newName.Trim());
        if (clean.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) clean = clean[..^ext.Length];
        var target = clean + ext;
        if (string.Equals(target, template, StringComparison.Ordinal)) return template;
        var from = Path.Combine(set.Folder, template);
        var to = Path.Combine(set.Folder, target);
        if (!string.Equals(target, template, StringComparison.OrdinalIgnoreCase) && File.Exists(to))
            throw new EvrakciException("Bu adda bir belge zaten var.");
        try { File.Move(from, to); }
        catch (IOException) { throw new EvrakciException("Belge yeniden adlandırılamadı. Word'de açık olabilir."); }
        var def = set.Definition;
        def.Documents ??= set.Documents.Select(d => d.Entry).ToList();
        foreach (var d in def.Documents.Where(d => string.Equals(d.Template, template, StringComparison.OrdinalIgnoreCase)))
        {
            if (d.Output is null || string.Equals(d.Output, template, StringComparison.OrdinalIgnoreCase)) d.Output = null;
            d.Template = target;
        }
        def.Save(set.SetFilePath);
        return target;
    }

    static string CheckSetName(string name)
    {
        var n = Tr.CollapseSpaces(name ?? "");
        if (n.Length == 0) throw new EvrakciException("Set adı boş olamaz.");
        if (n.Length > 100) throw new EvrakciException("Set adı çok uzun.");
        return n;
    }

    static string UniqueFolder(string parent, string name)
    {
        Directory.CreateDirectory(parent);
        var clean = Tr.SanitizeFileName(name);
        var path = Path.Combine(parent, clean);
        for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Path.Combine(parent, $"{clean} ({n})");
        return path;
    }
}
