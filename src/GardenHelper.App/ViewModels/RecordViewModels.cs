using System.Windows.Media;
using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record PlantOption(string? Id, string Name)
{
    public override string ToString() => Name;
}

internal static class Dates
{
    public static DateOnly? ToDateOnly(DateTime? d) => d is null ? null : DateOnly.FromDateTime(d.Value);
    public static DateTime? ToDateTime(DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);
}

public sealed partial class GardenViewModel : PageViewModel
{
    private readonly Repository _repo;
    private Garden _garden = new();

    public GardenViewModel(Repository repo, ILogger<GardenViewModel> log) : base(log) => _repo = repo;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _country;
    [ObservableProperty] private string? _region;
    [ObservableProperty] private string? _zone;
    [ObservableProperty] private DateTime? _lastFrost;
    [ObservableProperty] private DateTime? _firstFrost;
    [ObservableProperty] private string? _notes;

    public override async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            _garden = await _repo.ActiveGardenAsync() ?? new Garden { Name = "" };
            Name = _garden.Name; Country = _garden.Country; Region = _garden.Region; Zone = _garden.HardinessZone;
            LastFrost = Dates.ToDateTime(_garden.LastSpringFrost); FirstFrost = Dates.ToDateTime(_garden.FirstAutumnFrost);
            Notes = _garden.Notes;
        });
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Name)) { ErrorMessage = Loc.T("garden_name_required"); return; }
        _garden.Name = Name.Trim(); _garden.Country = Country; _garden.Region = Region; _garden.HardinessZone = Zone;
        _garden.LastSpringFrost = Dates.ToDateOnly(LastFrost); _garden.FirstAutumnFrost = Dates.ToDateOnly(FirstFrost);
        _garden.Notes = Notes; _garden.IsActive = true;
        _garden = await _repo.SaveAsync(_garden);
        StatusMessage = Loc.T("saved");
    });
}

