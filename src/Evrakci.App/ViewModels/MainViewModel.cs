using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Evrakci.App.Services;
using Evrakci.App.Views;
using Evrakci.Core;
using Evrakci.Core.Cases;
using Evrakci.Core.Output;
using Evrakci.Core.Sets;
using Evrakci.Core.Templates;
using Evrakci.Core.Text;

namespace Evrakci.App.ViewModels;

public sealed record RecentSetItem(string Path, string Name, bool Exists);
public sealed record IssueItem(IssueSeverity Severity, string Message, string? Document)
{
    public string SeverityLabel => Severity switch { IssueSeverity.Error => "Hata", IssueSeverity.Warning => "Uyarı", _ => "Bilgi" };
    public bool IsError => Severity == IssueSeverity.Error;
    public bool IsWarning => Severity == IssueSeverity.Warning;
}
public sealed record DocumentItem(string Name, string Format, int Variables, string? Error, bool Optional, string? Note,
    bool IsFirst, bool IsLast)
{
    public bool IsDefault => !Optional;
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Name);
    public bool CanMoveUp => !IsFirst;
    public bool CanMoveDown => !IsLast;
}

/// <summary>A set in the library list on the first step.</summary>
public sealed class LibraryItem(LibraryEntry entry) : ObservableObject
{
    bool _current;
    public LibraryEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Folder => Entry.Folder;
    public string Badge => Entry.BuiltIn ? "Hazır" : Entry.Category == MainViewModel.OtherFolders ? "Klasör" : "Benim";
    public bool IsBuiltIn => Entry.BuiltIn;
    /// <summary>A set folder opened from disk, listed only because it was used recently.</summary>
    public bool IsExternal => Entry.Category == MainViewModel.OtherFolders;
    public string Details => $"{Entry.Documents} belge";
    public bool IsCurrent { get => _current; set => Set(ref _current, value); }
}

public sealed record LibraryGroup(string Name, List<LibraryItem> Items);

/// <summary>Whether a document of the set is produced for the current case.</summary>
public sealed class DocumentChoice(string template, string? note, bool selected, Action changed) : ObservableObject
{
    bool _selected = selected;
    public string Template { get; } = template;
    /// <summary>File name without extension and leading order number ("04 Anlaşma Belgesi" → "Anlaşma Belgesi").</summary>
    public string Name => System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(Template), @"^\d+\s+", "");
    public string? Note { get; } = note;
    public bool IsSelected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) changed(); }
    }
}
public sealed record ErrorItem(string Field, string Message);
public sealed record OutputItem(string FileName, string Path, bool Exists, bool Written = false)
{
    public bool IsUdf => Path.EndsWith(".udf", StringComparison.OrdinalIgnoreCase);
    public string FormatLabel => IsUdf ? "UDF" : "Word";
    public bool WillOverwrite => Exists && !Written;
}

public sealed class StepItem(int index, int number, string title) : ObservableObject
{
    bool _current, _done;
    public int Index { get; } = index;
    public int Number { get; } = number;
    public string Title { get; } = title;
    public bool IsCurrent { get => _current; set => Set(ref _current, value); }
    public bool IsDone { get => _done; set => Set(ref _done, value); }
}

public sealed class MainViewModel : ObservableObject
{
    public const int StepDocuments = 1, StepForm = 2, StepPreview = 3, StepGenerate = 4;

    public enum AppScreen { Home, Templates, Case }

    public const string OtherFolders = "Diğer klasörler";

    readonly AppSettings _settings = AppSettings.Load();
    readonly RememberedValues _remembered = RememberedValues.Load();
    readonly TemplateLibrary _library;
    readonly SuffixOverrides _overrides = new();
    readonly List<FieldViewModel> _fields = new();
    TemplateSet? _set;
    Resolution? _resolution;
    int _step;
    bool _dirty;
    readonly CaseStore _store;
    readonly System.Windows.Threading.DispatcherTimer _autosave;
    AppScreen _screen = AppScreen.Home;
    bool _caseOpen, _titleIsCustom, _pickingType;
    string? _casePath, _caseTitle, _saveState, _search;
    DateTimeOffset? _generatedAt;
    string? _status;
    SetDocument? _selectedPreview;
    PreviewDocument? _preview;
    string? _outputFolder;
    string? _planError;
    string? _generateMessage;
    bool _generated;
    TemplateEditorWindow? _editor;
    DateTime _setStamp;

