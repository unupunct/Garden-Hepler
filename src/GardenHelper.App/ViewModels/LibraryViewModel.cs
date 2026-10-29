using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Data;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Library;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record BookRow(Book Book)
{
    public override string ToString() => $"{Book.Title}, {Book.Author}";
    public string Title => Book.Title;
    public string Author => Book.Author ?? Loc.T("lib_unknown_author");
    public string Format => Book.Format;
    public string Meta => Book.IndexStatus == IndexStatus.Indexed
        ? Loc.F("lib_meta", Book.SectionCount, Book.WordCount.ToString("N0", Loc.Instance.Culture))
        : Loc.T("lib_not_searchable");
}

public sealed partial class LibraryViewModel : PageViewModel
{
    private readonly LibraryService _library;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private CancellationTokenSource? _searchCts;

    public LibraryViewModel(LibraryService library, StarterLibrary starter, IDialogs dialogs, INavigator nav, ILogger<LibraryViewModel> log) : base(log)
    {
        _starter = starter;
        _library = library; _dialogs = dialogs; _nav = nav;
    }

    public ObservableCollection<BookRow> Books { get; } = new();
    public ObservableCollection<SubjectCount> Subjects { get; } = new();
    public ObservableCollection<LibraryHit> Hits { get; } = new();
    public ObservableCollection<string> SelectedSubjects { get; } = new();
    public ObservableCollection<string> ImportProblems { get; } = new();

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string? _initialQuery;
    [ObservableProperty] private BookRow? _selected;
    [ObservableProperty] private SubjectCount? _subjectFilter;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _hasQuery;
    [ObservableProperty] private bool _noHits;
    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editAuthor = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private string _supportedFormats = string.Join("  ", BookFormats.SupportedExtensions);

    public IReadOnlyList<string> Formats { get; } = BookFormats.SupportedExtensions.ToList();

    partial void OnSelectedChanged(BookRow? value)
    {
        MarkSelected();
        if (value is null) return;
        EditTitle = value.Book.Title; EditAuthor = value.Book.Author ?? ""; EditTags = value.Book.Tags;
        SelectedSubjects.Clear();
        _ = LoadSubjectsAsync(value.Book.Id);
    }

    private async Task LoadSubjectsAsync(int id)
    {
        var full = await _library.GetAsync(id);
        if (full is null || Selected?.Book.Id != id) return;
        SelectedSubjects.Clear();
        foreach (var s in full.Subjects.Take(24)) SelectedSubjects.Add(s.Term);
    }

    partial void OnQueryChanged(string value)
    {
        HasQuery = !string.IsNullOrWhiteSpace(value);
        _searchCts?.Cancel();
        if (!HasQuery) { Hits.Clear(); NoHits = false; return; }
        var cts = _searchCts = new CancellationTokenSource();
        _ = DebouncedSearchAsync(value, cts.Token);
    }

    partial void OnSubjectFilterChanged(SubjectCount? value) => _ = RefreshBooksAsync();

