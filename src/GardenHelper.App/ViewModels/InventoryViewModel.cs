using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record BedOption(int? Id, string Name) { public override string ToString() => Name; }
public sealed record InfoLine(string Text);

/// <summary>Everything you grow, in pots or in the ground, indoors, in the greenhouse or outdoors, with trees getting health, seasonal care and shade/root effects on nearby beds.</summary>
public sealed partial class InventoryViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly IDialogs _dialogs;
    private readonly INavigator _nav;
    private Garden? _garden;
    private List<InventoryPlant> _all = new();
    private List<GardenBed> _beds = new();

    public InventoryViewModel(Repository repo, KnowledgeBase kb, IDialogs dialogs, INavigator nav, ILogger<InventoryViewModel> log) : base(log)
    {
        _repo = repo; _kb = kb; _dialogs = dialogs; _nav = nav;
        var lang = Loc.Instance.Language;
        Species = new[] { new PlantOption(null, Loc.T("none")) }.Concat(kb.Plantable.OrderBy(p => p.DisplayName(lang)).Select(p => new PlantOption(p.Id, p.DisplayName(lang)))).ToList();
        Filters = new[] { "all", "Outdoor", "Greenhouse", "Indoor", "trees" }.Select(k => new AnimalOption(k, Loc.T("inv_f_" + k))).ToList();
        Locations = new[] { "Outdoor", "Greenhouse", "Indoor" }.Select(k => new AnimalOption(k, Loc.T("inv_loc_" + k))).ToList();
        Statuses = InventoryRules.Statuses.Select(k => new AnimalOption(k, Loc.T("inv_st_" + k))).ToList();
        LogKinds = InventoryRules.LogKinds.Select(k => new AnimalOption(k, Loc.T("inv_log_" + k))).ToList();
        TreeTypes = TreeRules.Types.Select(k => new AnimalOption(k, Loc.T("tree_type_" + k))).ToList();
        _filter = Filters[0]; _logKind = LogKinds[0];
    }

    public IReadOnlyList<PlantOption> Species { get; }
    public IReadOnlyList<AnimalOption> Filters { get; }
    public IReadOnlyList<AnimalOption> Locations { get; }
    public IReadOnlyList<AnimalOption> Statuses { get; }
    public IReadOnlyList<AnimalOption> LogKinds { get; }
    public IReadOnlyList<AnimalOption> TreeTypes { get; }
    public ObservableCollection<InventoryPlant> Items { get; } = new();
    public ObservableCollection<BedOption> TreeObjects { get; } = new();
    public ObservableCollection<InfoLine> Actions { get; } = new();
    public ObservableCollection<InfoLine> Effects { get; } = new();
    public ObservableCollection<AnimalLogRow> Logs { get; } = new();

    [ObservableProperty] private AnimalOption _filter;
    [ObservableProperty] private InventoryPlant? _selected;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private PlantOption? _editSpecies;
    [ObservableProperty] private string _editVariety = "";
    [ObservableProperty] private AnimalOption? _editLocation;
    [ObservableProperty] private string _editPlace = "";
    [ObservableProperty] private string _editQuantity = "1";
    [ObservableProperty] private AnimalOption? _editStatus;
    [ObservableProperty] private string _editInterval = "";
    [ObservableProperty] private string _editNotes = "";
    [ObservableProperty] private bool _editIsTree;
    [ObservableProperty] private AnimalOption? _editTreeType;
    [ObservableProperty] private DateTime? _editPlanted;
    [ObservableProperty] private string _editHeight = "";
    [ObservableProperty] private string _editCrown = "";
    [ObservableProperty] private string _editRoot = "";
    [ObservableProperty] private BedOption? _editBed;
    [ObservableProperty] private string _watering = "";
    [ObservableProperty] private string _treeInfo = "";
    [ObservableProperty] private AnimalOption _logKind;
    [ObservableProperty] private string _logText = "";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static double? Num(string s) => string.IsNullOrWhiteSpace(s) ? null : double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    private static string F(double? v) => v?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";

    partial void OnFilterChanged(AnimalOption value) => ApplyFilter();

    private void ApplyFilter(int? keep = null)
    {
        var id = keep ?? Selected?.Id;
        var q = _all.AsEnumerable();
        q = Filter.Key switch { "all" => q, "trees" => q.Where(p => p.IsTree), var k => q.Where(p => p.Location.ToString() == k && !p.IsTree) };
        Items.Clear(); foreach (var p in q.OrderBy(p => p.Name)) Items.Add(p);
        Selected = Items.FirstOrDefault(p => p.Id == id) ?? Items.FirstOrDefault();
    }

    partial void OnSelectedChanged(InventoryPlant? p)
    {
        HasSelection = p is not null;
        if (p is null) { Logs.Clear(); Actions.Clear(); Effects.Clear(); Watering = TreeInfo = ""; return; }
        EditName = p.Name; EditSpecies = Species.FirstOrDefault(s => s.Id == p.SpeciesId) ?? Species[0]; EditVariety = p.Variety ?? "";
        EditLocation = Locations.First(l => l.Key == p.Location.ToString()); EditPlace = p.Place ?? ""; EditQuantity = p.Quantity.ToString(CultureInfo.InvariantCulture);
        EditStatus = Statuses.FirstOrDefault(s => s.Key == p.Status) ?? Statuses[0]; EditInterval = p.WateringIntervalDays?.ToString(CultureInfo.InvariantCulture) ?? ""; EditNotes = p.Notes ?? "";
        EditIsTree = p.IsTree; EditTreeType = TreeTypes.FirstOrDefault(t => t.Key == p.TreeType) ?? TreeTypes[0]; EditPlanted = Dates.ToDateTime(p.PlantedOn);
        EditHeight = F(p.HeightCm); EditCrown = F(p.CrownRadiusCm); EditRoot = F(p.RootRadiusCm); EditBed = TreeObjects.FirstOrDefault(b => b.Id == p.BedId) ?? TreeObjects[0];
        Refresh();
    }

    private void Refresh()
    {
        if (Selected is not { } p) return;
        var over = InventoryRules.DaysOverdue(p, Today);
        Watering = over is null ? Loc.T("inv_no_schedule")
            : over > 0 ? Loc.F("inv_overdue", over) : over == 0 ? Loc.T("inv_due_today") : Loc.F("inv_due_in", -over);
        Logs.Clear();
        foreach (var l in p.Logs.OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).Take(40))
            Logs.Add(new AnimalLogRow(l.Id, l.Date.ToString("d", Loc.Instance.Culture), Loc.T("inv_log_" + l.Kind), l.Text, ""));
        Actions.Clear(); Effects.Clear(); TreeInfo = "";
        if (!p.IsTree) return;
        var age = TreeRules.AgeYears(p.PlantedOn, Today);
        TreeInfo = string.Join(" · ", new[] { age is { } a ? Loc.F("tree_age", a.ToString("0.#", Loc.Instance.Culture)) : null, p.HeightCm is { } h ? Loc.F("tree_height", (h / 100).ToString("0.#", Loc.Instance.Culture)) : null,
            Loc.F("tree_last_check", p.Logs.Where(l => l.Kind == "observation").Select(l => (DateOnly?)l.Date).Max()?.ToString("d", Loc.Instance.Culture) ?? Loc.T("tree_never")) }.Where(x => x is not null));
        var m = Today.Month; var next = m % 12 + 1;
        foreach (var k in TreeRules.SeasonalActions(p.TreeType, m, age)) Actions.Add(new InfoLine(Loc.T("tree_now") + ": " + Loc.T("tact_" + k)));
        foreach (var k in TreeRules.SeasonalActions(p.TreeType, next, age).Except(TreeRules.SeasonalActions(p.TreeType, m, age))) Actions.Add(new InfoLine(Loc.T("tree_next_month") + ": " + Loc.T("tact_" + k)));
        if (Actions.Count == 0) Actions.Add(new InfoLine(Loc.T("tree_no_jobs")));
        if (p.Status is "attention" or "recovering") Actions.Insert(0, new InfoLine(Loc.T("tree_health_warn")));
        if (p.BedId is { } id && _beds.FirstOrDefault(b => b.Id == id) is { } tb)
        {
            var crown = p.CrownRadiusCm ?? Math.Max(tb.Width, tb.Height) / 2;
            var others = _beds.Where(b => b.Id != id && b.Kind != "tree" && b.Kind != "path");
            foreach (var i in TreeRules.Interactions(tb.X + tb.Width / 2, tb.Y + tb.Height / 2, crown, p.RootRadiusCm, others))
                Effects.Add(new InfoLine(Loc.F(i.ShadePercent > 0 ? "tree_effect_shade" : "tree_effect_roots", i.BedName, i.ShadePercent.ToString("0"), (i.DistanceCm / 100).ToString("0.0", Loc.Instance.Culture))
                    + (i.ShadePercent > 0 && i.RootsReach ? " " + Loc.T("tree_effect_roots_too") : "")));
            if (Effects.Count == 0) Effects.Add(new InfoLine(Loc.T("tree_effect_none")));
        }
        else Effects.Add(new InfoLine(Loc.T("tree_need_plan")));
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        _beds = _garden is null ? new() : await _repo.ListAsync<GardenBed>(q => q.Where(b => b.GardenId == _garden.Id));
        TreeObjects.Clear(); TreeObjects.Add(new BedOption(null, Loc.T("none")));
        foreach (var b in _beds.Where(b => b.Kind == "tree").OrderBy(b => b.Name)) TreeObjects.Add(new BedOption(b.Id, b.Name));
        _all = await _repo.ListAsync<InventoryPlant>(q => q.Include(p => p.Logs));
        ApplyFilter();
    });

    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        var isTree = Filter.Key == "trees";
        var loc = Filter.Key is "Greenhouse" or "Indoor" ? Enum.Parse<PlantLocation>(Filter.Key) : PlantLocation.Outdoor;
        var p = await _repo.SaveAsync(new InventoryPlant { Name = Loc.T(isTree ? "tree_default_name" : "inv_default_name"), Location = loc, IsTree = isTree, TreeType = isTree ? "fruit" : null, PlantedOn = isTree ? Today : null });
        _all.Add(p); p.Logs = new(); ApplyFilter(p.Id);
        StatusMessage = Loc.T("inv_added");
    }, busy: false);

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (Selected is not { } p) return;
        if (string.IsNullOrWhiteSpace(EditName)) { ErrorMessage = Loc.T("inv_name_required"); return; }
        var (qty, interval, h, c, r) = (Num(EditQuantity), Num(EditInterval), Num(EditHeight), Num(EditCrown), Num(EditRoot));
        if (qty is null or double.NaN or < 0 or > 100000 || interval is double.NaN or < 1 or > 365 || h is double.NaN or < 0 or > 20000 || c is double.NaN or < 0 or > 5000 || r is double.NaN or < 0 or > 8000)
        { ErrorMessage = Loc.T("inv_bad_numbers"); return; }
        p.Name = EditName.Trim(); p.SpeciesId = EditSpecies?.Id; p.Variety = string.IsNullOrWhiteSpace(EditVariety) ? null : EditVariety.Trim();
        p.Location = Enum.Parse<PlantLocation>(EditLocation?.Key ?? "Outdoor"); p.Place = string.IsNullOrWhiteSpace(EditPlace) ? null : EditPlace.Trim();
        p.Quantity = (int)Math.Round(qty.Value); p.Status = EditStatus?.Key ?? "healthy"; p.WateringIntervalDays = interval is { } iv ? (int)Math.Round(iv) : null;
        p.Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes; p.IsTree = EditIsTree; p.TreeType = EditIsTree ? EditTreeType?.Key : null;
        p.PlantedOn = Dates.ToDateOnly(EditPlanted); p.HeightCm = h; p.CrownRadiusCm = c; p.RootRadiusCm = r; p.BedId = EditBed?.Id;
        var logs = p.Logs; p.Logs = new();
        await _repo.SaveAsync(p);
        p.Logs = logs;
        ApplyFilter(p.Id);
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is not { } p || !_dialogs.Confirm(Loc.F("inv_delete_confirm", p.Name))) return;
        await _repo.DeleteAsync(p);
        _all.Remove(p); ApplyFilter(-1);
    }, busy: false);

    private async Task AddLogCoreAsync(InventoryPlant p, string kind, string text)
    {
        var log = await _repo.SaveAsync(new InventoryLog { PlantId = p.Id, Kind = kind, Text = text });
        p.Logs.Add(log);
    }

    [RelayCommand]
    private Task WaterNowAsync() => RunAsync(async () =>
    {
        if (Selected is not { } p) return;
        p.LastWatered = Today;
        var logs = p.Logs; p.Logs = new(); await _repo.SaveAsync(p); p.Logs = logs;
        await AddLogCoreAsync(p, "watering", "");
        Refresh(); StatusMessage = Loc.T("inv_watered");
    }, busy: false);

    [RelayCommand]
    private Task AddLogAsync() => RunAsync(async () =>
    {
        if (Selected is not { } p) return;
        if (string.IsNullOrWhiteSpace(LogText) && LogKind.Key is "note" or "observation" or "treatment") { ErrorMessage = Loc.T("animal_need_text"); return; }
        await AddLogCoreAsync(p, LogKind.Key, LogText.Trim());
        LogText = ""; Refresh(); StatusMessage = Loc.T("animal_log_added");
    }, busy: false);

    /// <summary>Puts a tree object on the garden plan (a circle the size of the crown) and links it, ready to be dragged into place in the designer.</summary>
    [RelayCommand]
    private Task CreateOnPlanAsync() => RunAsync(async () =>
    {
        if (Selected is not { } p || !p.IsTree) return;
        _garden ??= await _repo.SaveAsync(new Garden { Name = Loc.T("site_default_name") });
        var d = Math.Max(100, (p.CrownRadiusCm ?? 150) * 2);
        var bed = await _repo.SaveAsync(new GardenBed { GardenId = _garden.Id, Name = p.Name, Kind = "tree", X = 200, Y = 200, Width = d, Height = d });
        _beds.Add(bed); TreeObjects.Add(new BedOption(bed.Id, bed.Name));
        EditBed = TreeObjects.Last(); StatusMessage = Loc.T("tree_on_plan");
        await SaveAsync();
    }, busy: false);

    [RelayCommand] private void ShowInDesigner() => _nav.GoTo<DesignerViewModel>();
}
