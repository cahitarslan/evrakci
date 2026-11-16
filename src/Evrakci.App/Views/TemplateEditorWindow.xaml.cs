using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Evrakci.App.Services;
using Evrakci.Core;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates;
using Evrakci.Core.Text;
using Wpf.Ui.Controls;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace Evrakci.App.Views;

/// <summary>
/// In-app editor for one template of a set: the text can be typed and deleted freely, made bold /
/// italic / underlined and aligned, paragraphs added and removed; selected text can be turned into
/// a variable and variables inserted or renamed. "Kaydet" writes the template (Word or UDF); paragraphs
/// that were not touched keep their original formatting exactly.
/// </summary>
public partial class TemplateEditorWindow : FluentWindow
{
    /// <summary>
    /// The editor was opened from a file: it edits the file's working copy of the set. "Bu dosyaya
    /// kaydet" keeps changes in the file; <see cref="PublishToTemplate"/> writes them to the set itself.
    /// </summary>
    public sealed record CaseEditing(string FileTitle, Func<Task> PublishToTemplate);

    readonly string _folder;
    readonly Stack<List<FileChange>> _saves = new();
    readonly DispatcherTimer _highlightTimer;
    readonly Brush _chip;
    readonly Action _saved;
    readonly CaseEditing? _case;
    TemplateSet _set = null!;
    SetDocument? _doc;
    HashSet<int> _shown = new();
    DateTime _docWritten;
    bool _dirty, _loading;

