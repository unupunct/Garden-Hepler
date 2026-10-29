using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Library;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed partial class ReaderViewModel : PageViewModel
{
    private readonly LibraryService _library;
    private readonly ReaderPreparer _preparer;
    private readonly INavigator _nav;
    private int _bookId, _startOrdinal = 1;
    private string? _startTerm;
    private List<BookSection> _allSections = new();

    public ReaderViewModel(LibraryService library, ReaderPreparer preparer, INavigator nav, ILogger<ReaderViewModel> log) : base(log)
    {
        _library = library; _preparer = preparer; _nav = nav;
    }

    public ObservableCollection<BookSection> Sections { get; } = new();
    public ObservableCollection<LibraryHit> Hits { get; } = new();

    [ObservableProperty] private Book? _book;
    [ObservableProperty] private BookSection? _current;
    [ObservableProperty] private ReaderTarget? _target;
    [ObservableProperty] private string? _highlight;
    [ObservableProperty] private string? _fallbackText;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private bool _night;
    [ObservableProperty] private bool _showToc = true;
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _tocFilter = "";
    [ObservableProperty] private string _positionText = "";
    [ObservableProperty] private bool _noHits;
    [ObservableProperty] private bool _showHits;
    /// <summary>Set by the view when the WebView2 runtime is missing; the reader then shows plain text.</summary>
    [ObservableProperty] private bool _webViewUnavailable;

    public void Open(int bookId, int ordinal, string? term)
    {
        _bookId = bookId; _startOrdinal = ordinal; _startTerm = term;
        Query = term ?? "";
    }

    partial void OnTocFilterChanged(string value) => ApplyTocFilter();

    private void ApplyTocFilter()
    {
        var f = SubjectExtractor.Fold(TocFilter ?? "");
        Sections.Clear();
        foreach (var s in _allSections.Where(s => f.Length == 0 || SubjectExtractor.Fold(s.Title).Contains(f))) Sections.Add(s);
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        Book = await _library.GetAsync(_bookId);
        if (Book is null) { ErrorMessage = Loc.T("lib_missing_book"); return; }
        _allSections = await _library.GetSectionHeadersAsync(_bookId);
        ApplyTocFilter();
        await ShowAsync(Math.Clamp(_startOrdinal, 1, Math.Max(1, _allSections.Count)), _startTerm);
    });

    /// <summary>Loads a section into the reader. Selecting it in the table of contents does the same.</summary>
    public async Task ShowAsync(int ordinal, string? term)
    {
        if (Book is null) return;
        var header = _allSections.FirstOrDefault(s => s.Ordinal == ordinal);
        if (header is null) return;
        _suppress = true; Current = Sections.FirstOrDefault(s => s.Ordinal == ordinal); _suppress = false;
        Highlight = string.IsNullOrWhiteSpace(term) ? null : term;
        PositionText = Loc.F("reader_position", ordinal, _allSections.Count);
        ErrorMessage = null;
        try
        {
            var full = await _library.GetSectionAsync(Book.Id, ordinal);
            FallbackText = full is null ? null : full.Title + "\n\n" + full.Text;
            Target = await _preparer.PrepareAsync(Book, ordinal);
            await _library.MarkReadAsync(Book.Id, ordinal);
            _startOrdinal = ordinal;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or InvalidDataException)
        {
            ErrorMessage = Loc.F("err_generic", ex.Message);
            Target = null;
        }
    }

    private bool _suppress;
    partial void OnCurrentChanged(BookSection? value)
    {
        if (!_suppress && value is not null) _ = ShowAsync(value.Ordinal, Highlight);
    }

    private int Index => _allSections.FindIndex(s => s.Ordinal == _startOrdinal);

    [RelayCommand] private Task NextAsync() => Index + 1 < _allSections.Count ? ShowAsync(_allSections[Index + 1].Ordinal, null) : Task.CompletedTask;
    [RelayCommand] private Task PrevAsync() => Index > 0 ? ShowAsync(_allSections[Index - 1].Ordinal, null) : Task.CompletedTask;
    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(3, Math.Round(Zoom + 0.1, 1));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.5, Math.Round(Zoom - 0.1, 1));
    [RelayCommand] private void ToggleToc() => ShowToc = !ShowToc;
    [RelayCommand] private void ToggleNight() => Night = !Night;
    [RelayCommand] private void Back() => _nav.GoTo<LibraryViewModel>();

    [RelayCommand]
    private Task SearchInBookAsync() => RunAsync(async () =>
    {
        Hits.Clear();
        if (string.IsNullOrWhiteSpace(Query)) { ShowHits = false; return; }
        var hits = await _library.SearchAsync(Query, _bookId, 100);
        foreach (var h in hits) Hits.Add(h);
        NoHits = hits.Count == 0;
        ShowHits = true;
    }, busy: false);

    [RelayCommand]
    private Task OpenHitAsync(LibraryHit hit) => ShowAsync(hit.Ordinal, Query);

    [RelayCommand] private void CloseHits() => ShowHits = false;
}