    private async Task DebouncedSearchAsync(string text, CancellationToken ct)
    {
        try
        {
            await Task.Delay(300, ct);
            IsSearching = true;
            var hits = await _library.SearchAsync(text, null, 60, ct);
            ct.ThrowIfCancellationRequested();
            Hits.Clear();
            foreach (var h in hits) Hits.Add(h);
            NoHits = hits.Count == 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = Loc.F("err_generic", ex.Message); }
        finally { if (!ct.IsCancellationRequested) IsSearching = false; }
    }

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            await RefreshBooksAsync();
            var subjects = await _library.TopSubjectsAsync(30);
            Subjects.Clear();
            foreach (var s in subjects) Subjects.Add(s);
        });
        if (InitialQuery is { } q) { Query = q; InitialQuery = null; }
    }

    private async Task RefreshBooksAsync()
    {
        var keep = Selected?.Book.Id;
        var books = await _library.ListAsync(null, SubjectFilter?.Term);
        Books.Clear();
        foreach (var b in books) Books.Add(new BookRow(b));
        IsEmpty = Books.Count == 0 && SubjectFilter is null;
        StatsText = Loc.F("lib_stats_books", Books.Count);
        Selected = Books.FirstOrDefault(b => b.Book.Id == keep) ?? Selected;
        RebuildAll();
    }

    [RelayCommand]
    private Task BrowseAsync()
    {
        var files = _dialogs.PickBooks(BookFormats.DialogFilter);
        return files is null ? Task.CompletedTask : ImportAsync(files);
    }

    private readonly StarterLibrary _starter;

    /// <summary>Choose a folder and import every readable book in it (and its subfolders).</summary>
    [RelayCommand]
    private Task BrowseFolderAsync()
    {
        if (_dialogs.PickFolder() is not { } dir) return Task.CompletedTask;
        var files = StarterLibrary.BooksIn(dir);
        if (files.Count == 0) { ErrorMessage = Loc.T("lib_folder_empty"); return Task.CompletedTask; }
        return ImportAsync(files);
    }

    /// <summary>Adds the books that come with the app (the ones already in the library are skipped).</summary>
    [RelayCommand]
    private Task AddStarterAsync() => ImportAsync(StarterLibrary.BooksIn(StarterFolder));

    public static string StarterFolder => Path.Combine(AppContext.BaseDirectory, "StarterLibrary");

    /// <summary>Imports files (also used by drag and drop). Failures are listed; they never stop the rest of the batch.</summary>
    public Task ImportAsync(IEnumerable<string> files) => RunAsync(async () =>
    {
        IsImporting = true;
        ImportProblems.Clear();
        var progress = new Progress<ImportProgress>(p =>
        {
            Progress = (p.Index - 1) * 100.0 / p.Count;
            ProgressText = Loc.F("lib_importing", p.File, p.Index, p.Count);
        });
        try
        {
            var results = await _library.ImportAsync(files, progress);
            // A file that is already in the library is not a problem: it is counted, not listed.
            foreach (var r in results.Where(r => r.Error is not null && !r.Duplicate))
                ImportProblems.Add($"{Path.GetFileName(r.File)}: {r.Error}");
            var ok = results.Count(r => r.Book is not null);
            var skipped = results.Count(r => r.Duplicate);
            StatusMessage = skipped > 0 ? Loc.F("lib_import_summary", ok, skipped) : ok > 0 ? Loc.F("lib_imported", ok) : null;
            await LoadAsync();
            if (results.FirstOrDefault(r => r.Book is not null) is { } first)
                Selected = Books.FirstOrDefault(b => b.Book.Id == first.Book!.Id);
        }
        finally { IsImporting = false; Progress = 0; ProgressText = ""; }
    }, busy: false);

    [RelayCommand] private void DismissProblems() => ImportProblems.Clear();

    [RelayCommand]
    private void Read(BookRow? row)
    {
        var b = (row ?? Selected)?.Book;
        if (b is null) return;
        _nav.GoTo<ReaderViewModel>(vm => vm.Open(b.Id, Math.Max(1, b.LastReadSection), null));
    }

    [RelayCommand]
    private void OpenHit(LibraryHit hit) =>
        _nav.GoTo<ReaderViewModel>(vm => vm.Open(hit.BookId, hit.Ordinal, Query));

    [RelayCommand]
    private void FilterSubject(SubjectCount? subject) => SubjectFilter = subject is null || subject == SubjectFilter ? null : subject;

    [RelayCommand]
    private void SearchSubject(string term) => Query = term;

    [RelayCommand]
    private Task SaveDetailsAsync() => RunAsync(async () =>
    {
        if (Selected is null || string.IsNullOrWhiteSpace(EditTitle)) return;
        await _library.UpdateDetailsAsync(Selected.Book.Id, EditTitle, EditAuthor, EditTags);
        StatusMessage = Loc.T("saved");
        await RefreshBooksAsync();
    }, busy: false);

    [RelayCommand]
    private Task ReindexAsync() => RunAsync(async () =>
    {
        if (Selected is null) return;
        await _library.ReindexAsync(Selected.Book.Id);
        StatusMessage = Loc.T("lib_reindexed");
        await LoadAsync();
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is null || !_dialogs.Confirm(Loc.F("lib_confirm_delete", Selected.Title))) return;
        await _library.DeleteAsync(Selected.Book.Id);
        Selected = null;
        await LoadAsync();
    });
}