    TemplateEditorWindow(string folder, string? template, Action saved, CaseEditing? forCase)
    {
        _folder = folder;
        _saved = saved;
        _case = forCase;
        InitializeComponent();
        _chip = (Brush)FindResource("VariableChipBrush");
        if (Application.Current.MainWindow is { IsVisible: true } main && main != this) Owner = main;
        _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _highlightTimer.Tick += (_, _) => { _highlightTimer.Stop(); Highlight(); };
        InputBindings.Add(new KeyBinding(new RelayCommandLite(() => OnSave(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
        if (_case is not null)
        {
            SaveButton.Content = "Bu dosyaya kaydet";
            SaveButton.ToolTip = "Değişiklik yalnızca bu dosyada kullanılır; şablonun kendisi değişmez (Ctrl+S)";
            PublishButton.Visibility = Visibility.Visible;
            ModeText.Text = $"Bu dosya için düzenleniyor: {_case.FileTitle}";
        }
        Reload(template);
        Activated += (_, _) => ReloadIfChangedOutside();
        Closing += OnClosing;
    }

    /// <summary>
    /// Opens the editor beside the main window (not modal, so the main window can still be scrolled
    /// and moved). <paramref name="saved"/> runs after every write so the form can follow.
    /// </summary>
    public static TemplateEditorWindow Open(string folder, string? template, Action saved, CaseEditing? forCase = null)
    {
        var w = new TemplateEditorWindow(folder, template, saved, forCase);
        w.Show();
        return w;
    }

    /// <summary>Brings the open editor forward, switching to <paramref name="template"/> when given.</summary>
    public void Show(string? template)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (template is not null && Docs.ItemsSource is IEnumerable<SetDocument> docs &&
            docs.FirstOrDefault(d => string.Equals(d.Entry.Template, template, StringComparison.OrdinalIgnoreCase)) is { } target)
            Docs.SelectedItem = target;
    }

    void Changed() => _saved();

    sealed class RelayCommandLite(Action a) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => a();
    }

    // ------------------------------------------------------------------ loading

    void Reload(string? keep)
    {
        _set = TemplateSet.Open(_folder);
        Title = Bar.Title = $"Şablon düzenleyici — {_set.Name}";
        var docs = _set.Documents.Where(d => d.Template is not null).ToList();
        Docs.SelectionChanged -= OnDocChanged;
        Docs.ItemsSource = docs;
        Docs.SelectedItem = docs.FirstOrDefault(d => string.Equals(d.Entry.Template, keep, StringComparison.OrdinalIgnoreCase)) ?? docs.FirstOrDefault();
        Docs.SelectionChanged += OnDocChanged;
        _doc = Docs.SelectedItem as SetDocument;
        Load();
    }

    void Load(bool keepScroll = true)
    {
        RevertButton.IsEnabled = _saves.Count > 0;
        _loading = true;
        try
        {
            var offset = keepScroll ? Editor.VerticalOffset : 0;
            if (_doc?.Template is null)
            {
                Editor.Document = new FlowDocument(new WpfParagraph(new Run("Bu sette düzenlenecek belge yok. Set ekranından belge ekleyin.")));
                Editor.IsReadOnly = true;
                Status.Text = "";
                return;
            }
            Editor.IsReadOnly = false;
            _docWritten = File.GetLastWriteTimeUtc(_doc.Path);
            var source = _doc.Template.SourceParagraphs.ToDictionary(p => p.Id);
            Editor.Document = EditorDocument.Build(_doc.Template.Preview(EmptyValueSource.Instance), source, _chip, out _shown);
            Editor.ScrollToVerticalOffset(offset);
            SetDirty(false);
            int vars = _doc.Template.Occurrences.Select(o => o.Ref.Key).Distinct().Count();
            Status.Text = $"{_doc.Entry.Template}  ·  {vars} değişken";
        }
        finally { _loading = false; }
    }

    void SetDirty(bool dirty)
    {
        _dirty = dirty;
        DirtyDot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = dirty;
        if (dirty) Status.Text = "Kaydedilmemiş değişiklikler var";
    }

    async void OnDocChanged(object sender, SelectionChangedEventArgs e)
    {
        var target = Docs.SelectedItem as SetDocument;
        if (target == _doc) return;
        if (!await ConfirmLeave())
        {
            Docs.SelectionChanged -= OnDocChanged;
            Docs.SelectedItem = _doc;
            Docs.SelectionChanged += OnDocChanged;
            return;
        }
        _doc = target;
        Load(keepScroll: false);
    }

    void ReloadIfChangedOutside()
    {
        if (_doc is null || !File.Exists(_doc.Path) || _dirty) return;
        if (File.GetLastWriteTimeUtc(_doc.Path) == _docWritten) return;
        _saves.Clear();   // the file changed outside; earlier snapshots no longer apply
        TryRun(() => Reload(_doc.Entry.Template));
        Changed();
    }

    /// <summary>Asks to save unsaved edits. False means: stay.</summary>
    async Task<bool> ConfirmLeave()
    {
        if (!_dirty) return true;
        var choice = _case is null
            ? await Dialogs.Ask("Bu belgede kaydedilmemiş değişiklikler var.", "Kaydedilmedi", "Kaydet", "Kaydetme")
            : await Dialogs.Ask("Bu belgede kaydedilmemiş değişiklikler var. Şablonun kendisine yazmak için önce “Şablona kaydet”i kullanın.",
                "Kaydedilmedi", "Bu dosyaya kaydet", "Kaydetme");
        if (choice == Choice.Cancel) return false;
        if (choice == Choice.Primary) return await Save();
        SetDirty(false);
        return true;
    }

    async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_dirty) return;
        e.Cancel = true;
        if (await ConfirmLeave())
        {
            Closing -= OnClosing;
            await Dispatcher.InvokeAsync(Close);
        }
    }