public sealed partial class TasksViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;

    public TasksViewModel(Repository repo, KnowledgeBase kb, ILogger<TasksViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb;
        NewDue = DateTime.Today;
    }

    public static IReadOnlyList<string> ActivityKeys { get; } = new[] { "SowIndoors", "SowDirect", "Transplant", "Water", "Feed", "Prune", "Harvest", "Inspect", "Other" };
    public IReadOnlyList<ActivityOption> Activities { get; } = ActivityKeys.Select(k => new ActivityOption(k, Loc.T("task_" + k))).ToList();

    public ObservableCollection<TaskRow> Overdue { get; } = new();
    public ObservableCollection<TaskRow> Today { get; } = new();
    public ObservableCollection<TaskRow> Upcoming { get; } = new();
    public ObservableCollection<TaskRow> Done { get; } = new();

    [ObservableProperty] private string _newTitle = "";
    [ObservableProperty] private DateTime? _newDue;
    [ObservableProperty] private ActivityOption? _newActivity;
    [ObservableProperty] private bool _isEmpty;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        NewActivity ??= Activities[^1];
        var today = DateOnly.FromDateTime(DateTime.Today);
        var all = await _repo.ListAsync<GardenTask>(q => q.OrderBy(t => t.DueDate));
        TaskRow Row(GardenTask t) => new(t, t.PlantId is null ? null : _kb.Get(t.PlantId)?.DisplayName(Loc.Instance.Language), Loc.T("task_" + t.Activity));
        void Fill(ObservableCollection<TaskRow> c, IEnumerable<GardenTask> items) { c.Clear(); foreach (var t in items) c.Add(Row(t)); }
        Fill(Overdue, all.Where(t => !t.Done && t.DueDate < today));
        Fill(Today, all.Where(t => !t.Done && t.DueDate == today));
        Fill(Upcoming, all.Where(t => !t.Done && t.DueDate > today));
        Fill(Done, all.Where(t => t.Done).OrderByDescending(t => t.CompletedUtc).Take(15));
        IsEmpty = all.Count == 0;
    });

    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(NewTitle)) return;
        await _repo.SaveAsync(new GardenTask
        {
            Title = NewTitle.Trim(), DueDate = Dates.ToDateOnly(NewDue) ?? DateOnly.FromDateTime(DateTime.Today),
            Activity = NewActivity?.Key ?? "Other",
        });
        NewTitle = "";
        await LoadAsync();
    }, busy: false);

    [RelayCommand]
    private Task ToggleAsync(TaskRow row) => RunAsync(async () =>
    {
        row.Task.Done = !row.Task.Done;
        row.Task.CompletedUtc = row.Task.Done ? DateTime.UtcNow : null;
        await _repo.SaveAsync(row.Task);
        await LoadAsync();
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync(TaskRow row) => RunAsync(async () =>
    {
        await _repo.DeleteAsync(row.Task);
        await LoadAsync();
    }, busy: false);
}

public sealed record ActivityOption(string Key, string Label)
{
    public override string ToString() => Label;
}
public sealed record TaskRow(GardenTask Task, string? PlantName, string ActivityLabel);

public sealed partial class JournalViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly IDialogs _dialogs;
    private List<JournalEntry> _all = new();

    public JournalViewModel(Repository repo, KnowledgeBase kb, IDialogs dialogs, ILogger<JournalViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _dialogs = dialogs;
        Plants = new[] { new PlantOption(null, Loc.T("none")) }
            .Concat(kb.Plantable.OrderBy(p => p.DisplayName(Loc.Instance.Language)).Select(p => new PlantOption(p.Id, p.DisplayName(Loc.Instance.Language)))).ToList();
        NewEntry();
    }

    public IReadOnlyList<PlantOption> Plants { get; }
    public ObservableCollection<JournalEntry> Entries { get; } = new();

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private JournalEntry? _selected;
    [ObservableProperty] private DateTime? _editDate;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editText = "";
    [ObservableProperty] private string _editTags = "";
    [ObservableProperty] private PlantOption? _editPlant;
    [ObservableProperty] private bool _isExisting;

    /// <summary>Entry to select once loaded (set by global search).</summary>
    public int? PendingSelect { get; set; }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedChanged(JournalEntry? value)
    {
        if (value is null) return;
        EditDate = Dates.ToDateTime(value.Date); EditTitle = value.Title; EditText = value.Text; EditTags = value.Tags;
        EditPlant = Plants.FirstOrDefault(p => p.Id == value.PlantId) ?? Plants[0];
        IsExisting = true;
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _all = await _repo.ListAsync<JournalEntry>(q => q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id));
        ApplyFilter();
        if (PendingSelect is { } id) { Selected = _all.FirstOrDefault(e => e.Id == id); PendingSelect = null; }
    });

    private void ApplyFilter()
    {
        var f = Core.Library.SubjectExtractor.Fold(Filter ?? "");
        Entries.Clear();
        foreach (var e in _all.Where(e => f.Length == 0 || Core.Library.SubjectExtractor.Fold(e.Title + " " + e.Text + " " + e.Tags).Contains(f)))
            Entries.Add(e);
    }

    [RelayCommand]
    private void NewEntry()
    {
        Selected = null; IsExisting = false;
        EditDate = DateTime.Today; EditTitle = ""; EditText = ""; EditTags = ""; EditPlant = Plants[0];
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(EditTitle) && string.IsNullOrWhiteSpace(EditText)) return;
        var entry = Selected ?? new JournalEntry();
        entry.Date = Dates.ToDateOnly(EditDate) ?? DateOnly.FromDateTime(DateTime.Today);
        entry.Title = string.IsNullOrWhiteSpace(EditTitle) ? Loc.T("journal_untitled") : EditTitle.Trim();
        entry.Text = EditText; entry.Tags = EditTags.Trim(); entry.PlantId = EditPlant?.Id;
        var saved = await _repo.SaveAsync(entry);
        PendingSelect = saved.Id;
        await LoadAsync();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is null || !_dialogs.Confirm(Loc.T("confirm_delete"))) return;
        await _repo.DeleteAsync(Selected);
        NewEntry();
        await LoadAsync();
    }, busy: false);
}

