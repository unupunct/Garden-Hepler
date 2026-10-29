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

    public SpineItem(BookRow row)
    {
        Row = row;
        var m = Shelves.Spine(row.Title, row.Book.WordCount);
        Width = m.Width; Height = m.Height; Fill = Palette[m.ColorIndex];
        TextLength = m.Height - 46;
        Tip = $"{row.Title}\n{row.Author}\n{row.Format} · {row.Meta}";
    }

    public BookRow Row { get; }
    public double Width { get; }
    public double Height { get; }
    public double TextLength { get; }
    public Brush Fill { get; }
    public string Title => Row.Title;
    public string Tip { get; }
    [ObservableProperty] private bool _isSelected;
    /// <summary>Shown faded while a title search is active and this book does not match.</summary>
    [ObservableProperty] private bool _dimmed;
}

public sealed record ShelfRow(IReadOnlyList<SpineItem> Spines);
public sealed record LetterGroup(string Letter, IReadOnlyList<BookRow> Books);

/// <summary>The bookcase and the A-Z index.</summary>
public sealed partial class LibraryViewModel
{
    public ObservableCollection<ShelfRow> Shelves { get; } = new();
    public ObservableCollection<LetterGroup> Letters { get; } = new();
    public ObservableCollection<string> LetterBar { get; } = new();
    private readonly Dictionary<int, SpineItem> _spines = new();
    private double _shelfWidth = 900;

    [ObservableProperty] private string _titleFilter = "";
    [ObservableProperty] private string? _letterFilter;
    [ObservableProperty] private bool _noTitleMatches;

    partial void OnTitleFilterChanged(string value) => RebuildIndex();
    partial void OnLetterFilterChanged(string? value) => RebuildIndex();

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
    private void RebuildAll()
    {
        _spines.Clear();
        foreach (var b in Books) _spines[b.Book.Id] = new SpineItem(b);
        RebuildShelves(); RebuildIndex();
        MarkSelected();
    }

    private void RebuildShelves()
    {
        Shelves.Clear();
        var ordered = Books.OrderBy(b => GardenHelper.Core.Library.Shelves.SortKey(b.Title), StringComparer.Ordinal).Select(b => _spines[b.Book.Id]).ToList();
        var rows = GardenHelper.Core.Library.Shelves.Rows(ordered.Select(s => s.Width).ToList(), _shelfWidth - 36, 3);
        foreach (var row in rows) Shelves.Add(new ShelfRow(row.Select(i => ordered[i]).ToList()));
        ApplyDimming();
    }

    private void ApplyDimming()
    {
        var active = !string.IsNullOrWhiteSpace(TitleFilter);
        foreach (var s in _spines.Values) s.Dimmed = active && !Matches(s.Row, TitleFilter);
    }

    private void RebuildIndex()
    {
        ApplyDimming();
        var matching = Books.Where(b => Matches(b, TitleFilter)).ToList();
        var groups = GardenHelper.Core.Library.Shelves.Group(matching, b => b.Title);
        LetterBar.Clear(); foreach (var g in groups) LetterBar.Add(g.Letter);
        if (LetterFilter is not null && groups.All(g => g.Letter != LetterFilter)) { LetterFilter = null; return; }
        Letters.Clear();
        foreach (var g in groups.Where(g => LetterFilter is null || g.Letter == LetterFilter)) Letters.Add(new LetterGroup(g.Letter, g.Items));
        NoTitleMatches = matching.Count == 0 && Books.Count > 0;
    }

    private void MarkSelected()
    {
        foreach (var s in _spines.Values) s.IsSelected = Selected is not null && s.Row.Book.Id == Selected.Book.Id;
    }

    [RelayCommand]
    private void PickLetter(string letter) => LetterFilter = LetterFilter == letter ? null : letter;

    [RelayCommand]
    private void ClearTitleFilter() { TitleFilter = ""; LetterFilter = null; }

    /// <summary>Clicking a spine or a title in the index selects the book (its details appear); double-clicking reads it.</summary>
    [RelayCommand]
    private void SelectBook(BookRow? row) { if (row is not null) Selected = Books.FirstOrDefault(b => b.Book.Id == row.Book.Id) ?? row; }
}
