using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record SeasonTile(string Value, string Label);
public sealed record SeasonSection(string Title, IReadOnlyList<string> Lines);

/// <summary>A look back at one year: what grew, what was harvested, what went wrong, money, animals, work done, and what to plant where next year.</summary>
public sealed partial class SeasonViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;

    private readonly SeedPhotoStore _store;
    public SeasonViewModel(Repository repo, KnowledgeBase kb, SeedPhotoStore store, ILogger<SeasonViewModel> log) : base(log) { _repo = repo; _kb = kb; _store = store; }

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<SeasonTile> Tiles { get; } = new();
    public ObservableCollection<SeasonSection> Sections { get; } = new();
    public ObservableCollection<PhotoTile> Gallery { get; } = new();
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private bool _isEmpty;
    private bool _loading;

    /// <summary>The report as plain text blocks, for printing.</summary>
    public (string Title, IReadOnlyList<SeasonTile> Tiles, IReadOnlyList<SeasonSection> Sections) Report => (Title, Tiles.ToList(), Sections.ToList());

    partial void OnSelectedYearChanged(int value) { if (!_loading) _ = LoadAsync(); }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var garden = await _repo.ActiveGardenAsync();
        var beds = await _repo.ListAsync<GardenBed>(q => q.Include(b => b.Plantings));
        var journal = await _repo.ListAsync<JournalEntry>(null);
        var budget = await _repo.ListAsync<BudgetEntry>(null);
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs).Include(g => g.Rations));
        var tasks = await _repo.ListAsync<GardenTask>(null);
        var photos = await _repo.ListAsync<Photo>(null);
        var harvests = await _repo.ListAsync<Harvest>(null);

        _loading = true;
        var years = SeasonReviewBuilder.YearsWithData(beds, journal, budget, groups, DateTime.Today.Year, harvests);
        Years.Clear(); foreach (var y in years) Years.Add(y);
        if (!Years.Contains(SelectedYear)) SelectedYear = Years[0];
        _loading = false;

        var r = SeasonReviewBuilder.Build(SelectedYear, beds, _kb, journal, budget, groups, tasks, photos, DateOnly.FromDateTime(DateTime.Today), harvests);
        Fill(r, garden?.Name);
        Gallery.Clear();
        var culture = Loc.Instance.Culture;
        foreach (var ph in photos.Where(x => x.Date.Year == SelectedYear).OrderBy(x => x.Date).Take(12))
            Gallery.Add(new PhotoTile(ph, PhotoImage.Load(_store.PathOf(ph.File), 240), "", ph.Date.ToString("d MMM", culture), ph.Caption ?? ""));
    });

    private string Name(string plantId) => _kb.Get(plantId)?.DisplayName(Loc.Instance.Language) ?? plantId;
    private static string Money(decimal v) => v.ToString("N0", Loc.Instance.Culture);

    private void Fill(SeasonReview r, string? gardenName)
    {
        var culture = Loc.Instance.Culture;
        Title = Loc.F("season_title", r.Year) + (string.IsNullOrWhiteSpace(gardenName) ? "" : " · " + gardenName);
        Tiles.Clear(); Sections.Clear();
        Tiles.Add(new(r.Plantings.ToString(), Loc.T("season_t_plantings")));
        Tiles.Add(new(r.HarvestCount.ToString(), Loc.T("season_t_harvests")));
        Tiles.Add(new(Money(r.Net), Loc.T("season_t_net")));
        Tiles.Add(new(r.TasksDone.ToString(), Loc.T("season_t_tasks")));
        IsEmpty = r.Plantings == 0 && r.Yields.Count == 0 && r.Income == 0 && r.Expenses == 0 && r.Animals.All(a => a.Production == 0) && r.Harvests.Count == 0 && r.TasksDone == 0;

        if (r.Crops.Count > 0)
            Sections.Add(new(Loc.T("season_s_grew"), r.Crops.Take(12).Select(c => Loc.F("season_crop_line", Name(c.PlantId), c.Plants, string.Join(", ", c.Beds.Take(3)))).ToList()));

        if (r.Yields.Count > 0)
            Sections.Add(new(Loc.T("season_s_yield"), r.Yields.Take(14).Select(y => Loc.F(y.BestBed is null ? "season_yield_line" : "season_yield_best", Name(y.PlantId), y.Total.ToString("N1", culture) + " " + Loc.T("unit_" + y.Unit), y.Harvests, y.BestBed ?? "", y.BestBedTotal.ToString("N1", culture))).ToList()));

        var hp = new List<string>();
        foreach (var h in r.Harvests.OrderByDescending(x => x.Value).Take(8)) hp.Add(Loc.F("season_harvest_line", Name(h.Key), h.Value));
        foreach (var p in r.Problems.OrderByDescending(x => x.Value).Take(8)) hp.Add(Loc.F("season_problem_line", Name(p.Key), p.Value));
        if (hp.Count > 0) Sections.Add(new(Loc.T("season_s_harvest"), hp));

        var money = new List<string> { Loc.F("season_income", Money(r.Income)), Loc.F("season_expenses", Money(r.Expenses)), Loc.F("season_net", Money(r.Net)) };
        money.AddRange(r.ExpenseByCategory.Take(6).Select(c => Loc.F("season_cat_line", c.Category == BudgetCatalog.AnimalsKey ? Loc.T("bcat_animals") : Loc.T("bcat_" + c.Category), Money(c.Amount))));
        if (r.Income > 0 || r.Expenses > 0) Sections.Add(new(Loc.T("season_s_money"), money));

        var animals = r.Animals.Where(a => a.Production > 0 || a.Treatments > 0 || a.Costs > 0)
            .Select(a => Loc.F("season_animal_line", a.Name, a.Production > 0 ? $"{a.Production.ToString("N0", culture)} {Loc.T(a.Species == "goat" ? "season_litres" : "season_eggs")}" : "–", a.PerDay is { } d ? d.ToString("0.#", culture) : "–", a.Treatments, Money(a.Costs))).ToList();
        if (animals.Count > 0) Sections.Add(new(Loc.T("season_s_animals"), animals));

        var cost = new List<string>();
        if (r.KgHarvested > 0 && r.InputsCost > 0) cost.Add(Loc.F("season_cost_kg", Money(r.InputsCost), r.KgHarvested.ToString("N0", culture), (r.InputsCost / (decimal)r.KgHarvested).ToString("N2", culture)));
        foreach (var e in r.Economy)
            cost.Add(e.CostPerUnit is { } cpu ? Loc.F("season_cost_unit", e.Name, Money(e.Total), e.Production.ToString("N0", culture), Loc.T(e.Species == "goat" ? "season_litre_one" : "season_egg_one"), cpu.ToString("N2", culture))
                                              : Loc.F("season_cost_total", e.Name, Money(e.Total)));
        if (r.Economy.Count > 0 && r.SalesIncome > 0) cost.Add(Loc.F("season_cost_vs_sales", Money(r.Economy.Sum(x => x.Total)), Money(r.SalesIncome)));
        if (cost.Count > 0) { cost.Add(Loc.T("season_cost_note")); Sections.Add(new(Loc.T("season_s_cost"), cost)); }

        Sections.Add(new(Loc.T("season_s_work"), new[] { Loc.F("season_tasks_line", r.TasksDone, r.TasksLate), Loc.F("season_waterings_line", r.Waterings), Loc.F("season_photos_line", r.Photos) }));

        if (r.Rotation.Count > 0)
            Sections.Add(new(Loc.F("season_s_rotation", r.Year + 1), r.Rotation.Select(b => Loc.F("season_rot_line", b.BedName, Loc.T("season_g_" + b.ThisYear), Loc.T("season_g_" + b.Next), string.Join(", ", b.Suggestions.Select(Name)))).ToList()));
    }
}