    // ------------------------------------------------------------------ editing

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (!_dirty) SetDirty(true);
        _highlightTimer.Stop();
        _highlightTimer.Start();
    }

    /// <summary>Gives variables (whole {{{…}}} tokens) the green background and removes it elsewhere.</summary>
    void Highlight()
    {
        _loading = true;
        try
        {
            Editor.BeginChange();
            // Applying the background splits runs, which changes the document: collect the paragraphs and
            // the ranges (whose pointers follow such changes) before touching anything.
            foreach (var p in EditorDocument.Paragraphs(Editor.Document).ToList())
            {
                var map = ParagraphText.Of(p);
                foreach (var piece in map.Pieces)
                    if (piece.Run is { } r && ReferenceEquals(r.Background, _chip)) r.Background = null;
                var ranges = _set.Matcher.Find(map.Text)
                    .Select(m => new TextRange(map.PositionAt(m.Start), map.PositionAt(m.Start + m.DelimitedLength))).ToList();
                foreach (var range in ranges) range.ApplyPropertyValue(TextElement.BackgroundProperty, _chip);
            }
        }
        finally
        {
            Editor.EndChange();
            _loading = false;
        }
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!Editor.Selection.IsEmpty && SelectionProblem() is null) SelectionPopup.IsOpen = true;
        }, DispatcherPriority.Input);

    /// <summary>Why the selection cannot become a variable, or null when it can.</summary>
    string? SelectionProblem()
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return "Önce değişkene çevrilecek metni seçin.";
        if (sel.Start.Paragraph is null || sel.Start.Paragraph != sel.End.Paragraph) return "Seçim tek bir paragraf içinde olmalı.";
        var text = sel.Text.Trim();
        if (text.Length == 0) return "Seçim boş.";
        if (text.Contains(_set.Definition.Delimiters.Open) || text.Contains(_set.Definition.Delimiters.Close))
            return "Seçim bir değişkenin üzerine taşıyor. Yalnızca düz metni seçin.";
        if (text.IndexOfAny(['\t', '\r', '\n']) >= 0) return "Seçim sekme ya da satır sonu içeremez. Daha küçük bir parça seçin.";
        return null;
    }

    async void OnMakeVariable(object sender, RoutedEventArgs e)
    {
        SelectionPopup.IsOpen = false;
        if (_doc is null) return;
        if (SelectionProblem() is { } problem) { await Dialogs.Info(problem, "Değişken yap"); return; }
        var sel = Editor.Selection;
        var para = sel.Start.Paragraph!;
        var map = ParagraphText.Of(para);
        int start = map.OffsetOf(sel.Start), end = map.OffsetOf(sel.End);
        while (start < end && char.IsWhiteSpace(map.Text[start])) start++;
        while (end > start && char.IsWhiteSpace(map.Text[end - 1])) end--;
        var text = map.Text[start..end];

        var inDoc = FindInEditor(text);
        if (!inDoc.Any(x => x.Paragraph == para && x.Start == start)) inDoc.Insert(0, (para, start));
        var others = TemplateEditing.FindText(_set, text).Where(l => l.Document.Entry.Template != _doc.Entry.Template).ToList();
        var answer = MakeVariableWindow.Ask(_set, text, inDoc.Count, inDoc.Count + others.Count);
        if (answer is null) return;

        var token = TemplateEditing.Token(_set, answer.Name);
        var targets = answer.Scope == EditScope.Here ? [(para, start)] : inDoc;
        Editor.BeginChange();
        // Replace from the end of each paragraph so earlier offsets stay valid.
        foreach (var (p, s) in targets.OrderByDescending(t => t.Start))
        {
            var m = ParagraphText.Of(p);
            new TextRange(m.PositionAt(s), m.PositionAt(s + text.Length)).Text = token;
        }
        Editor.EndChange();
        Highlight();

        // Saved at once so the set's settings and the other documents stay in step.
        if (!await Save(reload: false, notify: false)) return;
        try
        {
            if (answer.Scope == EditScope.Set && others.Count > 0)
                _saves.Push(TemplateEditing.MakeVariable(TemplateSet.Open(_folder), Relocate(others), answer.Name, answer.Settings));
            else if (answer.Settings is not null)
                _saves.Push([TemplateEditing.UpdateDefinition(TemplateSet.Open(_folder), answer.Name, answer.Settings)]);
        }
        catch (EvrakciException ex) { await Dialogs.Error(ex.Message, "Diğer belgeler değiştirilemedi"); }
        Reload(_doc.Entry.Template);
        Changed();
        Status.Text = $"✓ “{text}” → {token}" + (_case is null ? "" : "  ·  bu dosyaya kaydedildi");
    }

    /// <summary>Locations found in an earlier opening of the set, mapped to a freshly opened one.</summary>
    List<TextLocation> Relocate(List<TextLocation> locations)
    {
        var set = TemplateSet.Open(_folder);
        return locations.Select(l => l with { Document = set.Documents.First(d => d.Entry.Template == l.Document.Entry.Template) }).ToList();
    }

    /// <summary>Whole-word occurrences of plain text in the editor (outside variables).</summary>
    List<(WpfParagraph Paragraph, int Start)> FindInEditor(string text)
    {
        var list = new List<(WpfParagraph, int)>();
        foreach (var p in EditorDocument.Paragraphs(Editor.Document))
        {
            var map = ParagraphText.Of(p);
            var vars = _set.Matcher.Find(map.Text);
            int i = 0;
            while ((i = map.Text.IndexOf(text, i, StringComparison.Ordinal)) >= 0)
            {
                int end = i + text.Length;
                bool inside = vars.Any(v => i < v.Start + v.DelimitedLength && v.Start < end);
                bool word = (!char.IsLetterOrDigit(text[0]) || i == 0 || !char.IsLetterOrDigit(map.Text[i - 1])) &&
                            (!char.IsLetterOrDigit(text[^1]) || end == map.Text.Length || !char.IsLetterOrDigit(map.Text[end]));
                if (!inside && word) list.Add((p, i));
                i = end;
            }
        }
        return list;
    }

    void OnInsertMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = InsertButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var group in _set.Fields.GroupBy(f => f.Group))
        {
            var g = new System.Windows.Controls.MenuItem { Header = group.Key };
            foreach (var f in group)
            {
                var item = new System.Windows.Controls.MenuItem { Header = f.Label, ToolTip = f.Name };
                var name = f.Name;
                item.Click += (_, _) => InsertToken(TemplateEditing.Token(_set, name));
                g.Items.Add(item);
            }
            menu.Items.Add(g);
        }
        var custom = new System.Windows.Controls.MenuItem { Header = "Yeni değişken…" };
        custom.Click += (_, _) =>
        {
            var name = InputWindow.Ask("Değişken ekle", "Yeni değişkenin adı", "Ad", "", ok: "Ekle",
                validate: n => TemplateEditing.CheckName(_set, n));
            if (name is not null) InsertToken(TemplateEditing.Token(_set, name));
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(custom);
        menu.IsOpen = true;
    }

    void InsertToken(string token)
    {
        Editor.Focus();
        Editor.Selection.Text = token;
        Editor.CaretPosition = Editor.Selection.End;
        Highlight();
    }

    // ------------------------------------------------------------------ context menu

    void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        Menu.Items.Clear();
        Menu.Items.Add(MenuItem("Değişken yap…", () => OnMakeVariable(this, new RoutedEventArgs()), SelectionProblem() is null));
        if (TokenAtCaret() is { } token)
        {
            Menu.Items.Add(new Separator());
            Menu.Items.Add(MenuItem($"“{token.Name}” değişkenini yeniden adlandır… (bütün sette)", () => RenameVariable(token.Name)));
            Menu.Items.Add(MenuItem("Bu değişkeni metne çevir…", () => TokenToText(token)));
        }
        Menu.Items.Add(new Separator());
        Menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Kes", Command = ApplicationCommands.Cut });
        Menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Kopyala", Command = ApplicationCommands.Copy });
        Menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Yapıştır", Command = ApplicationCommands.Paste });
    }

    static System.Windows.Controls.MenuItem MenuItem(string header, Action action, bool enabled = true)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    sealed record TokenRef(WpfParagraph Paragraph, int Start, int Length, string Name);

    TokenRef? TokenAtCaret()
    {
        var pos = Editor.Selection.IsEmpty ? Editor.CaretPosition : Editor.Selection.Start;
        if (pos.Paragraph is not { } p) return null;
        var map = ParagraphText.Of(p);
        int offset = map.OffsetOf(pos);
        var m = _set.Matcher.Find(map.Text).FirstOrDefault(v => v.Start <= offset && offset <= v.Start + v.DelimitedLength);
        return m is null ? null : new TokenRef(p, m.Start, m.DelimitedLength, m.Ref.Name);
    }

    async void RenameVariable(string name)
    {
        if (!await ConfirmLeave()) return;
        var newName = InputWindow.Ask("Değişkeni yeniden adlandır", "Yeni değişken adı", "Ad", name,
            "Bütün belgelerdeki, dosya adlarındaki ve set ayarlarındaki kullanımlar birlikte değişir.", "Yeniden adlandır",
            n => TemplateEditing.CheckName(_set, n));
        if (newName is null) return;
        try
        {
            _saves.Push(TemplateEditing.RenameVariable(TemplateSet.Open(_folder), name, newName));
            Reload(_doc?.Entry.Template);
            Changed();
            Status.Text = $"✓ {name} → {newName}";
        }
        catch (EvrakciException ex) { await Dialogs.Error(ex.Message, "Yeniden adlandırılamadı"); }
    }

    void TokenToText(TokenRef token)
    {
        var text = InputWindow.Ask("Metne çevir", "Değişkenin yerine yazılacak metin", "Metin", "",
            "Bu yerde değişken kaldırılır, yazdığınız metin sabit kalır.", "Metne çevir");
        if (text is null) return;
        var map = ParagraphText.Of(token.Paragraph);
        new TextRange(map.PositionAt(token.Start), map.PositionAt(token.Start + token.Length)).Text = text;
        Highlight();
    }

    // ------------------------------------------------------------------ saving

    async void OnSave(object sender, RoutedEventArgs e) => await Save();

    async Task<bool> Save(bool reload = true, bool notify = true)
    {
        if (_doc?.Template is null) return false;
        if (!_dirty) return true;
        try
        {
            var paragraphs = EditorDocument.Collect(Editor.Document);
            var bytes = _doc.Template.Rewrite(paragraphs, _shown);
            var before = File.ReadAllBytes(_doc.Path);
            TemplateEditing.WriteFile(_doc.Path, bytes);
            _saves.Push([new FileChange(_doc.Path, before)]);
            SetDirty(false);
            if (reload) Reload(_doc.Entry.Template);
            if (notify) Changed();
            Status.Text = _case is null
                ? $"✓ Kaydedildi — {_doc.Entry.Template}"
                : $"✓ Bu dosyaya kaydedildi — {_doc.Entry.Template} (şablonun kendisi değişmedi)";
            return true;
        }
        catch (EvrakciException ex)
        {
            await Dialogs.Error(ex.Message, "Kaydedilemedi");
            return false;
        }
    }

    /// <summary>"Şablona kaydet": saves, then writes this file's template changes to the set itself.</summary>
    async void OnPublish(object sender, RoutedEventArgs e)
    {
        if (_case is null) return;
        if (!await Dialogs.Confirm(
                "Bu dosyada yaptığınız şablon değişiklikleri setin asıl şablonuna da yazılsın mı? Bundan sonra açılan bütün dosyalar bu hâli kullanır.",
                "Şablona kaydet", "Şablona kaydet"))
            return;
        if (!await Save()) return;
        await _case.PublishToTemplate();
        Status.Text = "✓ Şablona kaydedildi — yeni dosyalar da bu hâli kullanacak";
    }

    async void OnRevert(object sender, RoutedEventArgs e)
    {
        if (_saves.Count == 0) return;
        if (!await Dialogs.Confirm("Son kaydedilen değişiklik geri alınsın mı?", "Son kaydı geri al", "Geri al")) return;
        try
        {
            TemplateEditing.Restore(_saves.Pop());
            _dirty = false;
            Reload(_doc?.Entry.Template);
            Changed();
            Status.Text = "Son kayıt geri alındı.";
        }
        catch (EvrakciException ex) { await Dialogs.Error(ex.Message, "Geri alınamadı"); }
    }

    async void OnOpenInWord(object sender, RoutedEventArgs e)
    {
        if (_doc is null || !await ConfirmLeave()) return;
        try { TempFiles.OpenWithDefaultProgram(_doc.Path); }
        catch (System.ComponentModel.Win32Exception) { await Dialogs.Error("Bu dosya türünü açacak bir program bulunamadı."); }
    }

    void OnClose(object sender, RoutedEventArgs e) => Close();

    static void TryRun(Action a)
    {
        try { a(); }
        catch (EvrakciException) { }
    }
}