    // Template changes made for the open file only ("Bu dosyaya kaydet"). While the file is open its
    // set is read from a working copy (_work) of the set folder (_origin) with these files written over it.
    readonly Dictionary<string, byte[]> _caseFiles = new(StringComparer.OrdinalIgnoreCase);
    string? _work, _origin;

    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public MainViewModel()
    {
        Steps = [new(StepDocuments, 1, "Hazırlanacak tutanaklar"), new(StepForm, 2, "Tutanak bilgileri"),
                 new(StepPreview, 3, "Önizleme"), new(StepGenerate, 4, "Belgeleri üret")];
        _library = new TemplateLibrary(_settings.LibraryRoot);
        _store = new CaseStore(Path.Combine(Path.GetDirectoryName(_library.Root.TrimEnd('\\', '/'))!, "Dosyalar"));
        _autosave = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _autosave.Tick += (_, _) => { _autosave.Stop(); SaveNow(); };
        _step = StepDocuments;
        BuiltInTemplates.EnsureExtracted(_library);
        RefreshRecent();
        RefreshFiles();
        UpdateSteps();

        // While the template editor is open the app stays on the current file / set: everything that
        // would leave it is disabled (scrolling, typing and moving between steps still work).
        OpenSetFolderCommand = new AsyncCommand(OpenSetFolderAsync, () => NotEditing);
        OpenRecentCommand = new AsyncCommand(p => p is RecentSetItem r ? OpenSetAsync(r.Path) : Task.CompletedTask, _ => NotEditing);
        RemoveRecentCommand = new RelayCommand(p =>
        {
            if (p is not RecentSetItem r) return;
            _settings.RecentSets.RemoveAll(x => string.Equals(x, r.Path, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            RefreshRecent();
        });
        EditSettingsCommand = new AsyncCommand(EditSettingsAsync, () => _set is not null && NotEditing);
        ReloadSetCommand = new AsyncCommand(() => ReloadSetAsync(), () => _set is not null);
        OpenSetInExplorerCommand = new RelayCommand(() => OpenFolder(SetHome!), () => _set is not null);
        NextCommand = new AsyncCommand(NextAsync, () => CanGoNext);
        BackCommand = new AsyncCommand(async () =>
        {
            if (_step > StepDocuments) GoTo(_step - 1);
            else await GoHomeAsync();
        }, () => _step > StepDocuments || NotEditing);
        GoToStepCommand = new AsyncCommand(p => p is StepItem s ? GoToStepAsync(s.Index) : Task.CompletedTask);
        HomeCommand = new AsyncCommand(GoHomeAsync, () => NotEditing);
        TemplatesCommand = new AsyncCommand(GoTemplatesAsync, () => NotEditing);
        NewFileCommand = new AsyncCommand(async () => { await GoHomeAsync(); PickingType = true; }, () => NotEditing);
        CancelPickCommand = new RelayCommand(() => PickingType = false);
        PickTypeCommand = new AsyncCommand(p => p is LibraryItem i ? StartNewFileAsync(i.Folder) : Task.CompletedTask, _ => NotEditing);
        NewFileFromSetCommand = new AsyncCommand(() => _set is null ? Task.CompletedTask : StartNewFileAsync(_set.Folder),
            () => _set is not null && NotEditing);
        OpenFileCommand = new AsyncCommand(p => p is StoredCase c ? OpenStoredAsync(c.Path) : Task.CompletedTask, _ => NotEditing);
        DeleteFileCommand = new AsyncCommand(p => p is StoredCase c ? DeleteFileAsync(c) : Task.CompletedTask, _ => NotEditing);
        RenameFileCommand = new AsyncCommand(RenameFileAsync, () => _caseOpen);
        ExportFileCommand = new AsyncCommand(ExportFileAsync, () => _caseOpen);
        ImportFileCommand = new AsyncCommand(() => ImportAsync(null), () => NotEditing);
        DeleteCurrentFileCommand = new AsyncCommand(async () =>
        {
            if (_casePath is null) { await GoHomeAsync(); return; }
            var path = _casePath;
            await DeleteFileAsync(new StoredCase(path, CaseTitle ?? "", null, null, DateTimeOffset.Now, "", false));
        }, () => _caseOpen && NotEditing);
        OpenInWordCommand = new AsyncCommand(OpenInWordAsync, () => _selectedPreview is not null);
        ChooseOutputFolderCommand = new RelayCommand(ChooseOutputFolder);
        GenerateCommand = new AsyncCommand(GenerateAsync, () => CanGenerate);
        OpenOutputFolderCommand = new RelayCommand(() =>
        {
            var dir = Outputs.Count > 0 ? Path.GetDirectoryName(Outputs[0].Path) : _outputFolder;
            if (dir is not null && Directory.Exists(dir)) OpenFolder(dir);
        });
        OpenOutputCommand = new RelayCommand(p =>
        {
            if (p is OutputItem o && File.Exists(o.Path)) TempFiles.OpenWithDefaultProgram(o.Path);
        });
        GoToFormCommand = new RelayCommand(() => GoTo(StepForm));
        GoToDocumentsCommand = new RelayCommand(() => GoTo(StepDocuments));
        EditChoiceTemplateCommand = new AsyncCommand(p => p is DocumentChoice c ? EditTemplateAsync(c.Template) : Task.CompletedTask);

        OpenLibraryItemCommand = new AsyncCommand(p => p is LibraryItem i ? OpenSetAsync(i.Folder) : Task.CompletedTask, _ => NotEditing);
        ForgetFolderCommand = new RelayCommand(p =>
        {
            if (p is not LibraryItem { IsExternal: true } item) return;
            _settings.RecentSets.RemoveAll(x => string.Equals(x, item.Folder, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            if (_set is not null && string.Equals(_set.Folder, item.Folder, StringComparison.OrdinalIgnoreCase)) CloseSet();
            else RefreshRecent();
            Status = $"“{item.Name}” listeden kaldırıldı; klasör silinmedi.";
        }, p => NotEditing && !_caseOpen);
        OpenLibraryFolderCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(_library.Root);
            OpenFolder(_library.Root);
        });
        NewSetCommand = new AsyncCommand(NewSetAsync, () => NotEditing);
        CopySetCommand = new AsyncCommand(CopySetAsync, () => _set is not null && NotEditing);
        RenameSetCommand = new AsyncCommand(RenameSetAsync, () => IsOwnSet && NotEditing);
        DeleteSetCommand = new AsyncCommand(DeleteSetAsync, () => IsOwnSet && NotEditing);
        RestoreSetCommand = new AsyncCommand(RestoreSetAsync, () => IsBuiltInSet && NotEditing);
        AddDocumentCommand = new AsyncCommand(AddDocumentAsync, () => _set is not null && NotEditing);
        NewDocumentCommand = new AsyncCommand(NewDocumentAsync, () => _set is not null && NotEditing);
        EditDocumentCommand = new AsyncCommand(p => EditTemplateAsync((p as DocumentItem)?.Name), _ => _set is not null);
        EditPreviewTemplateCommand = new AsyncCommand(() => EditTemplateAsync(_selectedPreview?.Entry.Template), () => _selectedPreview is not null);
        OpenTemplateInWordCommand = new AsyncCommand(p => p is DocumentItem d ? OpenTemplateInWordAsync(d.Name) : Task.CompletedTask);
        RenameDocumentCommand = new AsyncCommand(p => p is DocumentItem d ? RenameDocumentAsync(d.Name) : Task.CompletedTask, _ => NotEditing);
        RemoveDocumentCommand = new AsyncCommand(p => p is DocumentItem d ? RemoveDocumentAsync(d.Name) : Task.CompletedTask, _ => NotEditing);
        MoveDocumentUpCommand = new AsyncCommand(p => p is DocumentItem d ? ChangeSetAsync(s => TemplateLibrary.MoveDocument(s, d.Name, -1)) : Task.CompletedTask, _ => NotEditing);
        MoveDocumentDownCommand = new AsyncCommand(p => p is DocumentItem d ? ChangeSetAsync(s => TemplateLibrary.MoveDocument(s, d.Name, 1)) : Task.CompletedTask, _ => NotEditing);
        ToggleOptionalCommand = new AsyncCommand(p => p is DocumentItem d ? ChangeSetAsync(s =>
        {
            s.Definition.Documents ??= s.Documents.Select(x => x.Entry).ToList();
            foreach (var e in s.Definition.Documents.Where(e => string.Equals(e.Template, d.Name, StringComparison.OrdinalIgnoreCase)))
                e.Optional = !e.Optional;
            s.Definition.Save(s.SetFilePath);
        }) : Task.CompletedTask, _ => NotEditing);
        ForgetAllCommand = new AsyncCommand(ForgetAllAsync, () => _remembered.Count > 0);
        SetThemeCommand = new RelayCommand(p => Theme = p as string ?? ThemeService.System);
        OpenSourceCommand = new RelayCommand(() =>
            Process.Start(new ProcessStartInfo(Distribution.SourceUrl) { UseShellExecute = true }));
        InstallUpdateCommand = new AsyncCommand(async () =>
        {
            if (_editor is not null) { _editor.Activate(); return; }
            await CloseCaseAsync();
            _updater.ApplyAndRestart();
        });
        _updater.Ready += () => Raise(nameof(UpdateReady));
    }

    public RelayCommand SetThemeCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public string VersionText => $"Evrakçı {AppVersion} · AGPL-3.0";

    // ------------------------------------------------------------------ updates

    readonly UpdateService _updater = new();

    public AsyncCommand InstallUpdateCommand { get; }
    public bool UpdateReady => _updater.IsReady;

    /// <summary>After the window appears: asks once whether updates may be checked, then checks quietly if allowed.</summary>
    public async Task StartBackgroundWorkAsync()
    {
        if (string.IsNullOrWhiteSpace(Distribution.UpdateUrl)) return;
        if (!_settings.UpdateChoiceMade)
        {
            _settings.CheckForUpdates = await Dialogs.Confirm(
                "Evrakçı her açılışta evrakci.cahitarslan.com adresine yeni bir sürüm olup olmadığını sorabilir; varsa indirip kapanışta kurar. " +
                "İstekte yalnızca kurulu sürüm numarası gider; sizinle, dosyalarınızla ya da müvekkil bilgileriyle ilgili hiçbir şey gönderilmez.\n\n" +
                "Bu ayarı daha sonra sağ üstteki tema menüsünden (Güncellemeleri otomatik denetle) değiştirebilirsiniz.",
                "Otomatik güncelleme", "Güncellemeleri denetle", "Denetleme");
            _settings.UpdateChoiceMade = true;
            _settings.Save();
            Raise(nameof(CheckForUpdates));
        }
        if (_settings.CheckForUpdates) await _updater.CheckAsync();
    }

    /// <summary>Menu toggle "Güncellemeleri otomatik denetle". Off: the app never contacts the internet by itself.</summary>
    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value) return;
            _settings.CheckForUpdates = value;
            _settings.Save();
            Raise(nameof(CheckForUpdates));
            Status = value ? "Güncellemeler bir sonraki açılışta denetlenecek." : "Otomatik güncelleme denetimi kapatıldı; uygulama kendiliğinden internete bağlanmayacak.";
        }
    }

    /// <summary>"Sistem", "Açık" or "Koyu"; applied at once and remembered.</summary>
    public string Theme
    {
        get => ThemeService.Normalize(_settings.Theme);
        set
        {
            _settings.Theme = ThemeService.Normalize(value);
            _settings.Save();
            if (System.Windows.Application.Current?.MainWindow is { } w) ThemeService.Apply(_settings.Theme, w);
            foreach (var n in new[] { nameof(Theme), nameof(IsThemeSystem), nameof(IsThemeLight), nameof(IsThemeDark) }) Raise(n);
        }
    }

    public bool IsThemeSystem => Theme == ThemeService.System;
    public bool IsThemeLight => Theme == ThemeService.Light;
    public bool IsThemeDark => Theme == ThemeService.Dark;

    public AsyncCommand OpenLibraryItemCommand { get; }
    public RelayCommand ForgetFolderCommand { get; }
    public RelayCommand OpenLibraryFolderCommand { get; }
    public AsyncCommand NewSetCommand { get; }
    public AsyncCommand CopySetCommand { get; }
    public AsyncCommand RenameSetCommand { get; }
    public AsyncCommand DeleteSetCommand { get; }
    public AsyncCommand RestoreSetCommand { get; }
    public AsyncCommand AddDocumentCommand { get; }
    public AsyncCommand NewDocumentCommand { get; }
    public AsyncCommand EditDocumentCommand { get; }
    public AsyncCommand EditPreviewTemplateCommand { get; }
    public AsyncCommand OpenTemplateInWordCommand { get; }
    public AsyncCommand RenameDocumentCommand { get; }
    public AsyncCommand RemoveDocumentCommand { get; }
    public AsyncCommand MoveDocumentUpCommand { get; }
    public AsyncCommand MoveDocumentDownCommand { get; }
    public AsyncCommand ToggleOptionalCommand { get; }
    public AsyncCommand ForgetAllCommand { get; }

    // ------------------------------------------------------------------ commands

    public AsyncCommand OpenSetFolderCommand { get; }
    public AsyncCommand OpenRecentCommand { get; }
    public RelayCommand RemoveRecentCommand { get; }
    public AsyncCommand EditSettingsCommand { get; }
    public AsyncCommand ReloadSetCommand { get; }
    public RelayCommand OpenSetInExplorerCommand { get; }
    public AsyncCommand NextCommand { get; }
    public AsyncCommand BackCommand { get; }
    public AsyncCommand GoToStepCommand { get; }
    public AsyncCommand HomeCommand { get; }
    public AsyncCommand TemplatesCommand { get; }
    public AsyncCommand NewFileCommand { get; }
    public RelayCommand CancelPickCommand { get; }
    public AsyncCommand PickTypeCommand { get; }
    public AsyncCommand NewFileFromSetCommand { get; }
    public AsyncCommand OpenFileCommand { get; }
    public AsyncCommand DeleteFileCommand { get; }
    public AsyncCommand RenameFileCommand { get; }
    public AsyncCommand ExportFileCommand { get; }
    public AsyncCommand ImportFileCommand { get; }
    public AsyncCommand DeleteCurrentFileCommand { get; }
    public AsyncCommand OpenInWordCommand { get; }
    public RelayCommand ChooseOutputFolderCommand { get; }
    public AsyncCommand GenerateCommand { get; }
    public RelayCommand OpenOutputFolderCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand GoToFormCommand { get; }
    public RelayCommand GoToDocumentsCommand { get; }
    public AsyncCommand EditChoiceTemplateCommand { get; }

    /// <summary>The template editor is open (it is not modal; the main window stays usable).</summary>
    public bool IsEditing => _editor is not null;
    public bool NotEditing => _editor is null;

    /// <summary>The set's own folder, also while the open file uses a working copy of it.</summary>
    string? SetHome => _origin ?? _set?.Folder;

    // ------------------------------------------------------------------ general state

    public List<StepItem> Steps { get; }

    public int Step
    {
        get => _step;
        private set
        {
            if (Set(ref _step, value))
            {
                UpdateSteps();
                Raise(nameof(NextText));
                Raise(nameof(IsLastStep));
                Raise(nameof(ShowNext));
            }
        }
    }

    public bool IsLastStep => _step == StepGenerate;
    public bool ShowNext => _screen == AppScreen.Case && _step != StepGenerate;
    public string NextText => _step switch
    {
        StepDocuments => "Bilgileri gir", StepForm => "Önizle", StepPreview => "Üretime geç", _ => "İleri",
    };
    public bool CanGoNext => _step != StepGenerate && _caseOpen;

    public string? Status { get => _status; private set => Set(ref _status, value); }

    public string Title => _screen == AppScreen.Case && CaseTitle is { } t ? $"Evrakçı — {t}" : "Evrakçı";

    void UpdateSteps()
    {
        foreach (var s in Steps)
        {
            s.IsCurrent = s.Index == _step;
            s.IsDone = s.Index < _step;
        }
    }

    // ------------------------------------------------------------------ step 0: set

    public ObservableCollection<RecentSetItem> RecentSets { get; } = new();
    public ObservableCollection<IssueItem> Issues { get; } = new();
    public ObservableCollection<DocumentItem> SetDocuments { get; } = new();
    public bool HasSet => _set is not null;
    public string? SetName => _set?.Name;
    public string? SetDescription => _set?.Definition.Description;
    public string? SetFolder => _set?.Folder;
    public int FieldCount => _set?.Fields.Count ?? 0;
    public bool SetHasNoFile => _set is { HasSetFile: false };
    public bool HasIssues => Issues.Count > 0;

    void RefreshRecent()
    {
        RecentSets.Clear();
        foreach (var p in _settings.RecentSets)
            RecentSets.Add(new RecentSetItem(p, Path.GetFileName(p.TrimEnd('\\', '/')), Directory.Exists(p)));
        Raise(nameof(HasRecentSets));
        RefreshLibrary();
    }

    // ------------------------------------------------------------------ library

    public ObservableCollection<LibraryGroup> LibraryGroups { get; } = new();
    public string LibraryRoot => _library.Root;
    public bool IsBuiltInSet => _set is not null && _library.IsBuiltIn(_set.Folder);
    public bool IsOwnSet => _set is not null && _library.IsOwn(_set.Folder);
    public bool IsExternalSet => _set is not null && !IsBuiltInSet && !IsOwnSet;
    public string? SetCategory => _set is null ? null : string.IsNullOrWhiteSpace(_set.Definition.Category) ? null : _set.Definition.Category;

    void RefreshLibrary()
    {
        List<LibraryEntry> entries;
        try { entries = _library.List(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { entries = new(); }
        foreach (var p in _settings.RecentSets.Where(Directory.Exists))
            if (!_library.IsBuiltIn(p) && !_library.IsOwn(p) && !entries.Any(e => string.Equals(e.Folder, p, StringComparison.OrdinalIgnoreCase)))
                entries.Add(new LibraryEntry(p, Path.GetFileName(p.TrimEnd('\\', '/')), OtherFolders, null, false,
                    Directory.EnumerateFiles(p).Count(TemplateLoader.IsTemplateFile)));
        LibraryGroups.Clear();
        foreach (var g in entries.GroupBy(e => e.Category))
            LibraryGroups.Add(new LibraryGroup(g.Key, g.Select(e => new LibraryItem(e)
            {
                IsCurrent = _set is not null && string.Equals(e.Folder, _set.Folder, StringComparison.OrdinalIgnoreCase),
            }).ToList()));
    }

    IEnumerable<string> Categories() =>
        TemplateLibrary.CategoryOrder.Concat(LibraryGroups.Select(g => g.Name)).Where(c => c != OtherFolders).Distinct();

    async Task NewSetAsync()
    {
        var answer = InputWindow.AskWithChoice("Yeni set", "Boş bir şablon seti oluştur", "Set adı", "",
            Categories(), SetCategory ?? TemplateLibrary.OtherCategory,
            "Set “Setlerim” altına oluşturulur. Ardından belge ekleyip metinleri değişkene çevirebilirsiniz.", "Oluştur",
            n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın." : null);
        if (answer is null) return;
        await Try(async () => await OpenSetAsync(_library.Create(answer.Value.Text, answer.Value.Choice)));
    }

    async Task CopySetAsync()
    {
        if (_set is null) return;
        var answer = InputWindow.AskWithChoice("Kopyasını oluştur", "Bu setin kopyasını oluştur", "Yeni setin adı", $"{_set.Name} (kopya)",
            Categories(), SetCategory ?? TemplateLibrary.OtherCategory,
            "Bütün belgeler ve ayarlar “Setlerim” altına kopyalanır. Kopya üzerinde dilediğiniz değişikliği yapabilirsiniz; asıl set değişmez.",
            "Kopyala", n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın." : null);
        if (answer is null) return;
        await Try(async () =>
        {
            var folder = _library.Copy(_set.Folder, answer.Value.Text, answer.Value.Choice);
            await OpenSetAsync(folder, keepInputs: true);
            Status = "Kopya oluşturuldu; artık bu set üzerinde çalışıyorsunuz.";
        });
    }

    async Task RenameSetAsync()
    {
        if (_set is null) return;
        var answer = InputWindow.AskWithChoice("Yeniden adlandır", "Setin adı ve türü", "Set adı", _set.Name,
            Categories(), SetCategory ?? TemplateLibrary.OtherCategory, null, "Kaydet",
            n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın." : null);
        if (answer is null) return;
        await Try(async () =>
        {
            var old = _set.Folder;
            var folder = _library.Rename(old, answer.Value.Text, answer.Value.Choice);
            _settings.RecentSets.RemoveAll(x => string.Equals(x, old, StringComparison.OrdinalIgnoreCase));
            await OpenSetAsync(folder, keepInputs: true);
        });
    }

    async Task DeleteSetAsync()
    {
        if (_set is null || !IsOwnSet) return;
        if (!await Dialogs.Confirm($"“{_set.Name}” seti ve içindeki belgeler Geri Dönüşüm Kutusu'na taşınsın mı?", "Seti sil", "Sil", danger: true))
            return;
        await Try(() =>
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(_set.Folder, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            _settings.RecentSets.RemoveAll(x => string.Equals(x, _set.Folder, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            CloseSet();
            return Task.CompletedTask;
        });
    }

    async Task RestoreSetAsync()
    {
        if (_set is null || !IsBuiltInSet) return;
        if (!await Dialogs.Confirm("Bu hazır şablondaki bütün değişiklikler silinip set ilk hâline döndürülsün mü? Eklediğiniz belgeler de kaldırılır.",
                "Orijinaline döndür", "Orijinaline döndür", danger: true))
            return;
        await Try(async () =>
        {
            BuiltInTemplates.Restore(_set.Folder);
            await OpenSetAsync(_set.Folder, keepInputs: true);
            Status = "Set orijinal hâline döndürüldü.";
        });
    }

    void CloseSet()
    {
        _set = null;
        _resolution = null;
        _fields.Clear();
        Groups.Clear();
        DocumentChoices.Clear();
        Issues.Clear();
        SetDocuments.Clear();
        _dirty = false;
        RaiseSetProperties();
        RefreshRecent();
        Raise(nameof(Title));
    }

    // ------------------------------------------------------------------ documents of the set

    async Task AddDocumentAsync()
    {
        if (_set is null) return;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sete eklenecek belgeleri seçin",
            Filter = "Word ya da UYAP belgesi (*.docx;*.udf)|*.docx;*.udf",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != true) return;
        await ChangeSetAsync(s =>
        {
            foreach (var f in dlg.FileNames) TemplateLibrary.AddDocument(s, f);
        });
        Status = dlg.FileNames.Length == 1 ? "Belge eklendi. Metinleri değişkene çevirmek için “Düzenle”ye tıklayın." : $"{dlg.FileNames.Length} belge eklendi.";
    }

    async Task NewDocumentAsync()
    {
        if (_set is null) return;
        var folder = _set.Folder;
        var name = InputWindow.Ask("Yeni belge", "Boş bir Word belgesi ekle", "Belge adı", "",
            "Belge Word'de açılır; metni yazıp kaydedin, sonra “Düzenle” ile değişkenleri belirleyin.", "Oluştur",
            n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın."
                : File.Exists(Path.Combine(folder, Tr.SanitizeFileName(n.Trim()) + ".docx")) ? "Bu adda bir belge zaten var." : null);
        if (name is null) return;
        var path = Path.Combine(folder, Tr.SanitizeFileName(name.Trim()) + ".docx");
        await ChangeSetAsync(s =>
        {
            File.WriteAllBytes(path, new Core.Templates.Docx.DocxBuilder().Title(Tr.Upper(name.Trim())).Empty().Para("").Build());
            TemplateLibrary.AddDocument(s, path);
        });
        await OpenTemplateInWordAsync(Path.GetFileName(path));
    }

    /// <summary>
    /// Opens the template editor beside the main window. From a file it edits the file's working copy:
    /// "Bu dosyaya kaydet" keeps the change in the file, "Şablona kaydet" writes it to the set too.
    /// </summary>
    async Task EditTemplateAsync(string? template)
    {
        if (_set is null) return;
        if (_editor is not null)
        {
            _editor.Show(template);
            return;
        }
        TemplateEditorWindow.CaseEditing? forCase = null;
        string folder = _set.Folder;
        if (_caseOpen)
        {
            try { folder = EnsureWorkCopy(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Dialogs.Error("Şablonun bu dosyaya özel kopyası hazırlanamadı.");
                return;
            }
            if (!string.Equals(_set.Folder, folder, StringComparison.OrdinalIgnoreCase) && !await OpenSetAsync(folder, keepInputs: true)) return;
            forCase = new TemplateEditorWindow.CaseEditing(CaseTitle ?? "bu dosya", PublishCaseTemplatesAsync);
        }
        _editor = TemplateEditorWindow.Open(folder, template, OnTemplatesSaved, forCase);
        _editor.Closed += (_, _) =>
        {
            _editor = null;
            RaiseEditing();
        };
        RaiseEditing();
    }

    void RaiseEditing()
    {
        Raise(nameof(IsEditing));
        Raise(nameof(NotEditing));
        RequeryCommands();
    }

    /// <summary>The editor wrote templates: the form follows at once (new or removed variables).</summary>
    async void OnTemplatesSaved()
    {
        if (_set is null) return;
        if (_caseOpen && _work is not null)
        {
            CaptureCaseFiles();
            MarkDirty();
        }
        await OpenSetAsync(_set.Folder, keepInputs: true);
        if (_step == StepPreview) PreparePreview();
        else if (_step == StepGenerate) PrepareGenerate();
        Status = _caseOpen && _caseFiles.Count > 0
            ? "Şablon değişikliği bu dosyaya kaydedildi; form güncellendi."
            : "Şablon değişiklikleri kaydedildi; form güncellendi.";
    }

    /// <summary>Templates of the open file that differ from its set ("Bu dosyaya kaydet").</summary>
    public bool HasCaseTemplates => _caseFiles.Count > 0;

    public string? CaseTemplatesText => _caseFiles.Count == 0 ? null
        : $"Bu dosyada şablondan farklı {_caseFiles.Count} dosya var (yalnızca bu dosyaya kaydedilmiş değişiklikler).";

    /// <summary>A working copy of the set for the open file, with the file's own template changes written over it.</summary>
    string EnsureWorkCopy()
    {
        if (_work is not null) return _work;
        var origin = _set!.Folder;
        // Same folder name as the set: a set without a name in set.json is named after its folder.
        var work = Path.Combine(TempFiles.NewFolder(), Path.GetFileName(origin.TrimEnd('\\', '/')));
        Directory.CreateDirectory(work);
        foreach (var f in Directory.EnumerateFiles(origin))
            File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
        foreach (var (name, bytes) in _caseFiles)
            File.WriteAllBytes(Path.Combine(work, Path.GetFileName(name)), bytes);
        _origin = origin;
        _work = work;
        return work;
    }

    /// <summary>Remembers every file of the working copy that differs from the set.</summary>
    void CaptureCaseFiles()
    {
        if (_work is null || _origin is null) return;
        _caseFiles.Clear();
        try
        {
            foreach (var f in Directory.EnumerateFiles(_work))
            {
                var name = Path.GetFileName(f);
                if (name.EndsWith(".evrakci-tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = File.ReadAllBytes(f);
                var original = Path.Combine(_origin, name);
                if (!File.Exists(original) || !bytes.AsSpan().SequenceEqual(File.ReadAllBytes(original))) _caseFiles[name] = bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Raise(nameof(HasCaseTemplates));
        Raise(nameof(CaseTemplatesText));
    }

    /// <summary>"Şablona kaydet": the file's template changes become the set's templates.</summary>
    async Task PublishCaseTemplatesAsync()
    {
        if (_work is null || _origin is null) return;
        CaptureCaseFiles();
        try
        {
            foreach (var (name, bytes) in _caseFiles)
                TemplateEditing.WriteFile(Path.Combine(_origin, name), bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EvrakciException)
        {
            await Dialogs.Error("Şablon dosyalarına yazılamadı. Dosya başka bir programda açık olabilir.");
            return;
        }
        CaptureCaseFiles();
        MarkDirty();
        Status = "Şablon güncellendi; bundan sonraki dosyalar da bu hâli kullanacak.";
    }

    /// <summary>Leaves the working copy: back to the set's own folder.</summary>
    void DropWorkCopy()
    {
        if (_work is null) return;
        var origin = _origin;
        var work = _work;
        _work = null;
        _origin = null;
        _caseFiles.Clear();
        Raise(nameof(HasCaseTemplates));
        Raise(nameof(CaseTemplatesText));
        try { Directory.Delete(Path.GetDirectoryName(work)!, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (origin is null) return;
        try { ApplySet(TemplateSet.Open(origin)); }
        catch (EvrakciException) { CloseSet(); }
    }

    /// <summary>A template of the open set changed outside the app (e.g. saved in Word): read it again.</summary>
    public async Task CheckSetChangedAsync()
    {
        if (_set is null || _editor is not null) return;
        if (SetStamp(_set.Folder) == _setStamp) return;
        await OpenSetAsync(_set.Folder, keepInputs: true);
        if (_step == StepPreview) PreparePreview();
        Status = "Şablonlar dışarıda değişmiş; yeniden okundu.";
    }

    static DateTime SetStamp(string folder)
    {
        try { return Directory.EnumerateFiles(folder).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return default; }
    }

    async Task OpenTemplateInWordAsync(string template)
    {
        if (_set is null) return;
        var path = Path.Combine(_set.Folder, template);
        try { TempFiles.OpenWithDefaultProgram(path); }
        catch (System.ComponentModel.Win32Exception) { await Dialogs.Error("Bu dosya türünü açacak bir program bulunamadı."); return; }
        Status = "Şablon Word'de açıldı. Kaydettikten sonra “Yeniden tara” ile değişiklikleri okuyun.";
    }

    async Task RenameDocumentAsync(string template)
    {
        if (_set is null) return;
        var name = InputWindow.Ask("Belgeyi yeniden adlandır", "Belgenin yeni adı", "Ad", Path.GetFileNameWithoutExtension(template),
            ok: "Kaydet", validate: n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın." : null);
        if (name is null) return;
        await ChangeSetAsync(s => TemplateLibrary.RenameDocument(s, template, name));
    }

    async Task RemoveDocumentAsync(string template)
    {
        if (_set is null) return;
        if (!await Dialogs.Confirm($"“{template}” bu setten çıkarılsın mı? Dosya Geri Dönüşüm Kutusu'na taşınır.", "Belgeyi çıkar", "Çıkar", danger: true))
            return;
        await ChangeSetAsync(s =>
        {
            TemplateLibrary.RemoveDocument(s, template);
            var path = Path.Combine(s.Folder, template);
            if (File.Exists(path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        });
    }

    /// <summary>Runs a change on the open set's files and re-opens it, keeping the entered values.</summary>
    async Task ChangeSetAsync(Action<TemplateSet> change)
    {
        if (_set is null) return;
        await Try(async () =>
        {
            change(_set);
            await OpenSetAsync(_set.Folder, keepInputs: true);
        });
    }

    static async Task Try(Func<Task> action)
    {
        try { await action(); }
        catch (EvrakciException ex) { await Dialogs.Error(ex.Message); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Error("İşlem yapılamadı. Dosya başka bir programda açık olabilir ya da klasöre yazma izniniz olmayabilir.");
        }
        catch (OperationCanceledException) { }
    }

    public bool HasRecentSets => RecentSets.Count > 0;

    async Task OpenSetFolderAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Şablon seti klasörünü seçin" };
        if (_settings.RecentSets.FirstOrDefault() is { } last && Directory.Exists(last))
            dlg.InitialDirectory = Path.GetDirectoryName(last);
        if (dlg.ShowDialog() == true) await OpenSetAsync(dlg.FolderName);
    }

    public async Task<bool> OpenSetAsync(string folder, bool keepInputs = false)
    {
        TemplateSet set;
        try { set = TemplateSet.Open(folder); }
        catch (EvrakciException ex)
        {
            await Dialogs.Error(ex.Message, "Set açılamadı");
            return false;
        }
        var previous = keepInputs ? _fields.ToDictionary(f => f.Key, f => f.ToInput()) : null;
        var previousDocs = keepInputs && DocumentChoices.Count > 0
            ? DocumentChoices.ToDictionary(c => c.Template, c => c.IsSelected, StringComparer.OrdinalIgnoreCase) : null;
        ApplySet(set);
        StartCase(previous, previousDocs);
        _setStamp = SetStamp(set.Folder);
        if (_work is null || !string.Equals(folder, _work, StringComparison.OrdinalIgnoreCase)) _settings.AddRecentSet(set.Folder);
        RefreshRecent();
        if (!keepInputs) _overrides.Clear();
        Raise(nameof(Title));
        return true;
    }

    void ApplySet(TemplateSet set)
    {
        _set = set;
        Issues.Clear();
        foreach (var i in set.Issues.Where(i => i.Severity != IssueSeverity.Info).OrderByDescending(i => i.Severity))
            Issues.Add(new IssueItem(i.Severity, i.Message, i.Document));
        SetDocuments.Clear();
        for (int i = 0; i < set.Documents.Count; i++)
        {
            var d = set.Documents[i];
            SetDocuments.Add(new DocumentItem(d.Entry.Template, d.Template?.Format == DocumentFormat.Udf ? "UDF" : "Word",
                d.Template?.Occurrences.Count ?? 0, d.LoadError, d.Entry.Optional, d.Entry.Note, i == 0, i == set.Documents.Count - 1));
        }
        RaiseSetProperties();
    }

    void RaiseSetProperties()
    {
        foreach (var name in new[] { nameof(HasSet), nameof(SetName), nameof(SetDescription), nameof(SetFolder),
                     nameof(FieldCount), nameof(SetHasNoFile), nameof(HasIssues), nameof(CanGoNext), nameof(IsBuiltInSet),
                     nameof(IsOwnSet), nameof(IsExternalSet), nameof(SetCategory), nameof(HasDocumentChoices) })
            Raise(name);
        RequeryCommands();
    }

    /// <summary>Command states depend on async state changes that WPF does not notice on its own.</summary>
    static void RequeryCommands() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    async Task ReloadSetAsync()
    {
        if (_set is null) return;
        await OpenSetAsync(_set.Folder, keepInputs: true);
    }

    async Task EditSettingsAsync()
    {
        if (_set is null) return;
        if (SetSettingsWindow.Edit(_set) is not { } updated) return;
        try
        {
            updated.Save(_set.SetFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Error("set.json kaydedilemedi. Klasöre yazma izniniz olmayabilir.");
            return;
        }
        await OpenSetAsync(_set.Folder, keepInputs: true);
        Status = "Set ayarları kaydedildi.";
    }

    // ------------------------------------------------------------------ step 1: form

    public ObservableCollection<FieldGroupViewModel> Groups { get; } = new();
    public ObservableCollection<string> CaseErrors { get; } = new();
    public int ErrorCount => _resolution?.ErrorCount ?? 0;
    public string FormSummary => _resolution is null ? ""
        : _resolution.IsValid ? "Tüm alanlar tamam."
        : $"{ErrorCount} alan eksik ya da hatalı.";
    public bool FormIsValid => _resolution?.IsValid == true;

    /// <summary>Inputs of a new case: set defaults, then the values the user asked to remember.</summary>
    Dictionary<string, FieldInput> NewInputs()
    {
        var inputs = Resolver.NewCase(_set!);
        foreach (var f in _set!.Fields.Where(f => !f.IsComputed))
            if (_remembered.Get(f.Name) is { } r)
                inputs[f.Key] = new FieldInput
                {
                    Raw = r.Raw, Blank = r.Blank,
                    Currency = f.Type == Core.Values.FieldType.Para ? r.Currency ?? inputs[f.Key].Currency : null,
                };
        return inputs;
    }

    void StartCase(Dictionary<string, FieldInput>? keep, Dictionary<string, bool>? docs = null)
    {
        var inputs = NewInputs();
        if (keep is not null)
            foreach (var (k, v) in keep)
                if (inputs.ContainsKey(k)) inputs[k] = v;
        SetChoices(docs);
        BuildFields(inputs);
    }

    void BuildFields(Dictionary<string, FieldInput> inputs)
    {
        _fields.Clear();
        foreach (var f in _set!.Fields)
        {
            var vm = new FieldViewModel(f, inputs.GetValueOrDefault(f.Key) ?? new FieldInput(), OnFieldChanged,
                name => _set.Field(name)?.Label ?? name, _remembered.Has(f.Name), OnRememberChanged);
            _fields.Add(vm);
        }
        RebuildGroups();
        Resolve();
    }

    // ------------------------------------------------------------------ documents of the case

    public ObservableCollection<DocumentChoice> DocumentChoices { get; } = new();
    public bool HasDocumentChoices => DocumentChoices.Count > 1;
    HashSet<string> _active = new();

    public string DocumentSummary
    {
        get
        {
            int n = DocumentChoices.Count(c => c.IsSelected);
            return n == 0 ? "Hiç tutanak seçilmedi" : $"{n} / {DocumentChoices.Count} tutanak seçili";
        }
    }

    public string SelectedDocumentNames
    {
        get
        {
            var names = DocumentChoices.Where(c => c.IsSelected).Select(c => c.Name).ToList();
            return names.Count == 0 ? "Hiç tutanak seçilmedi." : "Hazırlanacak: " + string.Join(", ", names);
        }
    }

    void RaiseChoices()
    {
        Raise(nameof(DocumentSummary));
        Raise(nameof(SelectedDocumentNames));
    }

    HashSet<string> SelectedDocuments =>
        DocumentChoices.Where(c => c.IsSelected).Select(c => c.Template).ToHashSet(StringComparer.OrdinalIgnoreCase);

    void SetChoices(IReadOnlyDictionary<string, bool>? selection)
    {
        DocumentChoices.Clear();
        foreach (var d in _set!.Documents.Where(d => d.Template is not null))
        {
            bool selected = selection is not null && selection.TryGetValue(d.Entry.Template, out var v) ? v : !d.Entry.Optional;
            DocumentChoices.Add(new DocumentChoice(d.Entry.Template, d.Entry.Note, selected, OnChoiceChanged));
        }
        Raise(nameof(HasDocumentChoices));
        RaiseChoices();
    }

    void OnChoiceChanged()
    {
        RaiseChoices();
        MarkDirty();
        RebuildGroups();
        Resolve();
    }

    /// <summary>The form only shows fields needed by the chosen documents.</summary>
    void RebuildGroups()
    {
        _active = Resolver.ActiveFields(_set!, SelectedDocuments);
        Groups.Clear();
        foreach (var vm in _fields.Where(f => _active.Contains(f.Key)))
        {
            var group = Groups.FirstOrDefault(g => g.Name == vm.Field.Group);
            if (group is null) Groups.Add(group = new FieldGroupViewModel(vm.Field.Group));
            group.Fields.Add(vm);
        }
    }

    void OnRememberChanged(FieldViewModel field)
    {
        if (field.Remember)
        {
            _remembered.Set(field.Name, field.ToInput());
            Status = $"“{field.Label}” hatırlanacak; yeni dosyalarda ve diğer setlerde aynı alana otomatik yazılır.";
        }
        else
        {
            _remembered.Remove(field.Name);
            Status = $"“{field.Label}” artık hatırlanmıyor.";
        }
        RequeryCommands();
    }

    async Task ForgetAllAsync()
    {
        if (!await Dialogs.Confirm($"Hatırlanan {_remembered.Count} bilgi silinsin mi? Formdaki değerler değişmez.", "Hatırlananları temizle", "Temizle"))
            return;
        _remembered.Clear();
        foreach (var f in _fields) f.Remember = false;
        Status = "Hatırlanan bilgiler temizlendi.";
    }

    void OnFieldChanged(FieldViewModel field)
    {
        // A manual suffix only fits the value it was made for.
        if (_set is not null)
            foreach (var doc in _set.Documents.Where(d => d.Template is not null))
                foreach (var o in doc.Template!.Occurrences.Where(o => o.Ref.Key == field.Key))
                    _overrides.Set(doc.Entry.Template, o.Occurrence, null);
        if (field.Remember) _remembered.Set(field.Name, field.ToInput());
        MarkDirty();
        Resolve();
    }

    /// <summary>Changes are saved automatically shortly after the last edit.</summary>
    void MarkDirty()
    {
        if (!_caseOpen) return;
        _dirty = true;
        SaveState = "Kaydediliyor…";
        _autosave.Stop();
        _autosave.Start();
    }

    void Resolve()
    {
        if (_set is null) return;
        _resolution = Resolver.Resolve(_set, _fields.ToDictionary(f => f.Key, f => f.ToInput()), _active);
        foreach (var f in _fields) f.Apply(_resolution.Fields.GetValueOrDefault(f.Key));
        CaseErrors.Clear();
        foreach (var e in _resolution.Errors) CaseErrors.Add(e);
        Raise(nameof(ErrorCount));
        Raise(nameof(FormSummary));
        Raise(nameof(FormIsValid));
        Raise(nameof(CanGenerate));
        Status = _step == StepForm ? FormSummary : Status;
        RequeryCommands();
    }

    // ------------------------------------------------------------------ screens

    public AppScreen Screen
    {
        get => _screen;
        private set
        {
            if (!Set(ref _screen, value)) return;
            foreach (var n in new[] { nameof(IsHome), nameof(IsTemplates), nameof(IsCase), nameof(Title), nameof(CanGoNext), nameof(ShowNext) }) Raise(n);
            if (value != AppScreen.Case) Status = null;
            RequeryCommands();
        }
    }

    public bool IsHome => _screen == AppScreen.Home;
    public bool IsTemplates => _screen == AppScreen.Templates;
    public bool IsCase => _screen == AppScreen.Case;

    /// <summary>On the home screen: choosing the kind of a new file.</summary>
    public bool PickingType
    {
        get => _pickingType;
        set
        {
            if (!Set(ref _pickingType, value)) return;
            if (value) RefreshLibrary();
            Raise(nameof(ShowFiles));
        }
    }

    public bool ShowFiles => !_pickingType;

    async Task GoHomeAsync()
    {
        await CloseCaseAsync();
        PickingType = false;
        RefreshFiles();
        Screen = AppScreen.Home;
    }

    async Task GoTemplatesAsync()
    {
        await CloseCaseAsync();
        RefreshLibrary();
        Screen = AppScreen.Templates;
    }

    // ------------------------------------------------------------------ files (Dosyalarım)

    public ObservableCollection<StoredCase> Files { get; } = new();
    List<StoredCase> _allFiles = new();
    public bool HasFiles => _allFiles.Count > 0;
    public bool NoFiles => _allFiles.Count == 0;
    public string FilesFolder => _store.Folder;

    public string? Search
    {
        get => _search;
        set { if (Set(ref _search, value)) FilterFiles(); }
    }

    public void RefreshFiles()
    {
        try { _allFiles = _store.List(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _allFiles = new(); }
        FilterFiles();
        Raise(nameof(HasFiles));
        Raise(nameof(NoFiles));
    }

    void FilterFiles()
    {
        Files.Clear();
        var q = Tr.Key(_search ?? "");
        foreach (var f in _allFiles.Where(f => q.Length == 0 || Tr.Key(f.Title + " " + f.SetName).Contains(q)))
            Files.Add(f);
    }

    public string? CaseTitle
    {
        get => _caseTitle;
        private set { if (Set(ref _caseTitle, value)) Raise(nameof(Title)); }
    }

    public string? SaveState { get => _saveState; private set => Set(ref _saveState, value); }

    async Task StartNewFileAsync(string folder)
    {
        await CloseCaseAsync();
        if (!await OpenSetAsync(folder)) return;
        if (!_set!.CanGenerate)
            await Dialogs.Info("Bu şablon setinde hatalar var; belgeler üretilmeden önce “Şablonlar” ekranından düzeltilmeli.", "Şablonda sorun var");
        _overrides.Clear();
        _casePath = null;
        _titleIsCustom = false;
        _generatedAt = null;
        CaseTitle = $"Yeni dosya – {SetCategory ?? _set.Name}";
        SaveState = null;
        _caseOpen = true;
        _dirty = false;
        PickingType = false;
        Screen = AppScreen.Case;
        GoTo(StepDocuments);
    }

    async Task OpenStoredAsync(string path)
    {
        CaseData data;
        try { data = _store.Load(path); }
        catch (EvrakciException ex) { await Dialogs.Error(ex.Message, "Dosya açılamadı"); return; }
        await OpenCaseDataAsync(data, path);
    }

    /// <summary>Shows a case: opens its template set, fills the form and continues where it was left.</summary>
    async Task<bool> OpenCaseDataAsync(CaseData data, string? path)
    {
        await CloseCaseAsync();
        // The set may have moved: look for it by name in the library and the recently used folders.
        RefreshLibrary();
        var folder = data.SetFolder is not null && Directory.Exists(data.SetFolder) ? data.SetFolder
            : LibraryGroups.SelectMany(g => g.Items).FirstOrDefault(i => i.Name == data.SetName)?.Folder
              ?? _settings.RecentSets.Where(Directory.Exists).FirstOrDefault(f => TrySetName(f) == data.SetName);
        if (folder is null)
        {
            await Dialogs.Error($"Bu dosyanın şablon seti (“{data.SetName}”) bulunamadı. Set silinmiş ya da taşınmış olabilir.", "Şablon bulunamadı");
            return false;
        }
        if (!await OpenSetAsync(folder)) return false;

        // Template changes saved for this file only: the set is read from a working copy with them.
        if (data.TemplateFiles is { Count: > 0 } own)
        {
            foreach (var (name, b64) in own)
                try { _caseFiles[Path.GetFileName(name)] = Convert.FromBase64String(b64); }
                catch (FormatException) { }
            try
            {
                if (_caseFiles.Count > 0 && !await OpenSetAsync(EnsureWorkCopy())) return false;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Dialogs.Error("Bu dosyaya özel şablon değişiklikleri yüklenemedi; setin şablonları kullanılacak.");
                _caseFiles.Clear();
            }
            Raise(nameof(HasCaseTemplates));
            Raise(nameof(CaseTemplatesText));
        }

        // Values saved with the file win; remembered values only fill fields the file does not have.
        var inputs = NewInputs();
        int unknown = 0;
        foreach (var (name, v) in data.Values)
        {
            var key = Tr.Key(name);
            if (!inputs.ContainsKey(key)) { unknown++; continue; }
            inputs[key] = new FieldInput { Raw = v.Value ?? "", Blank = v.Blank, Currency = v.Currency ?? inputs[key].Currency };
        }
        SetChoices(data.Documents is { } chosen
            ? _set!.Documents.ToDictionary(d => d.Entry.Template, d => chosen.Contains(d.Entry.Template, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
            : null);
        BuildFields(inputs);
        _overrides.Clear();
        if (data.SuffixOverrides is not null)
            foreach (var (doc, map) in data.SuffixOverrides)
                foreach (var (occ, suffix) in map)
                    if (int.TryParse(occ, out var i)) _overrides.Set(doc, i, suffix);
        Resolve();

        _casePath = path;
        _titleIsCustom = data.TitleIsCustom;
        _generatedAt = data.GeneratedAt;
        CaseTitle = data.Title ?? (path is null ? null : Path.GetFileNameWithoutExtension(path));
        SaveState = path is null ? null : "✓ Kaydedildi";
        _caseOpen = true;
        _dirty = false;
        Screen = AppScreen.Case;
        GoTo(StepForm);
        if (unknown > 0)
            await Dialogs.Info($"Dosyada bu sette bulunmayan {unknown} alan vardı; bunlar yok sayıldı.");
        return true;
    }

    static string? TrySetName(string folder)
    {
        try { return TemplateSet.Open(folder).Name; }
        catch (EvrakciException) { return null; }
    }

    /// <summary>Saves pending changes and leaves the file.</summary>
    async Task CloseCaseAsync()
    {
        if (!_caseOpen) return;
        _autosave.Stop();
        if (_dirty) SaveNow();
        _caseOpen = false;
        DropWorkCopy();
        _casePath = null;
        CaseTitle = null;
        SaveState = null;
        RequeryCommands();
        await Task.CompletedTask;
    }

    CaseData Snapshot() => new()
    {
        AppVersion = AppVersion,
        SetName = _set!.Name,
        SetFolder = SetHome,
        TemplateFiles = _caseFiles.Count == 0 ? null
            : _caseFiles.ToDictionary(kv => kv.Key, kv => Convert.ToBase64String(kv.Value), StringComparer.OrdinalIgnoreCase),
        SavedAt = DateTimeOffset.Now,
        Values = _fields.Where(f => !f.IsComputed).ToDictionary(f => f.Name, f => new CaseValue
        {
            Value = string.IsNullOrWhiteSpace(f.Raw) ? null : f.Raw,
            Blank = f.IsBlank,
            Currency = f.IsMoney ? f.Currency : null,
        }),
        Documents = SelectedDocuments.ToList(),
        SuffixOverrides = _overrides.ByDocument.Count == 0 ? null
            : _overrides.ByDocument.Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToDictionary(x => x.Key.ToString(), x => x.Value)),
        Title = _titleIsCustom ? CaseTitle : AutoTitle(),
        TitleIsCustom = _titleIsCustom,
        GeneratedAt = _generatedAt,
    };

    /// <summary>"AYŞE YILMAZ – Kira": the first party's name and the kind of file.</summary>
    string AutoTitle()
    {
        var kind = SetCategory ?? _set!.Name;
        var party = _fields.FirstOrDefault(f => f.Key == Tr.Key("TARAF1 ADI") && !string.IsNullOrWhiteSpace(f.Raw))
                    ?? _fields.FirstOrDefault(f => f.Type == Core.Values.FieldType.Metin && !string.IsNullOrWhiteSpace(f.Raw) &&
                                                   f.Key.StartsWith("TARAF") && f.Key.Contains("AD"));
        return party is null ? $"Adsız dosya – {kind}" : $"{Tr.Upper(Tr.CollapseSpaces(party.Raw))} – {kind}";
    }

    void SaveNow()
    {
        if (!_caseOpen || _set is null) return;
        try
        {
            var data = Snapshot();
            _casePath = _store.Save(_casePath, data);
            CaseTitle = data.Title;
            _dirty = false;
            SaveState = "✓ Kaydedildi";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            SaveState = "⚠ Kaydedilemedi";
            Status = "Dosya kaydedilemedi. Belgelerim klasörüne yazma izni olmayabilir.";
        }
    }

    async Task RenameFileAsync()
    {
        if (!_caseOpen) return;
        var name = InputWindow.Ask("Dosyayı yeniden adlandır", "Dosyanın adı", "Ad", CaseTitle ?? "", ok: "Kaydet",
            validate: n => string.IsNullOrWhiteSpace(n) ? "Bir ad yazın." : null);
        if (name is null) return;
        _titleIsCustom = true;
        CaseTitle = Tr.CollapseSpaces(name);
        _dirty = true;
        SaveNow();
        await Task.CompletedTask;
    }

    async Task DeleteFileAsync(StoredCase file)
    {
        if (!await Dialogs.Confirm($"“{file.Title}” dosyası silinsin mi? Geri Dönüşüm Kutusu'na taşınır; üretilmiş Word/UDF belgeleri silinmez.",
                "Dosyayı sil", "Sil", danger: true))
            return;
        bool current = _caseOpen && string.Equals(_casePath, file.Path, StringComparison.OrdinalIgnoreCase);
        if (current)
        {
            _autosave.Stop();
            _caseOpen = false;
            _dirty = false;
            DropWorkCopy();
        }
        try
        {
            if (File.Exists(file.Path))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file.Path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            await Dialogs.Error("Dosya silinemedi.");
        }
        if (current) await GoHomeAsync();
        else RefreshFiles();
    }

    /// <summary>A copy that can be opened on another computer, optionally with a password.</summary>
    async Task ExportFileAsync()
    {
        if (!_caseOpen || _set is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Dosyayı dışa aktar",
            Filter = "Evrakçı dosyası (*.evrak)|*.evrak",
            DefaultExt = CaseFile.Extension,
            AddExtension = true,
            FileName = Tr.SanitizeFileName(CaseTitle ?? "Dosya") + CaseFile.Extension,
            InitialDirectory = _settings.LastCaseFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog() != true) return;
        if (string.Equals(Path.GetDirectoryName(dlg.FileName), _store.Folder, StringComparison.OrdinalIgnoreCase))
        {
            await Dialogs.Error("Dışa aktarılan kopya Dosyalarım klasörünün dışına kaydedilmeli.");
            return;
        }
        var choice = PasswordWindow.AskToSave();
        if (choice is null) return;
        try { CaseFile.Save(dlg.FileName, Snapshot(), choice.Length == 0 ? null : choice); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Error("Dosya kaydedilemedi. Klasöre yazma izniniz olmayabilir.");
            return;
        }
        _settings.LastCaseFolder = Path.GetDirectoryName(dlg.FileName);
        _settings.Save();
        Status = choice.Length == 0 ? "Dışa aktarıldı." : "Şifreli olarak dışa aktarıldı.";
    }

    /// <summary>Brings an exported .evrak (or an older case file) into Dosyalarım and opens it.</summary>
    async Task ImportAsync(string? path)
    {
        if (path is null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "İçe aktarılacak dosyayı seçin",
                Filter = "Evrakçı dosyası (*.evrak)|*.evrak",
                InitialDirectory = _settings.LastCaseFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dlg.ShowDialog() != true) return;
            path = dlg.FileName;
        }
        if (CaseStore.IsWindowsProtected(path))
        {
            if (string.Equals(Path.GetDirectoryName(path), _store.Folder, StringComparison.OrdinalIgnoreCase)) { await OpenStoredAsync(path); return; }
            CaseData own;
            try { own = _store.Load(path); }
            catch (EvrakciException ex) { await Dialogs.Error(ex.Message, "Dosya açılamadı"); return; }
            if (await OpenCaseDataAsync(own, null)) { _dirty = true; SaveNow(); }
            return;
        }
        CaseData data;
        try
        {
            if (CaseFile.IsEncrypted(path))
            {
                while (true)
                {
                    var password = PasswordWindow.AskToOpen(Path.GetFileName(path));
                    if (password is null) return;
                    try { data = CaseFile.Load(path, password); break; }
                    catch (EvrakciException ex) when (ex.Message.StartsWith("Şifre yanlış"))
                    {
                        await Dialogs.Error(ex.Message, "Dosya açılamadı");
                    }
                }
            }
            else data = CaseFile.Load(path, null);
        }
        catch (EvrakciException ex)
        {
            await Dialogs.Error(ex.Message, "Dosya açılamadı");
            return;
        }
        _settings.LastCaseFolder = Path.GetDirectoryName(path);
        _settings.Save();
        if (await OpenCaseDataAsync(data, null))
        {
            _dirty = true;
            SaveNow();
            Status = "Dosya içe aktarıldı ve Dosyalarım'a eklendi.";
        }
    }

    // ------------------------------------------------------------------ navigation

    async Task NextAsync()
    {
        switch (_step)
        {
            case StepDocuments:
                if (DocumentChoices.Count > 0 && !DocumentChoices.Any(c => c.IsSelected))
                {
                    await Dialogs.Info("Devam etmek için en az bir tutanak işaretleyin.", "Tutanak seçilmedi");
                    return;
                }
                GoTo(StepForm);
                break;
            case StepForm:
                foreach (var f in _fields) f.ShowAllErrors = true;
                GoTo(StepPreview);
                break;
            case StepPreview:
                GoTo(StepGenerate);
                break;
        }
    }

    async Task GoToStepAsync(int target)
    {
        if (target < _step) { GoTo(target); return; }
        while (_step < target)
        {
            int before = _step;
            await NextAsync();
            if (_step == before) return;
        }
    }

    void GoTo(int step)
    {
        if (step < StepDocuments || step > StepGenerate) return;
        Step = step;
        Raise(nameof(CanGoNext));
        RequeryCommands();
        switch (step)
        {
            case StepDocuments:
                Status = "Hazırlanacak tutanakları işaretleyin.";
                break;
            case StepForm:
                Status = FormSummary;
                break;
            case StepPreview:
                PreparePreview();
                Status = FormIsValid ? "Değerler sarı, ekler mavi gösterilir. Eki düzeltmek için üzerine tıklayın." : FormSummary;
                break;
            case StepGenerate:
                PrepareGenerate();
                Status = null;
                break;
            default:
                Status = null;
                break;
        }
    }

    // ------------------------------------------------------------------ step 2: preview

    public ObservableCollection<SetDocument> PreviewDocuments { get; } = new();

    public SetDocument? SelectedPreviewDocument
    {
        get => _selectedPreview;
        set
        {
            if (Set(ref _selectedPreview, value)) RefreshPreview();
        }
    }

    public PreviewDocument? CurrentPreview { get => _preview; private set => Set(ref _preview, value); }

    void PreparePreview()
    {
        var keep = _selectedPreview?.Entry.Template;
        PreviewDocuments.Clear();
        var chosen = SelectedDocuments;
        foreach (var d in _set!.Documents.Where(d => d.Template is not null && chosen.Contains(d.Entry.Template))) PreviewDocuments.Add(d);
        _selectedPreview = PreviewDocuments.FirstOrDefault(d => d.Entry.Template == keep) ?? PreviewDocuments.FirstOrDefault();
        Raise(nameof(SelectedPreviewDocument));
        RefreshPreview();
    }

    void RefreshPreview()
    {
        if (_selectedPreview?.Template is null || _resolution is null) { CurrentPreview = null; return; }
        CurrentPreview = _selectedPreview.Template.Preview(
            new ResolvedValues(_resolution, _overrides.For(_selectedPreview.Entry.Template)));
    }

    public void EditSuffix(PreviewSegment segment, string valueText)
    {
        if (_selectedPreview is null || segment.Occurrence < 0) return;
        var current = segment.Text.Length > 0 ? segment.Text[1..] : "";
        var result = SuffixWindow.Ask(valueText, segment.Text.Length > 0 ? segment.Text[0] : '\'', current, segment.AutoSuffix ?? "");
        if (result is null) return;
        _overrides.Set(_selectedPreview.Entry.Template, segment.Occurrence, result.Value.Reset ? null : result.Value.Suffix);
        MarkDirty();
        RefreshPreview();
    }

    async Task OpenInWordAsync()
    {
        if (_selectedPreview is null || _resolution is null || _set is null) return;
        if (!_resolution.IsValid)
        {
            await Dialogs.Error($"{_resolution.ErrorCount} alan eksik ya da hatalı. Belgeyi açmadan önce bunları tamamlayın.", "Eksik bilgi");
            return;
        }
        try
        {
            var bytes = Generator.Render(_selectedPreview, _resolution, _overrides);
            var name = Generator.Plan(_set, _resolution, Path.GetTempPath(), SelectedDocuments)
                .FirstOrDefault(p => p.Document == _selectedPreview)?.Path is { } p ? Path.GetFileName(p) : _selectedPreview.Entry.Template;
            TempFiles.OpenWithDefaultProgram(TempFiles.Write(name, bytes));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            await Dialogs.Error("Bu dosya türünü açacak bir program bulunamadı.");
        }
    }

    // ------------------------------------------------------------------ step 3: generate

    public ObservableCollection<OutputItem> Outputs { get; } = new();

    /// <summary>Word, UDF or both; remembered between sessions.</summary>
    OutputFormats Formats
    {
        get => Enum.TryParse<OutputFormats>(_settings.OutputFormats, out var f) && (f & OutputFormats.Both) != 0 ? f : OutputFormats.Word;
        set
        {
            _settings.OutputFormats = value.ToString();
            _settings.Save();
            foreach (var n in new[] { nameof(FormatWord), nameof(FormatUdf), nameof(FormatBoth) }) Raise(n);
            RefreshPlan();
        }
    }

    public bool FormatWord { get => Formats == OutputFormats.Word; set { if (value) Formats = OutputFormats.Word; } }
    public bool FormatUdf { get => Formats == OutputFormats.Udf; set { if (value) Formats = OutputFormats.Udf; } }
    public bool FormatBoth { get => Formats == OutputFormats.Both; set { if (value) Formats = OutputFormats.Both; } }
    public ObservableCollection<ErrorItem> BlockingErrors { get; } = new();

    public string? OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (Set(ref _outputFolder, value)) RefreshPlan();
        }
    }

    public string? PlanError
    {
        get => _planError;
        private set
        {
            if (Set(ref _planError, value)) Raise(nameof(HasPlanError));
        }
    }

    public bool HasPlanError => _planError is not null;
    public string? GenerateMessage { get => _generateMessage; private set => Set(ref _generateMessage, value); }
    public bool Generated { get => _generated; private set => Set(ref _generated, value); }
    public bool HasBlockingErrors => BlockingErrors.Count > 0;
    public int ExistingCount => Outputs.Count(o => o.Exists);
    public bool CanGenerate => _set is not null && _resolution?.IsValid == true && Outputs.Count > 0 && PlanError is null;

    void PrepareGenerate()
    {
        Generated = false;
        GenerateMessage = null;
        _outputFolder ??= _settings.LastOutputFolder is { } last && Directory.Exists(last)
            ? last
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Raise(nameof(OutputFolder));
        BlockingErrors.Clear();
        if (SelectedDocuments.Count == 0)
            BlockingErrors.Add(new ErrorItem("Belgeler", "Bu dosya için hiç tutanak seçilmedi. “Hazırlanacak tutanaklar” adımında işaretleyin."));
        if (_resolution is not null)
        {
            // Empty fields in one line; other problems (invalid TCKN, IBAN …) one by one.
            var missing = _fields.Where(f => f.Error == Resolver.MissingMessage).Select(f => $"{f.Label} ({f.Field.Group})").ToList();
            if (missing.Count > 0)
                BlockingErrors.Add(new ErrorItem($"{missing.Count} boş alan",
                    string.Join(", ", missing.Take(12)) + (missing.Count > 12 ? $" ve {missing.Count - 12} alan daha" : "")));
            foreach (var f in _fields.Where(f => f.Error is not null && f.Error != Resolver.MissingMessage))
                BlockingErrors.Add(new ErrorItem(f.Label, f.Error!));
            foreach (var e in _resolution.Errors) BlockingErrors.Add(new ErrorItem("Kontrol", e));
        }
        Raise(nameof(HasBlockingErrors));
        RefreshPlan();
    }

    void RefreshPlan()
    {
        Outputs.Clear();
        PlanError = null;
        if (_set is null || _resolution is null || !_resolution.IsValid || string.IsNullOrWhiteSpace(_outputFolder))
        {
            Raise(nameof(CanGenerate));
            Raise(nameof(ExistingCount));
            return;
        }
        try
        {
            foreach (var p in Generator.Plan(_set, _resolution, _outputFolder, SelectedDocuments, Formats))
                Outputs.Add(new OutputItem(Path.GetFileName(p.Path), p.Path, p.Exists));
        }
        catch (EvrakciException ex) { PlanError = ex.Message; }
        Raise(nameof(CanGenerate));
        Raise(nameof(ExistingCount));
        RequeryCommands();
    }

    void ChooseOutputFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Belgelerin kaydedileceği klasörü seçin" };
        if (_outputFolder is not null && Directory.Exists(_outputFolder)) dlg.InitialDirectory = _outputFolder;
        if (dlg.ShowDialog() == true) OutputFolder = dlg.FolderName;
    }

    async Task GenerateAsync()
    {
        if (_set is null || _resolution is null || _outputFolder is null) return;
        RefreshPlan();
        if (!CanGenerate) return;
        bool overwrite = false;
        int existing = ExistingCount;
        if (existing > 0)
        {
            overwrite = await Dialogs.Confirm(
                $"{existing} dosya bu klasörde zaten var. Üzerlerine yazılsın mı? Eski halleri kaybolur.",
                "Dosyalar zaten var", "Üzerine yaz", "Vazgeç", danger: true);
            if (!overwrite) return;
        }
        try
        {
            var plan = Generator.Plan(_set, _resolution, _outputFolder, SelectedDocuments, Formats);
            Generator.Write(plan, _resolution, _overrides, overwrite);
            _settings.LastOutputFolder = _outputFolder;
            _settings.Save();
            Outputs.Clear();
            foreach (var p in plan) Outputs.Add(new OutputItem(Path.GetFileName(p.Path), p.Path, true, Written: true));
            Raise(nameof(ExistingCount));
            Generated = true;
            _generatedAt = DateTimeOffset.Now;
            MarkDirty();
            GenerateMessage = $"{plan.Count} belge üretildi.";
        }
        catch (EvrakciException ex)
        {
            await Dialogs.Error(ex.Message, "Belgeler üretilemedi");
        }
    }

    static void OpenFolder(string folder) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });

    // ------------------------------------------------------------------ startup / shutdown

    public async Task OpenFromCommandLineAsync(string path)
    {
        if (Directory.Exists(path))
        {
            await GoTemplatesAsync();
            await OpenSetAsync(path);
        }
        else if (File.Exists(path) && path.EndsWith(CaseFile.Extension, StringComparison.OrdinalIgnoreCase)) await ImportAsync(path);
    }

    /// <summary>Everything is saved automatically; closing only flushes the last edit.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (_editor is not null)
        {
            // The editor may hold unsaved changes; it asks about them when it closes.
            _editor.Activate();
            _editor.Close();
            if (_editor is not null) return false;
        }
        await CloseCaseAsync();
        _updater.ApplyOnExit();
        return true;
    }
}
