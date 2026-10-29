using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record AnimalOption(string Key, string Label) { public override string ToString() => Label; }
public sealed record AnimalLogRow(int Id, string Date, string Kind, string Text, string Detail);

/// <summary>Animals on the property: groups with head count, housing, feed rations and prices, production, treatments and costs.</summary>
public sealed partial class AnimalsViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly IDialogs _dialogs;
    private Garden? _garden;

    public AnimalsViewModel(Repository repo, IDialogs dialogs, ILogger<AnimalsViewModel> log) : base(log)
    {
        _repo = repo; _dialogs = dialogs;
        Species = AnimalCatalog.Species.Select(k => new AnimalOption(k, Loc.T("animal_" + k))).ToList();
        LogKinds = AnimalCatalog.LogKinds.Select(k => new AnimalOption(k, Loc.T("alog_" + k))).ToList();
        LogKind = LogKinds[0];
    }

    public IReadOnlyList<AnimalOption> Species { get; }
    public IReadOnlyList<AnimalOption> LogKinds { get; }
    public ObservableCollection<AnimalGroup> Groups { get; } = new();
    public ObservableCollection<PlaceOption> Places { get; } = new();
    public ObservableCollection<FeedRation> Rations { get; } = new();
    public ObservableCollection<AnimalLogRow> Logs { get; } = new();

    [ObservableProperty] private AnimalGroup? _selected;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _editName = "";
    [ObservableProperty] private AnimalOption? _editSpecies;
    [ObservableProperty] private string _editBreed = "";
    [ObservableProperty] private string _editCount = "1";
    [ObservableProperty] private PlaceOption? _editPlace;
    [ObservableProperty] private string _editNotes = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _withdrawal = "";
    [ObservableProperty] private string _spent = "";
    [ObservableProperty] private string _rationName = "";
    [ObservableProperty] private string _rationGrams = "";
    [ObservableProperty] private string _rationPrice = "";
    [ObservableProperty] private AnimalOption? _logKind;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _logQuantity = "";
    [ObservableProperty] private string _logCost = "";
    [ObservableProperty] private string _logWithdrawDays = "";
    [ObservableProperty] private string _logDueDays = "";
    [ObservableProperty] private string _rationStock = "";
    [ObservableProperty] private string _totals = "";

    private static double? Num(string s) => double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    private static string Money(decimal v) => v.ToString("N2", Loc.Instance.Culture);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    partial void OnSelectedChanged(AnimalGroup? value)
    {
        HasSelection = value is not null;
        if (value is null) { Rations.Clear(); Logs.Clear(); Summary = Withdrawal = Spent = ""; return; }
        EditName = value.Name; EditSpecies = Species.FirstOrDefault(s => s.Key == value.Species) ?? Species[0]; EditBreed = value.Breed ?? "";
        EditCount = value.Count.ToString(CultureInfo.InvariantCulture); EditNotes = value.Notes ?? "";
        EditPlace = Places.FirstOrDefault(p => p.Id == value.FeatureId?.ToString()) ?? Places[0];
        Refresh();
    }

    /// <summary>Recomputes the figures and lists of the selected group from its stored data.</summary>
    private void Refresh()
    {
        if (Selected is not { } g) return;
        Rations.Clear(); foreach (var r in g.Rations.OrderBy(r => r.Name)) Rations.Add(r);
        Logs.Clear();
        foreach (var l in g.Logs.OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).Take(60))
        {
            var detail = string.Join(" · ", new[]
            {
                l.Quantity is { } q ? q.ToString("0.##", Loc.Instance.Culture) : null,
                l.Cost is { } c ? Money(c) : null,
                l.WithdrawalUntil is { } w ? Loc.F("animal_withdrawal_until", w.ToString("d", Loc.Instance.Culture)) : null,
                l.DueDate is { } due ? Loc.F(l.Kind == "breeding" ? "animal_expected" : "animal_next_due", due.ToString("d", Loc.Instance.Culture)) : null,
            }.Where(x => x is not null));
            Logs.Add(new AnimalLogRow(l.Id, l.Date.ToString("d", Loc.Instance.Culture), Loc.T("alog_" + l.Kind), l.Text, detail));
        }
        var s = AnimalRules.Summarize(g, Today);
        var lines = new List<string> { Loc.F("animal_feed_line", s.FeedKgPerDay.ToString("0.##", Loc.Instance.Culture), Money(s.FeedCostPerDay), Money(s.FeedCostPerMonth)) };
        if (s.AveragePerDay is { } avg) lines.Add(Loc.F("animal_prod_line", avg.ToString("0.#", Loc.Instance.Culture)) + (s.LayingPercent is { } pct ? " " + Loc.F("animal_laying", pct.ToString("0", Loc.Instance.Culture)) : ""));
        foreach (var r in g.Rations.Where(r => r.StockKg is not null))
            lines.Add(Loc.F("animal_stock_line", r.Name, AnimalRules.FeedDaysLeft(r, g.Count, Today) is { } d ? Math.Round(d).ToString("0", Loc.Instance.Culture) : "–", r.StockKg!.Value.ToString("0.#", Loc.Instance.Culture)));
        if (AnimalRules.LastWeight(g.Logs) is { } wt) lines.Add(Loc.F("animal_weight_line", wt.Kg.ToString("0.#", Loc.Instance.Culture), wt.Date.ToString("d", Loc.Instance.Culture)));
        Summary = string.Join("\n", lines);
        Withdrawal = s.WithdrawalUntil is { } w2 ? Loc.F("animal_withdrawal_warn", w2.ToString("D", Loc.Instance.Culture)) : "";
        Spent = Loc.F("animal_spent", Money(AnimalRules.SpentInMonth(g.Logs, Today.Year, Today.Month)));
    }

    private void RefreshTotals()
    {
        var all = Groups.ToList();
        var head = all.Sum(g => g.Count);
        var day = all.Sum(g => AnimalRules.FeedCostPerDay(g.Count, g.Rations));
        var month = all.Sum(g => AnimalRules.SpentInMonth(g.Logs, Today.Year, Today.Month));
        Totals = all.Count == 0 ? "" : Loc.F("animal_totals", all.Count, head, Money(day * 30), Money(month));
    }

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        Places.Clear(); Places.Add(new PlaceOption(null, Loc.T("none")));
        if (_garden is not null)
            foreach (var f in await _repo.ListAsync<PropertyFeature>(q => q.Where(x => x.GardenId == _garden.Id).OrderBy(x => x.Name))) Places.Add(new PlaceOption(f.Id.ToString(), f.Name));
        await ReloadAsync(Selected?.Id);
    });

    private async Task ReloadAsync(int? keep)
    {
        var list = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Rations).Include(g => g.Logs).OrderBy(g => g.Name));
        Groups.Clear(); foreach (var g in list) Groups.Add(g);
        RefreshTotals();
        Selected = Groups.FirstOrDefault(g => g.Id == keep) ?? Groups.FirstOrDefault();
    }

    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        _garden ??= await _repo.SaveAsync(new Garden { Name = Loc.T("site_default_name") });
        var g = await _repo.SaveAsync(new AnimalGroup { GardenId = _garden.Id, Name = $"{Loc.T("animal_default_name")} {Groups.Count + 1}" });
        await ReloadAsync(g.Id);
        StatusMessage = Loc.T("animal_added");
    }, busy: false);

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        if (Selected is not { } g) return;
        if (string.IsNullOrWhiteSpace(EditName)) { ErrorMessage = Loc.T("animal_name_required"); return; }
        if (Num(EditCount) is not { } n || n < 0 || n > 100000) { ErrorMessage = Loc.T("animal_bad_count"); return; }
        g.Name = EditName.Trim(); g.Species = EditSpecies?.Key ?? "other"; g.Breed = string.IsNullOrWhiteSpace(EditBreed) ? null : EditBreed.Trim();
        g.Count = (int)Math.Round(n); g.FeatureId = int.TryParse(EditPlace?.Id, out var fid) ? fid : null; g.Notes = string.IsNullOrWhiteSpace(EditNotes) ? null : EditNotes;
        await _repo.SaveAsync(new AnimalGroup { Id = g.Id, GardenId = g.GardenId, Name = g.Name, Species = g.Species, Breed = g.Breed, Count = g.Count, FeatureId = g.FeatureId, Notes = g.Notes });
        RefreshTotals(); Refresh();
        StatusMessage = Loc.T("saved");
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        if (Selected is not { } g || !_dialogs.Confirm(Loc.F("animal_delete_confirm", g.Name))) return;
        await _repo.DeleteAsync(g);
        await ReloadAsync(null);
    }, busy: false);

    [RelayCommand]
    private Task AddRationAsync() => RunAsync(async () =>
    {
        if (Selected is not { } g) return;
        if (string.IsNullOrWhiteSpace(RationName) || Num(RationGrams) is not { } gr || gr <= 0 || gr > 20000 || (!string.IsNullOrWhiteSpace(RationPrice) && Num(RationPrice) is null or < 0))
        { ErrorMessage = Loc.T("animal_bad_ration"); return; }
        var r = await _repo.SaveAsync(new FeedRation { GroupId = g.Id, Name = RationName.Trim(), GramsPerAnimalPerDay = gr, PricePerKg = (decimal)(Num(RationPrice) ?? 0), StockKg = Num(RationStock) is > 0 and var st ? st : null, StockDate = Num(RationStock) is > 0 ? Today : null });
        g.Rations.Add(r);
        RationName = RationGrams = RationPrice = RationStock = "";
        RefreshTotals(); Refresh();
    }, busy: false);

    /// <summary>Records how much feed is in store now (use the amount in the stock box) for every feed of the group that has a stock, or the first feed when none has.</summary>
    [RelayCommand]
    private Task RestockAsync(FeedRation r) => RunAsync(async () =>
    {
        if (Selected is not { } g) return;
        if (Num(RationStock) is not { } kg || kg < 0 || kg > 100000) { ErrorMessage = Loc.T("animal_bad_stock"); return; }
        r.StockKg = kg; r.StockDate = Today;
        await _repo.SaveAsync(r);
        RationStock = ""; RefreshTotals(); Refresh();
        StatusMessage = Loc.T("animal_restocked");
    }, busy: false);

    [RelayCommand]
    private Task RemoveRationAsync(FeedRation r) => RunAsync(async () =>
    {
        if (Selected is not { } g) return;
        await _repo.DeleteAsync(r);
        g.Rations.RemoveAll(x => x.Id == r.Id);
        RefreshTotals(); Refresh();
    }, busy: false);

    [RelayCommand]
    private Task AddLogAsync() => RunAsync(async () =>
    {
        if (Selected is not { } g || LogKind is not { } kind) return;
        double? qty = string.IsNullOrWhiteSpace(LogQuantity) ? null : Num(LogQuantity);
        double? cost = string.IsNullOrWhiteSpace(LogCost) ? null : Num(LogCost);
        double? days = string.IsNullOrWhiteSpace(LogWithdrawDays) ? null : Num(LogWithdrawDays);
        if ((!string.IsNullOrWhiteSpace(LogQuantity) && qty is null) || (!string.IsNullOrWhiteSpace(LogCost) && cost is null) || cost < 0 || (!string.IsNullOrWhiteSpace(LogWithdrawDays) && days is null or < 0))
        { ErrorMessage = Loc.T("animal_bad_numbers"); return; }
        if (kind.Key is "production" or "count" && qty is null) { ErrorMessage = Loc.T("animal_need_quantity"); return; }
        if (kind.Key is "note" or "treatment" && string.IsNullOrWhiteSpace(LogText)) { ErrorMessage = Loc.T("animal_need_text"); return; }
        if (kind.Key == "weight" && qty is not > 0) { ErrorMessage = Loc.T("animal_need_quantity"); return; }
        double? dueDays = string.IsNullOrWhiteSpace(LogDueDays) ? null : Num(LogDueDays);
        if (!string.IsNullOrWhiteSpace(LogDueDays) && dueDays is null or < 0 or > 3650) { ErrorMessage = Loc.T("animal_bad_numbers"); return; }
        DateOnly? dueDate = kind.Key switch
        {
            "breeding" => Today.AddDays((int)Math.Round(dueDays ?? AnimalRules.Gestation(g.Species))),
            "vaccination" or "worming" => Today.AddDays((int)Math.Round(dueDays ?? 90)),
            _ => null,
        };
        var log = await _repo.SaveAsync(new AnimalLog
        {
            GroupId = g.Id, Kind = kind.Key, Text = LogText.Trim(), Quantity = qty, Cost = cost is null ? null : (decimal)cost,
            WithdrawalUntil = kind.Key == "treatment" && days is > 0 ? Today.AddDays((int)Math.Round(days.Value)) : null, DueDate = dueDate,
        });
        g.Logs.Add(log);
        if (kind.Key == "count")
        {
            g.Count = AnimalRules.ApplyCountChange(g.Count, qty!.Value);
            await _repo.SaveAsync(new AnimalGroup { Id = g.Id, GardenId = g.GardenId, Name = g.Name, Species = g.Species, Breed = g.Breed, Count = g.Count, FeatureId = g.FeatureId, Notes = g.Notes });
            EditCount = g.Count.ToString(CultureInfo.InvariantCulture);
        }
        LogText = LogQuantity = LogCost = LogWithdrawDays = LogDueDays = "";
        RefreshTotals(); Refresh();
        StatusMessage = Loc.T("animal_log_added");
    }, busy: false);
}