/// <summary>Builds the editable document from a template and reads it back.</summary>
static class EditorDocument
{
    public static FlowDocument Build(PreviewDocument doc, IReadOnlyDictionary<int, SourceParagraph> source, Brush chip, out HashSet<int> shown)
    {
        var ids = new HashSet<int>();
        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Times New Roman"),
            FontSize = 15,
            PagePadding = new Thickness(56, 40, 56, 56),
            TextAlignment = TextAlignment.Left,
        };
        foreach (var section in doc.Sections)
        {
            if (section.Title is not null)
                flow.Blocks.Add(new BlockUIContainer(new System.Windows.Controls.TextBlock
                {
                    Text = section.Title.ToUpperInvariant(), FontFamily = (FontFamily)Application.Current.Resources["BrandSans"], FontSize = 11,
                    FontWeight = FontWeights.SemiBold, Foreground = Brushes.Gray, Margin = new Thickness(0, 18, 0, 4),
                }));
            foreach (var b in section.Blocks) flow.Blocks.Add(Block(b, source, chip, ids));
        }
        if (flow.Blocks.Count == 0) flow.Blocks.Add(new Paragraph());
        shown = ids;
        return flow;
    }

    static Block Block(PreviewBlock block, IReadOnlyDictionary<int, SourceParagraph> source, Brush chip, HashSet<int> ids)
    {
        if (block is PreviewTable t)
        {
            var table = new WpfTable { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 8) };
            int cols = t.Rows.Count == 0 ? 1 : t.Rows.Max(r => r.Count);
            for (int i = 0; i < cols; i++) table.Columns.Add(new TableColumn());
            var group = new TableRowGroup();
            foreach (var row in t.Rows)
            {
                var tr = new TableRow();
                foreach (var cell in row)
                {
                    var tc = new TableCell { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0.5), Padding = new Thickness(6, 3, 6, 3) };
                    foreach (var b in cell) tc.Blocks.Add(Block(b, source, chip, ids));
                    if (tc.Blocks.Count == 0) tc.Blocks.Add(new Paragraph());
                    tr.Cells.Add(tc);
                }
                group.Rows.Add(tr);
            }
            table.RowGroups.Add(group);
            return table;
        }
        var p = (PreviewParagraph)block;
        var para = new Paragraph
        {
            Tag = p.Id,
            Margin = new Thickness(0, 0, 0, 4),
            TextAlignment = p.Alignment switch
            {
                PreviewAlignment.Center => TextAlignment.Center,
                PreviewAlignment.Right => TextAlignment.Right,
                PreviewAlignment.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            },
        };
        ids.Add(p.Id);
        var text = source.TryGetValue(p.Id, out var sp) ? sp.Text : "";
        foreach (var s in p.Segments)
        {
            string piece = s.Kind switch
            {
                SegmentKind.Literal => s.Text,
                // Variables are shown as written in the template so they can be edited like text.
                _ when s.SourceStart >= 0 && s.SourceStart + s.SourceLength <= text.Length => text.Substring(s.SourceStart, s.SourceLength),
                _ => s.Text,
            };
            var parts = piece.TrimEnd('\n').Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) para.Inlines.Add(new LineBreak());
                if (parts[i].Length == 0) continue;
                var run = new Run(parts[i]);
                if (s.Bold) run.FontWeight = FontWeights.Bold;
                if (s.Italic) run.FontStyle = FontStyles.Italic;
                if (s.Underline) run.TextDecorations = TextDecorations.Underline;
                if (s.Kind != SegmentKind.Literal) run.Background = chip;
                para.Inlines.Add(run);
            }
        }
        if (!p.InTextBox) return para;
        return new Section(para) { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0), Margin = new Thickness(24, 2, 0, 2) };
    }

    /// <summary>All paragraphs in document order, inside tables and sections too.</summary>
    public static IEnumerable<Paragraph> Paragraphs(FlowDocument doc) => Walk(doc.Blocks);

    static IEnumerable<Paragraph> Walk(IEnumerable<Block> blocks)
    {
        foreach (var b in blocks)
        {
            switch (b)
            {
                case Paragraph p: yield return p; break;
                case Section s: foreach (var x in Walk(s.Blocks)) yield return x; break;
                case List l: foreach (var x in Walk(l.ListItems.SelectMany(i => i.Blocks))) yield return x; break;
                case WpfTable t:
                    foreach (var x in Walk(t.RowGroups.SelectMany(g => g.Rows).SelectMany(r => r.Cells).SelectMany(c => c.Blocks)))
                        yield return x;
                    break;
            }
        }
    }

    /// <summary>The editor content as paragraphs for <see cref="ITemplateDocument.Rewrite"/>.</summary>
    public static List<EditParagraph> Collect(FlowDocument doc)
    {
        var seen = new HashSet<int>();
        var list = new List<EditParagraph>();
        foreach (var p in Paragraphs(doc))
        {
            // Splitting a paragraph with Enter copies its id; only the first copy is the original.
            int? id = p.Tag is int i && seen.Add(i) ? i : null;
            var runs = new List<EditRun>();
            Inlines(p.Inlines, runs, false);
            var align = p.TextAlignment switch
            {
                TextAlignment.Center => PreviewAlignment.Center,
                TextAlignment.Right => PreviewAlignment.Right,
                TextAlignment.Justify => PreviewAlignment.Justify,
                _ => PreviewAlignment.Left,
            };
            list.Add(new EditParagraph(id, runs, align));
        }
        return list;
    }

    static void Inlines(InlineCollection inlines, List<EditRun> runs, bool underline)
    {
        foreach (var inline in inlines)
        {
            bool u = underline || (inline.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Underline) ?? false);
            switch (inline)
            {
                case Run r:
                    runs.Add(new EditRun(r.Text.Replace("\r", ""), r.FontWeight >= FontWeights.SemiBold, r.FontStyle == FontStyles.Italic, u));
                    break;
                case LineBreak:
                    runs.Add(new EditRun("\n"));
                    break;
                case Span s:
                    Inlines(s.Inlines, runs, u);
                    break;
            }
        }
    }
}

