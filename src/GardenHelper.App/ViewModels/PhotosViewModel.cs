using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PhotoTile(Photo Photo, ImageSource? Thumb, string About, string DateText, string Caption);
public sealed record PhotoMonthGroup(string Title, IReadOnlyList<PhotoTile> Tiles);
public sealed record OwnerOption(int? Id, string? PlantId, string Name) { public override string ToString() => Name; }

/// <summary>A timeline of pictures of the property: beds, plants, animals, places. Add a picture, say what it is about, and look back at any month.</summary>
public sealed partial class PhotosViewModel : PageViewModel
{
    public static readonly string[] Kinds = { "general", "bed", "plant", "animal", "place" };
    private readonly Repository _repo;
    private readonly SeedPhotoStore _store;
    private readonly KnowledgeBase _kb;
    private readonly IDialogs _dialogs;
    private List<Photo> _all = new();
    private Dictionary<(string, int), string> _names = new();

    public PhotosViewModel(Repository repo, SeedPhotoStore store, KnowledgeBase kb, IDialogs dialogs, ILogger<PhotosViewModel> log) : base(log)
    {
        _repo = repo; _store = store; _kb = kb; _dialogs = dialogs;
        KindFilters = new[] { new AnimalOption("all", Loc.T("photos_all")) }.Concat(Kinds.Select(k => new AnimalOption(k, Loc.T("photos_k_" + k)))).ToList();
        _kindFilter = KindFilters[0];
        NewKinds = Kinds.Select(k => new AnimalOption(k, Loc.T("photos_k_" + k))).ToList();
        _newKind = NewKinds[0];
    }

    public IReadOnlyList<AnimalOption> KindFilters { get; }
    public IReadOnlyList<AnimalOption> NewKinds { get; }
    public ObservableCollection<PhotoMonthGroup> Groups { get; } = new();
    public ObservableCollection<OwnerOption> Owners { get; } = new();
    [ObservableProperty] private AnimalOption _kindFilter;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private bool _isEmpty;
    // adding
    [ObservableProperty] private bool _isAdding;
    [ObservableProperty] private string? _pendingFile;
    [ObservableProperty] private ImageSource? _pendingThumb;
    [ObservableProperty] private string _caption = "";
    [ObservableProperty] private AnimalOption _newKind;
    [ObservableProperty] private OwnerOption? _newOwner;
    [ObservableProperty] private DateTime? _newDate = DateTime.Today;
    // viewing
    [ObservableProperty] private PhotoTile? _viewing;
    [ObservableProperty] private ImageSource? _viewImage;

    partial void OnKindFilterChanged(AnimalOption value) => Rebuild();
    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnNewKindChanged(AnimalOption value) => _ = LoadOwnersAsync();

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _all = await _repo.ListAsync<Photo>(q => q.OrderByDescending(p => p.Date).ThenByDescending(p => p.Id));
            var names = new Dictionary<(string, int), string>();
            foreach (var b in await _repo.ListAsync<GardenBed>(null)) names[("bed", b.Id)] = b.Name;
            foreach (var g in await _repo.ListAsync<AnimalGroup>(null)) names[("animal", g.Id)] = g.Name;
            foreach (var f in await _repo.ListAsync<PropertyFeature>(null)) names[("place", f.Id)] = f.Name;
            _names = names;
            Rebuild();
            await LoadOwnersAsync();
        });
    }

    private string AboutOf(Photo p)
    {
        var parts = new List<string>();
        if (p.OwnerId is { } id && _names.TryGetValue((p.OwnerKind, id), out var n)) parts.Add(n);
        if (p.PlantId is { } pid) parts.Add(_kb.Get(pid)?.DisplayName(Loc.Instance.Language) ?? pid);
        return string.Join(" · ", parts);
    }

    private void Rebuild()
    {
        var q = Search.Trim();
        var shown = _all.Where(p => (KindFilter.Key == "all" || p.OwnerKind == KindFilter.Key) &&
            (q.Length == 0 || (p.Caption ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) || AboutOf(p).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        var culture = Loc.Instance.Culture;
        Groups.Clear();
        foreach (var month in shown.GroupBy(p => new DateOnly(p.Date.Year, p.Date.Month, 1)).OrderByDescending(g => g.Key))
            Groups.Add(new PhotoMonthGroup(month.Key.ToString("MMMM yyyy", culture),
                month.Select(p => new PhotoTile(p, PhotoImage.Load(_store.PathOf(p.File), 260), AboutOf(p), p.Date.ToString("d MMM yyyy", culture), p.Caption ?? "")).ToList()));
        CountText = Loc.FN("photos_count", shown.Count, shown.Count);
        IsEmpty = _all.Count == 0;
    }

    private async Task LoadOwnersAsync()
    {
        Owners.Clear();
        switch (NewKind.Key)
        {
            case "bed": foreach (var b in await _repo.ListAsync<GardenBed>(q => q.OrderBy(x => x.Name))) Owners.Add(new OwnerOption(b.Id, null, b.Name)); break;
            case "animal": foreach (var g in await _repo.ListAsync<AnimalGroup>(q => q.OrderBy(x => x.Name))) Owners.Add(new OwnerOption(g.Id, null, g.Name)); break;
            case "place": foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.OrderBy(x => x.Name))) Owners.Add(new OwnerOption(f.Id, null, f.Name)); break;
            case "plant": foreach (var p in _kb.Plantable.OrderBy(x => x.DisplayName(Loc.Instance.Language))) Owners.Add(new OwnerOption(null, p.Id, p.DisplayName(Loc.Instance.Language))); break;
        }
        NewOwner = Owners.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ChooseAsync()
    {
        var file = _dialogs.PickFile(Loc.T("photos_filter"));
        if (file is null) return;
        var stored = await _store.AddFileAsync(file);
        if (stored is null) { ErrorMessage = Loc.T("photos_bad_file"); return; }
        PendingFile = stored; PendingThumb = PhotoImage.Load(_store.PathOf(stored), 360); Caption = ""; NewDate = DateTime.Today; IsAdding = true;
    }

    [RelayCommand]
    private Task SaveNewAsync() => RunAsync(async () =>
    {
        if (PendingFile is null) return;
        await _repo.SaveAsync(new Photo
        {
            File = PendingFile, Caption = string.IsNullOrWhiteSpace(Caption) ? null : Caption.Trim(), Date = DateOnly.FromDateTime(NewDate ?? DateTime.Today),
            OwnerKind = NewKind.Key, OwnerId = NewOwner?.Id, PlantId = NewOwner?.PlantId,
        });
        PendingFile = null; PendingThumb = null; IsAdding = false;
        await LoadAsync();
    }, busy: false);

    [RelayCommand]
    private void CancelNew() { _store.Delete(PendingFile); PendingFile = null; PendingThumb = null; IsAdding = false; }

    [RelayCommand] private void View(PhotoTile tile) { Viewing = tile; ViewImage = PhotoImage.Load(_store.PathOf(tile.Photo.File), 1600); }
    [RelayCommand] private void CloseView() { Viewing = null; ViewImage = null; }

    [RelayCommand]
    private Task DeleteViewingAsync() => RunAsync(async () =>
    {
        if (Viewing is not { } t || !_dialogs.Confirm(Loc.T("photos_delete_confirm"))) return;
        await _repo.DeleteAsync(t.Photo); _store.Delete(t.Photo.File); CloseView(); await LoadAsync();
    }, busy: false);
}
