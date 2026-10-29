using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Greenhouse;
using GardenHelper.Core.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace GardenHelper.App.ViewModels;

public sealed record CardRow(string Label, string Value);

/// <summary>What shows when you click a plant on the plan: when it went in, when it was last watered, how it is doing and what the sensors say.</summary>
public sealed record PlantDetailCard(string Title, string Subtitle, string Glyph, IReadOnlyList<CardRow> Rows, IReadOnlyList<CardRow> Sensors, string SensorsNote, int PlantingId, string PlantId, string BedName, int BedId = 0);

/// <summary>A small icon on a building or space for what lives there (chickens in the coop, goats in the barn…).</summary>
public sealed record FeatureBadge(int FeatureId, int GroupId, string Place, double X, double Y, string Glyph, string Count, string Tip);

public sealed partial class DesignerViewModel
{
    public ObservableCollection<FeatureBadge> Badges { get; } = new();
    [ObservableProperty] private PlantDetailCard? _card;
    [ObservableProperty] private bool _cardOpen;

    private static string SpeciesGlyph(string species) => species switch { "chicken" => "🐔", "duck" => "🦆", "rabbit" => "🐇", "goat" => "🐐", "pig" => "🐖", _ => "🐾" };

    private async Task LoadBadgesAsync()
    {
        Badges.Clear();
        if (_garden is null) return;
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Where(g => g.GardenId == _garden.Id && g.FeatureId != null).OrderBy(g => g.Id));
        foreach (var byFeature in groups.GroupBy(g => g.FeatureId!.Value))
        {
            // a building drawn as a plan object has no outline shape of its own: use the object
            var bed = Beds.FirstOrDefault(b => b.Bed.FeatureId == byFeature.Key && b.IsStructure);
            var shape = FeatureShapes.FirstOrDefault(f => f.Id == byFeature.Key);
            if (bed is null && shape is null) continue;
            var (cx, cy, place) = bed is not null ? (bed.X + bed.Width / 2, bed.Y + bed.Height / 2, bed.Name) : (shape!.LabelX, shape.LabelY, shape.Name);
            var n = byFeature.Count(); var i = 0;
            foreach (var g in byFeature)
            {
                // side by side, centred on the building
                var step = 90.0; var x = cx + (i - (n - 1) / 2.0) * step; i++;
                Badges.Add(new FeatureBadge(byFeature.Key, g.Id, place, x, cy, SpeciesGlyph(g.Species), g.Count.ToString(),
                    $"{g.Name} ×{g.Count}" + (string.IsNullOrWhiteSpace(g.Breed) ? "" : $" ({g.Breed})") + $"\n{Loc.T("animal_" + g.Species)} · {place}"));
            }
        }
    }


    private const double LayerMargin = 3;      // the plant layer sits inside the bed's 3 px border

    private static PlantMarker? MarkerAt(BedItem bed, Point local)
    {
        if (bed.Markers is not { Count: > 0 } markers) return null;
        PlantMarker? best = null; var bestD = double.MaxValue;
        foreach (var m in markers)
        {
            var dx = local.X - LayerMargin - m.X; var dy = local.Y - LayerMargin - m.Y;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= Math.Max(m.Size * 0.65, 8) && d < bestD) { best = m; bestD = d; }
        }
        return best;
    }

    /// <summary>The name of the plant under the pointer (or null), for the hover tip.</summary>
    public string? PlantTipAt(BedItem bed, Point local) => MarkerAt(bed, local)?.Tip;

    /// <summary>Clicking a plant icon opens its card.</summary>
    public async Task ProbePlantAsync(BedItem bed, Point local)
    {
        if (MarkerAt(bed, local) is not { PlantingId: > 0 } m) { CardOpen = false; return; }
        var p = bed.Bed.Plantings.FirstOrDefault(x => x.Id == m.PlantingId); if (p is null) return;
        try { Card = await BuildCardAsync(bed, p, m); CardOpen = true; }
        catch (Exception ex) { _logger.LogWarning(ex, "Plant card failed"); }
    }

    private static string Ago(DateOnly d, DateOnly today) => (today.DayNumber - d.DayNumber) switch
    {
        0 => Loc.T("card_today"), 1 => Loc.T("card_yesterday"), < 0 => Loc.F("card_in_days", d.DayNumber - today.DayNumber), var n => Loc.F("card_days_ago", n),
    };

    private async Task<PlantDetailCard> BuildCardAsync(BedItem bed, BedPlanting p, PlantMarker m)
    {
        var lang = Loc.Instance.Language; var culture = Loc.Instance.Culture; var today = DateOnly.FromDateTime(DateTime.Today);
        var plant = _kb.Get(p.PlantId);
        var rows = new List<CardRow>();
        rows.Add(new(Loc.T("card_planted"), p.PlantedDate is { } d ? $"{d.ToString("d MMM yyyy", culture)} · {Ago(d, today)}" : Loc.T("card_not_planted")));
        rows.Add(new(Loc.T("card_status"), Loc.T("status_" + p.Status)));
        rows.Add(new(Loc.T("card_count"), $"{p.Quantity}" + (string.IsNullOrWhiteSpace(p.Variety) ? "" : $" · {p.Variety}")));
        if (p.PlantedDate is { } pd && plant?.DaysToMaturity is { } dm)
            rows.Add(new(Loc.T("card_harvest"), $"{pd.AddDays(dm.Min).ToString("d MMM", culture)} – {pd.AddDays(dm.Max).ToString("d MMM", culture)}"));
        var water = (await _repo.ListAsync<JournalEntry>(q => q.Where(j => j.PlantId == p.PlantId && j.Tags.Contains("watering")).OrderByDescending(j => j.Date).Take(1))).FirstOrDefault();
        rows.Add(new(Loc.T("card_watered"), water is null ? Loc.T("card_not_logged") : $"{water.Date.ToString("d MMM", culture)} · {Ago(water.Date, today)}"));
        var note = (await _repo.ListAsync<JournalEntry>(q => q.Where(j => j.PlantId == p.PlantId && !j.Tags.Contains("watering")).OrderByDescending(j => j.Date).Take(1))).FirstOrDefault();
        if (note is not null) rows.Add(new(Loc.T("card_last_note"), $"{note.Date.ToString("d MMM", culture)} · {note.Title}"));
        if (!string.IsNullOrWhiteSpace(p.Notes)) rows.Add(new(Loc.T("card_notes"), p.Notes!));
        rows.Add(new(Loc.T("card_bed"), $"{bed.Name} · {bed.SizeText}"));

        var sensors = new List<CardRow>(); var sensorNote = Loc.T("card_no_sensors");
        if ((bed.Bed.SensorFeatureId ?? bed.Bed.FeatureId) is { } fid)
        {
            var latest = await _greenhouse.LatestAsync(fid);
            foreach (var met in GreenhouseCatalog.Metrics)
                if (latest.TryGetValue(met.Key, out var r))
                {
                    var age = (DateTime.UtcNow - r.AtUtc).TotalMinutes;
                    sensors.Add(new(Loc.T("gh_m_" + met.Key), $"{r.Value.ToString(met.Key == "light" ? "0" : "0.#", culture)} {met.Unit}" + (age > 15 ? $" ({Loc.T("card_old")})" : "")));
                }
            if (sensors.Count > 0) sensorNote = "";
        }
        var (_, _) = (m.Shape, m.Body);
        return new PlantDetailCard(plant?.DisplayName(lang) ?? p.PlantId, plant?.Scientific ?? "", "", rows, sensors, sensorNote, p.Id, p.PlantId, bed.Name, bed.Id);
    }

    [RelayCommand] private void CloseCard() => CardOpen = false;

    [ObservableProperty] private string _cardNote = "";
    [ObservableProperty] private string _cardQty = "";
    public IReadOnlyList<AnimalOption> HarvestUnits { get; } = new[] { new AnimalOption("kg", Loc.T("unit_kg")), new AnimalOption("pcs", Loc.T("unit_pcs")) };
    [ObservableProperty] private AnimalOption _cardUnit = new("kg", Loc.T("unit_kg"));

    /// <summary>Records what you did or saw about the plant in the open card: watering, harvest, a problem, or a note. It goes into the journal against that plant.</summary>
    [RelayCommand]
    private Task LogCardAsync(string kind) => RunAsync(async () =>
    {
        if (Card is not { } c) return;
        var name = _kb.Get(c.PlantId)?.DisplayName(Loc.Instance.Language) ?? c.PlantId;
        if (kind is "problem" or "note" && string.IsNullOrWhiteSpace(CardNote)) { ErrorMessage = Loc.T("card_need_note"); return; }
        var (title, tags) = kind switch
        {
            "water" => (Loc.F("card_water_title", name, c.BedName), "watering"),
            "harvest" => (Loc.F("card_harvest_title", name, c.BedName), "harvest"),
            "problem" => (Loc.F("card_problem_title", name, c.BedName), "pests"),
            _ => (Loc.F("card_note_title", name, c.BedName), "note"),
        };
        double? amount = null;
        if (kind == "harvest" && CardQty.Trim().Length > 0)
        {
            if (!double.TryParse(CardQty.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var q) || q <= 0 || q > 100000) { ErrorMessage = Loc.T("card_bad_qty"); return; }
            amount = q;
        }
        var amountText = amount is { } a ? $" ({a.ToString("0.##", Loc.Instance.Culture)} {Loc.T("unit_" + CardUnit.Key)})" : "";
        await _repo.SaveAsync(new JournalEntry { Title = title + amountText, Text = CardNote.Trim(), Tags = tags, PlantId = c.PlantId });
        if (kind == "harvest") await _repo.SaveAsync(new Harvest { PlantId = c.PlantId, BedId = c.BedId > 0 ? c.BedId : null, Quantity = amount, Unit = CardUnit.Key, Notes = string.IsNullOrWhiteSpace(CardNote) ? null : CardNote.Trim() });
        if (kind == "harvest" && Beds.SelectMany(x => x.Bed.Plantings).FirstOrDefault(x => x.Id == c.PlantingId) is { } bp && bp.Status < PlantingStatus.Harvesting)
        { bp.Status = PlantingStatus.Harvesting; await _repo.SaveAsync(bp); }
        CardNote = ""; CardQty = ""; CardOpen = false;
        StatusMessage = Loc.F(kind == "water" ? "card_water_done" : "card_logged", name);
    }, busy: false);

    /// <summary>Adds a picture of the plant in the open card to the photo timeline.</summary>
    [RelayCommand]
    private async Task AddCardPhotoAsync()
    {
        if (Card is not { } c) return;
        var file = _dialogs.PickFile(Loc.T("photos_filter")); if (file is null) return;
        var stored = await _photos.AddFileAsync(file);
        if (stored is null) { ErrorMessage = Loc.T("photos_bad_file"); return; }
        await _repo.SaveAsync(new Photo { File = stored, Caption = c.Title, OwnerKind = c.BedId > 0 ? "bed" : "plant", OwnerId = c.BedId > 0 ? c.BedId : null, PlantId = c.PlantId });
        CardOpen = false; StatusMessage = Loc.T("photos_saved");
    }

    [RelayCommand]
    private async Task AddAnimalPhotoAsync()
    {
        if (AnimalCard is not { } c) return;
        var file = _dialogs.PickFile(Loc.T("photos_filter")); if (file is null) return;
        var stored = await _photos.AddFileAsync(file);
        if (stored is null) { ErrorMessage = Loc.T("photos_bad_file"); return; }
        await _repo.SaveAsync(new Photo { File = stored, Caption = c.Title, OwnerKind = "animal", OwnerId = c.GroupId });
        AnimalCardOpen = false; StatusMessage = Loc.T("photos_saved");
    }

    // ---- animals: click the icon on a coop, pen or barn
    public sealed record AnimalCardInfo(int GroupId, string Title, IReadOnlyList<CardRow> Rows, string? Unit, string? UnitLabel);
    [ObservableProperty] private AnimalCardInfo? _animalCard;
    [ObservableProperty] private bool _animalCardOpen;
    [ObservableProperty] private string _animalQty = "";

    [RelayCommand]
    private async Task OpenBadgeAsync(FeatureBadge b)
    {
        try
        {
            var g = (await _repo.ListAsync<AnimalGroup>(q => q.Where(x => x.Id == b.GroupId).Include(x => x.Logs).Include(x => x.Rations))).FirstOrDefault(); if (g is null) return;
            var today = DateOnly.FromDateTime(DateTime.Today); var sum = AnimalRules.Summarize(g, today); var culture = Loc.Instance.Culture;
            var unit = g.Species switch { "chicken" or "duck" => "eggs", "goat" => "milk", _ => null };
            var rows = new List<CardRow> { new(Loc.T("card_a_count"), $"{g.Count}" + (string.IsNullOrWhiteSpace(g.Breed) ? "" : $" · {g.Breed}")) };
            if (unit is not null)
            {
                var todays = g.Logs.Where(l => l.Kind == "production" && l.Date == today && l.Quantity is not null).Sum(l => l.Quantity!.Value);
                rows.Add(new(Loc.T("card_a_today"), g.Logs.Any(l => l.Kind == "production" && l.Date == today) ? todays.ToString("0.#", culture) : Loc.T("card_not_logged")));
                if (sum.AveragePerDay is { } avg) rows.Add(new(Loc.T("card_a_avg"), avg.ToString("0.#", culture) + (sum.LayingPercent is { } lp ? $" ({lp:0}%)" : "")));
            }
            if (sum.WithdrawalUntil is { } w) rows.Add(new(Loc.T("card_a_withdrawal"), w.ToString("d MMM yyyy", culture)));
            rows.Add(new(Loc.T("card_a_feed"), $"{sum.FeedKgPerDay:0.#} kg · {sum.FeedCostPerDay:0.00}"));
            AnimalQty = ""; AnimalCard = new AnimalCardInfo(g.Id, $"{g.Name} · {b.Place}", rows, unit, unit is null ? null : Loc.T("today_unit_" + unit)); AnimalCardOpen = true;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Animal card failed"); }
    }

    [RelayCommand] private void CloseAnimalCard() => AnimalCardOpen = false;
    [RelayCommand] private void OpenAnimals() { AnimalCardOpen = false; _nav.GoTo<AnimalsViewModel>(); }

    [RelayCommand]
    private Task LogAnimalProductionAsync() => RunAsync(async () =>
    {
        if (AnimalCard is not { } c) return;
        if (!double.TryParse(AnimalQty.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var qty) || qty < 0) { ErrorMessage = Loc.T("animal_need_quantity"); return; }
        await _repo.SaveAsync(new AnimalLog { GroupId = c.GroupId, Date = DateOnly.FromDateTime(DateTime.Today), Kind = "production", Quantity = qty, Text = "" });
        AnimalCardOpen = false; StatusMessage = Loc.F("card_logged", c.Title);
    }, busy: false);
}