/// <summary>The plain text of one editor paragraph with a way back from character offsets to positions.</summary>
sealed class ParagraphText
{
    public sealed record Piece(Run? Run, int Start, int Length);

    public string Text { get; private set; } = "";
    public List<Piece> Pieces { get; } = new();
    readonly Paragraph _p;

    ParagraphText(Paragraph p) => _p = p;

    public static ParagraphText Of(Paragraph p)
    {
        var sb = new System.Text.StringBuilder();
        var map = new ParagraphText(p);
        void Walk(InlineCollection inlines)
        {
            foreach (var i in inlines)
            {
                if (i is Run r) { map.Pieces.Add(new Piece(r, sb.Length, r.Text.Length)); sb.Append(r.Text); }
                else if (i is LineBreak) { map.Pieces.Add(new Piece(null, sb.Length, 1)); sb.Append('\n'); }
                else if (i is Span s) Walk(s.Inlines);
            }
        }
        Walk(p.Inlines);
        map.Text = sb.ToString();
        return map;
    }

    public TextPointer PositionAt(int offset)
    {
        foreach (var piece in Pieces)
            if (piece.Run is { } r && offset >= piece.Start && offset <= piece.Start + piece.Length)
                return r.ContentStart.GetPositionAtOffset(offset - piece.Start) ?? r.ContentEnd;
        return offset <= 0 ? _p.ContentStart : _p.ContentEnd;
    }

    public int OffsetOf(TextPointer pointer)
    {
        foreach (var piece in Pieces)
        {
            if (piece.Run is not { } r) continue;
            if (r.ContentStart.CompareTo(pointer) <= 0 && pointer.CompareTo(r.ContentEnd) <= 0)
                return piece.Start + r.ContentStart.GetOffsetToPosition(pointer);
        }
        // Between inlines: count the text that comes before the pointer.
        int total = 0;
        foreach (var piece in Pieces)
        {
            var start = piece.Run?.ContentStart;
            if (start is not null && start.CompareTo(pointer) >= 0) break;
            total = piece.Start + piece.Length;
        }
        return total;
    }
}
