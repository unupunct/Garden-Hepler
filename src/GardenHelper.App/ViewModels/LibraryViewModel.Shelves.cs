using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Library;

namespace GardenHelper.App.ViewModels;

/// <summary>One book standing on a shelf.</summary>
public sealed partial class SpineItem : ObservableObject
{
    private static readonly Brush[] Palette = new[]
    {
        "#7B2D26", "#2F5D3A", "#1F3A5F", "#B07A1E", "#3C6E71", "#5B3A29", "#6A3D6B", "#4A5568", "#8C4A2F", "#2E4F4F", "#9C5B2E", "#4B3F72",
    }.Select(c => { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(c)!; b.Freeze(); return (Brush)b; }).ToArray();

    public SpineItem(BookRow row, double scale)
    {
        Row = row;
        var m = GardenHelper.Core.Library.Shelves.Spine(row.Title, row.Book.WordCount, scale);
        Width = m.Width; Height = m.Height; Fill = Palette[m.ColorIndex];
        TextLength = Math.Max(24, m.Height - 46 * scale);
        FontSize = Math.Clamp(12.5 * scale + 0.5, 8, 12.5);
        ShowBands = scale >= 0.7;
        ShowText = m.Width >= 15;
        Tip = $"{row.Title}\n{row.Author}\n{row.Format} · {row.Meta}";
    }

    public BookRow Row { get; }
    public double Width { get; }
    public double Height { get; }
    public double TextLength { get; }
    public double FontSize { get; }
    public bool ShowBands { get; }
    public bool ShowText { get; }
    public Brush Fill { get; }
    public string Title => Row.Title;
    public string Tip { get; }
    [ObservableProperty] private bool _isSelected;
}

public sealed record ShelfRow(IReadOnlyList<SpineItem> Spines);
public sealed record LetterGroup(string Letter, IReadOnlyList<BookRow> Books);

/// <summary>The bookcase (with pages for big libraries) and the A-Z index.</summary>
public sealed partial class LibraryViewModel
{
    private const int BigLibrary = 250;       // above this the index shows one letter at a time
    private const int IndexCap = 300;

    public ObservableCollection<ShelfRow> Shelves { get; } = new();
    public ObservableCollection<LetterGroup> Letters { get; } = new();
    public ObservableCollection<string> LetterBar { get; } = new();
    private readonly Dictionary<int, SpineItem> _spines = new();
    private List<List<SpineItem>> _rows = new();
    private int _perPage = 6;
    private double _shelfWidth = 900;

    [ObservableProperty] private string _titleFilter = "";
    [ObservableProperty] private string? _letterFilter;
    [ObservableProperty] private bool _noTitleMatches;
    [ObservableProperty] private double _shelfHeight = 218;
    [ObservableProperty] private int _pageIndex;
    [ObservableProperty] private int _pageCount = 1;
    [ObservableProperty] private bool _hasPages;
    [ObservableProperty] private string _pageText = "";
    [ObservableProperty] private string? _indexNote;

    partial void OnTitleFilterChanged(string value) { PageIndex = 0; RebuildShelves(); RebuildIndex(); }
    partial void OnLetterFilterChanged(string? value) => RebuildIndex();
    partial void OnPageIndexChanged(int value) => ShowPage();

    /// <summary>Called by the view when its width changes, so the shelves hold as many books as fit.</summary>
    public void SetShelfWidth(double width)
    {
        var w = Math.Max(300, width);
        if (Math.Abs(w - _shelfWidth) < 12) return;
        _shelfWidth = w; RebuildShelves();
    }

    private static bool Matches(BookRow r, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var hay = SubjectExtractor.Fold(r.Title + " " + r.Book.Author);
        return SubjectExtractor.Fold(filter).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(hay.Contains);
    }

    /// <summary>Rebuilds shelves and index after the list of books changed.</summary>
    private void RebuildAll() { RebuildShelves(); RebuildIndex(); }