public sealed partial class SeedsViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly IDialogs _dialogs;
    private readonly SeedPhotoStore _photos;
    private readonly List<string> _unsaved = new();      // pictures added to the form but not saved yet
    private readonly List<string> _replaced = new();     // saved pictures the form no longer uses; deleted once the seed is saved

    public SeedsViewModel(Repository repo, KnowledgeBase kb, IDialogs dialogs, SeedPhotoStore photos, ILogger<SeedsViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _dialogs = dialogs; _photos = photos;
        Plants = new[] { new PlantOption(null, Loc.T("none")) }
            .Concat(kb.Plantable.OrderBy(p => p.DisplayName(Loc.Instance.Language)).Select(p => new PlantOption(p.Id, p.DisplayName(Loc.Instance.Language)))).ToList();
        NewItem();
    }

    public IReadOnlyList<PlantOption> Plants { get; }
    public ObservableCollection<SeedRow> Items { get; } = new();

    [ObservableProperty] private SeedRow? _selected;
    [ObservableProperty] private PlantOption? _editPlant;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private string _editVariety = "";
    [ObservableProperty] private string _editSupplier = "";
    [ObservableProperty] private string _editLot = "";
    [ObservableProperty] private DateTime? _editPurchase;
    [ObservableProperty] private string _editQuantity = "";
    [ObservableProperty] private string _editRemaining = "";
    [ObservableProperty] private string _editUnit = "seeds";
    [ObservableProperty] private string _editStorage = "";
    [ObservableProperty] private DateTime? _editReview;
    [ObservableProperty] private string _editGermination = "";
    [ObservableProperty] private string _editCost = "";
    [ObservableProperty] private string _editNotes = "";
    [ObservableProperty] private bool _isExisting;
    [ObservableProperty] private string? _editFront;
    [ObservableProperty] private string? _editBack;
    [ObservableProperty] private ImageSource? _frontImage;
    [ObservableProperty] private ImageSource? _backImage;

    partial void OnEditFrontChanged(string? value) => FrontImage = PhotoImage.Load(_photos.PathOf(value), 700);
    partial void OnEditBackChanged(string? value) => BackImage = PhotoImage.Load(_photos.PathOf(value), 700);

    public string? PendingPlantId { get; set; }
    public int? PendingSelect { get; set; }

    partial void OnSelectedChanged(SeedRow? value)
    {
        DiscardUnsaved();
        if (value is null) return;
        var s = value.Seed;
        EditFront = s.FrontPhoto; EditBack = s.BackPhoto; _replaced.Clear();
        EditPlant = Plants.FirstOrDefault(p => p.Id == s.PlantId) ?? Plants[0];
        EditName = s.Name; EditVariety = s.Variety ?? ""; EditSupplier = s.Supplier ?? ""; EditLot = s.Lot ?? "";
        EditPurchase = Dates.ToDateTime(s.PurchaseDate); EditQuantity = Num(s.Quantity); EditRemaining = Num(s.QuantityRemaining);
        EditUnit = s.Unit; EditStorage = s.StorageLocation ?? ""; EditReview = Dates.ToDateTime(s.ReviewDate);
        EditGermination = s.GerminationPercent is { } g ? Num(g) : ""; EditCost = s.Cost is { } c ? Num(c) : ""; EditNotes = s.Notes ?? "";
        IsExisting = true;
    }

    partial void OnEditPlantChanged(PlantOption? value)
    {
        if (!IsExisting && value?.Id is not null && string.IsNullOrWhiteSpace(EditName)) EditName = value.Name;
    }

    private static string Num(decimal d) => d.ToString("0.##", CultureInfo.CurrentCulture);
    private static decimal? Parse(string s) => decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var d) ? d : null;

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var seeds = await _repo.ListAsync<SeedItem>(q => q.OrderBy(s => s.Name).ThenBy(s => s.Variety));
        var today = DateOnly.FromDateTime(DateTime.Today);
        Items.Clear();
        foreach (var s in seeds)
        {
            var warn = s.ReviewDate is { } d && d <= today ? Loc.T("seed_badge_review")
                : s.Quantity > 0 && s.QuantityRemaining <= s.Quantity * 0.2m ? Loc.T("seed_badge_low") : null;
            Items.Add(new SeedRow(s, warn, PhotoImage.Load(_photos.PathOf(s.FrontPhoto), 90)));
        }
        if (PendingPlantId is { } pid)
        {
            NewItem();
            EditPlant = Plants.FirstOrDefault(p => p.Id == pid) ?? Plants[0];
            PendingPlantId = null;
        }
        else if (PendingSelect is { } id) { Selected = Items.FirstOrDefault(i => i.Seed.Id == id); PendingSelect = null; }
    });

    [RelayCommand]
    private void NewItem()
    {
        Selected = null; DiscardUnsaved(); _replaced.Clear(); EditFront = EditBack = null; IsExisting = false;
        EditPlant = Plants[0]; EditName = EditVariety = EditSupplier = EditLot = EditStorage = EditNotes = EditGermination = EditCost = "";
        EditQuantity = EditRemaining = ""; EditUnit = "seeds"; EditPurchase = DateTime.Today; EditReview = null;
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(EditName)) { ErrorMessage = Loc.T("seed_name_required"); return; }
        var quantity = Parse(EditQuantity) ?? 0;
        var remaining = string.IsNullOrWhiteSpace(EditRemaining) ? quantity : Parse(EditRemaining) ?? quantity;
        if (quantity < 0 || remaining < 0 || remaining > quantity && quantity > 0) { ErrorMessage = Loc.T("seed_quantity_invalid"); return; }
        var germ = Parse(EditGermination);
        if (germ is < 0 or > 100) { ErrorMessage = Loc.T("seed_germination_invalid"); return; }
        var s = Selected?.Seed ?? new SeedItem();
        s.PlantId = EditPlant?.Id; s.Name = EditName.Trim(); s.Variety = Blank(EditVariety); s.Supplier = Blank(EditSupplier); s.Lot = Blank(EditLot);
        s.PurchaseDate = Dates.ToDateOnly(EditPurchase); s.Quantity = quantity; s.QuantityRemaining = remaining; s.Unit = string.IsNullOrWhiteSpace(EditUnit) ? "seeds" : EditUnit.Trim();
        s.FrontPhoto = EditFront; s.BackPhoto = EditBack;
        s.StorageLocation = Blank(EditStorage); s.ReviewDate = Dates.ToDateOnly(EditReview); s.GerminationPercent = germ; s.Cost = Parse(EditCost); s.Notes = Blank(EditNotes);
        var saved = await _repo.SaveAsync(s);
        _unsaved.Clear();                                            // these are kept now
        foreach (var old in _replaced) _photos.Delete(old);
        _replaced.Clear();
        PendingSelect = saved.Id;
        await LoadAsync();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    private void DiscardUnsaved() { foreach (var f in _unsaved) _photos.Delete(f); _unsaved.Clear(); }

    private const string ImageFilter = "Pictures|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp|*.*|*.*";

    private void SetPhoto(string side, string? name)
    {
        var old = side == "front" ? EditFront : EditBack;
        if (old is not null) { if (_unsaved.Remove(old)) _photos.Delete(old); else _replaced.Add(old); }
        if (name is not null) _unsaved.Add(name);
        if (side == "front") EditFront = name; else EditBack = name;
    }

    /// <summary>Choose a picture of the packet (side is "front" or "back").</summary>
    [RelayCommand]
    private Task AddPhotoAsync(string side) => RunAsync(async () =>
    {
        if (_dialogs.PickFile(ImageFilter) is not { } file) return;
        if (await _photos.AddFileAsync(file) is not { } name) { ErrorMessage = Loc.T("seed_photo_bad"); return; }
        SetPhoto(side, name);
    }, busy: false);

    /// <summary>Paste a picture from the clipboard: a copied image, a screenshot, or a copied picture file.</summary>
    [RelayCommand]
    private Task PastePhotoAsync(string side) => RunAsync(async () =>
    {
        string? name = null;
        if (System.Windows.Clipboard.ContainsFileDropList() && System.Windows.Clipboard.GetFileDropList().Cast<string>().FirstOrDefault() is { } f) name = await _photos.AddFileAsync(f);
        else if (System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } img)
        {
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using var ms = new MemoryStream(); enc.Save(ms);
            name = await _photos.AddBytesAsync(ms.ToArray());
        }
        if (name is null) { ErrorMessage = Loc.T("seed_photo_none_clip"); return; }
        SetPhoto(side, name);
    }, busy: false);

    [RelayCommand] private void RemovePhoto(string side) => SetPhoto(side, null);

    [RelayCommand]
    private void OpenPhoto(string side)
    {
        if (_photos.PathOf(side == "front" ? EditFront : EditBack) is { } p && File.Exists(p))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(p) { UseShellExecute = true });
    }

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is null || !_dialogs.Confirm(Loc.T("confirm_delete"))) return;
        _photos.Delete(Selected.Seed.FrontPhoto); _photos.Delete(Selected.Seed.BackPhoto);
        await _repo.DeleteAsync(Selected.Seed);
        NewItem();
        await LoadAsync();
    }, busy: false);

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record SeedRow(SeedItem Seed, string? Warning, ImageSource? Thumb)
{
    public override string ToString() => Title;
    public string Title => Seed.Name + (string.IsNullOrWhiteSpace(Seed.Variety) ? "" : " – " + Seed.Variety);
    public string Detail => $"{Seed.QuantityRemaining:0.##} / {Seed.Quantity:0.##} {Seed.Unit}" + (string.IsNullOrWhiteSpace(Seed.Supplier) ? "" : " · " + Seed.Supplier);
}
