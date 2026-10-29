using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Services;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Site;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed partial class WelcomeAnimal : ObservableObject
{
    public WelcomeAnimal(string species) { Species = species; Label = Loc.T("animal_" + species); }
    public string Species { get; }
    public string Label { get; }
    [ObservableProperty] private bool _keep;
    [ObservableProperty] private string _count = "5";
}

public sealed partial class WelcomeCrop : ObservableObject
{
    public WelcomeCrop(PlantRecord plant, string name) { Plant = plant; Name = name; }
    public PlantRecord Plant { get; }
    public string Name { get; }
    [ObservableProperty] private bool _grow;
}

/// <summary>First start: name and place the homestead, say what lives and grows there, and get a first to-do list. Everything can be changed later.</summary>
public sealed partial class WelcomeViewModel : PageViewModel
{
    private static readonly string[] CommonCrops = { "tomato", "pepper", "cucumber", "potato", "onion", "garlic", "carrot", "cabbage", "lettuce", "bean", "pea", "pumpkin", "courgette", "strawberry", "beetroot", "parsley" };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    private readonly INavigator _nav;
    private readonly ILogger _logger;

    public WelcomeViewModel(Repository repo, KnowledgeBase kb, INavigator nav, ILogger<WelcomeViewModel> log) : base(log)
    {
        _logger = log; _repo = repo; _kb = kb; _nav = nav;
        var lang = Loc.Instance.Language;
        foreach (var sp in AnimalCatalog.Species.Where(s => s != "other")) Animals.Add(new WelcomeAnimal(sp));
        foreach (var id in CommonCrops) if (_kb.Get(id) is { } p) Crops.Add(new WelcomeCrop(p, p.DisplayName(lang)));
        var y = DateTime.Today.Year;
        SpringFrost = new DateTime(y, 5, 10); AutumnFrost = new DateTime(y, 10, 5);
    }

    public ObservableCollection<WelcomeAnimal> Animals { get; } = new();
    public ObservableCollection<WelcomeCrop> Crops { get; } = new();
    public ObservableCollection<PlaceHit> Hits { get; } = new();
    [ObservableProperty] private string _gardenName = Loc.T("welcome_default_name");
    [ObservableProperty] private string _placeQuery = "";
    [ObservableProperty] private string _country = "", _region = "", _latitude = "", _longitude = "";
    [ObservableProperty] private DateTime? _springFrost, _autumnFrost;
    [ObservableProperty] private string _foundInfo = "";
    [ObservableProperty] private bool _hasHits;

    /// <summary>Searches for the place by name (online, only when the button is pressed).</summary>
    [RelayCommand]
    private Task FindPlaceAsync() => RunAsync(async () =>
    {
        Hits.Clear(); HasHits = false;
        if (string.IsNullOrWhiteSpace(PlaceQuery)) return;
        try { foreach (var h in await Geocoding.SearchAsync(Http, PlaceQuery, Loc.Instance.Language)) Hits.Add(h); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { ErrorMessage = Loc.T("welcome_offline"); return; }
        HasHits = Hits.Count > 0;
        if (!HasHits) FoundInfo = Loc.T("welcome_no_hits");
    }, busy: false);

    [RelayCommand]
    private void PickHit(PlaceHit hit)
    {
        Latitude = hit.Lat.ToString("0.#####", CultureInfo.InvariantCulture); Longitude = hit.Lon.ToString("0.#####", CultureInfo.InvariantCulture);
        Country = hit.Country ?? Country; Region = hit.Region ?? Region;
        FoundInfo = Loc.F("welcome_picked", hit.ToString()); HasHits = false;
    }

    private static double? Num(string s) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    [RelayCommand]
    private Task CreateAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(GardenName)) { ErrorMessage = Loc.T("welcome_need_name"); return; }
        double? lat = Num(Latitude), lon = Num(Longitude);
        if ((Latitude.Trim().Length > 0 && lat is not (>= -90 and <= 90)) || (Longitude.Trim().Length > 0 && lon is not (>= -180 and <= 180))) { ErrorMessage = Loc.T("welcome_bad_coords"); return; }
        var year = DateTime.Today.Year;
        var garden = await _repo.SaveAsync(new Garden
        {
            Name = GardenName.Trim(), Country = string.IsNullOrWhiteSpace(Country) ? null : Country.Trim(), Region = string.IsNullOrWhiteSpace(Region) ? null : Region.Trim(),
            Latitude = lat, Longitude = lon, IsActive = true,
            LastSpringFrost = SpringFrost is { } s ? new DateOnly(year, s.Month, s.Day) : null, FirstAutumnFrost = AutumnFrost is { } a ? new DateOnly(year, a.Month, a.Day) : null,
        });
        foreach (var an in Animals.Where(x => x.Keep))
            await _repo.SaveAsync(new AnimalGroup { GardenId = garden.Id, Name = an.Label, Species = an.Species, Count = int.TryParse(an.Count, out var n) && n > 0 ? n : 1 });

        var today = DateOnly.FromDateTime(DateTime.Today);
        // first to-dos: put the property on the map, then one sowing task per chosen crop, dated at its next sowing window
        await _repo.SaveAsync(new GardenTask { Title = Loc.T("welcome_task_map"), Activity = "General", DueDate = today });
        await _repo.SaveAsync(new GardenTask { Title = Loc.T("welcome_task_bed"), Activity = "General", DueDate = today.AddDays(2) });
        foreach (var c in Crops.Where(x => x.Grow))
        {
            var plan = PlantingCalculator.Calculate(c.Plant, garden, year);
            var next = plan.Windows.Where(w => w.End >= today).OrderBy(w => w.Start).FirstOrDefault() ?? PlantingCalculator.Calculate(c.Plant, garden, year + 1).Windows.OrderBy(w => w.Start).FirstOrDefault();
            if (next is null) continue;
            await _repo.SaveAsync(new GardenTask { Title = Loc.F("welcome_task_sow", Loc.T("act_" + next.Activity), c.Name), Activity = next.Activity.ToString(), DueDate = next.Start < today ? today : next.Start, PlantId = c.Plant.Id });
        }
        StatusMessage = Loc.T("welcome_done");
        _nav.GoTo<MapViewModel>();
    }, busy: false);
}