    /// <summary>Lays the books out on shelves. The size of the books depends on the whole library; a title search shows only the matches.</summary>
    private void RebuildShelves()
    {
        var scale = GardenHelper.Core.Library.Shelves.ScaleFor(Books.Count);
        ShelfHeight = Math.Round(210 * scale) + 34;
        _spines.Clear();
        var ordered = Books.Where(b => Matches(b, TitleFilter))
            .OrderBy(b => GardenHelper.Core.Library.Shelves.SortKey(b.Title), StringComparer.Ordinal)
            .Select(b => _spines[b.Book.Id] = new SpineItem(b, scale)).ToList();
        var rows = GardenHelper.Core.Library.Shelves.Rows(ordered.Select(s => s.Width).ToList(), _shelfWidth - 36, 3);
        _rows = rows.Select(r => r.Select(i => ordered[i]).ToList()).ToList();
        _perPage = GardenHelper.Core.Library.Shelves.ShelvesPerPage(_rows.Count, scale);
        PageCount = GardenHelper.Core.Library.Shelves.PageCount(_rows.Count, _perPage);
        HasPages = PageCount > 1;
        if (PageIndex >= PageCount) PageIndex = PageCount - 1;
        ShowPage();
    }

    private void ShowPage()
    {
        Shelves.Clear();
        foreach (var r in _rows.Skip(PageIndex * _perPage).Take(_perPage)) Shelves.Add(new ShelfRow(r));
        PageText = Loc.F("lib_page", PageIndex + 1, PageCount);
        MarkSelected();
    }

    private void RebuildIndex()
    {
        var matching = Books.Where(b => Matches(b, TitleFilter)).ToList();
        var groups = GardenHelper.Core.Library.Shelves.Group(matching, b => b.Title);
        var big = Books.Count > BigLibrary;
        LetterBar.Clear(); foreach (var g in groups) LetterBar.Add(g.Letter);
        if (LetterFilter is not null && groups.All(g => g.Letter != LetterFilter)) { LetterFilter = null; return; }
        if (big && LetterFilter is null && groups.Count > 0 && string.IsNullOrWhiteSpace(TitleFilter)) { LetterFilter = groups[0].Letter; return; }   // a big library shows one letter at a time
        Letters.Clear();
        var shown = 0; var truncated = false;
        foreach (var g in groups.Where(g => LetterFilter is null || g.Letter == LetterFilter))
        {
            var take = g.Items.Take(Math.Max(0, IndexCap - shown)).ToList();
            if (take.Count < g.Items.Count) truncated = true;
            if (take.Count > 0) Letters.Add(new LetterGroup(g.Letter, take));
            shown += take.Count;
        }
        IndexNote = truncated ? Loc.F("lib_index_cap", IndexCap) : null;
        NoTitleMatches = matching.Count == 0 && Books.Count > 0;
    }

    private void MarkSelected()
    {
        foreach (var s in _spines.Values) s.IsSelected = Selected is not null && s.Row.Book.Id == Selected.Book.Id;
    }

    /// <summary>Turns to the page whose shelves hold the book (when the shelves are showing it).</summary>
    private void ShowBook(int bookId)
    {
        var shelf = _rows.FindIndex(r => r.Any(s => s.Row.Book.Id == bookId));
        if (shelf >= 0) PageIndex = GardenHelper.Core.Library.Shelves.PageOfShelf(shelf, _perPage);
    }

    [RelayCommand] private void NextPage() { if (PageIndex < PageCount - 1) PageIndex++; }
    [RelayCommand] private void PreviousPage() { if (PageIndex > 0) PageIndex--; }

    /// <summary>Picks a letter in the index and turns the shelves to the first book of that letter.</summary>
    [RelayCommand]
    private void PickLetter(string letter)
    {
        LetterFilter = Books.Count > BigLibrary ? letter : (LetterFilter == letter ? null : letter);
        var first = _rows.SelectMany(r => r).FirstOrDefault(s => GardenHelper.Core.Library.Shelves.LetterOf(s.Row.Title) == letter);
        if (first is not null) ShowBook(first.Row.Book.Id);
    }

    [RelayCommand]
    private void ClearTitleFilter() { TitleFilter = ""; LetterFilter = null; }

    /// <summary>Clicking a spine or a title in the index selects the book (its details appear) and turns the shelves to it; double-clicking reads it.</summary>
    [RelayCommand]
    private void SelectBook(BookRow? row)
    {
        if (row is null) return;
        Selected = Books.FirstOrDefault(b => b.Book.Id == row.Book.Id) ?? row;
        ShowBook(row.Book.Id);
    }
}
