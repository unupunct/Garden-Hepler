using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GardenHelper.App.Infrastructure;
using GardenHelper.Core.Animals;
using GardenHelper.Core.Domain;
using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GardenHelper.App.ViewModels;

public sealed record HorizonOption(int Days, string Label) { public override string ToString() => Label; }
public sealed record UpcomingRow(string Date, string Glyph, string Title, string Detail, string Kind, int? RefId);
public sealed record UpcomingGroup(string Title, IReadOnlyList<UpcomingRow> Items);

/// <summary>One list of what is due: what needs attention now, then days, weeks and months ahead.</summary>
public sealed partial class ComingUpViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly AlertService _alerts;
    private readonly INavigator _nav;

    public ComingUpViewModel(Repository repo, AlertService alerts, INavigator nav, ILogger<ComingUpViewModel> log) : base(log)
    {
        _repo = repo; _alerts = alerts; _nav = nav;
        Horizons = new[] { new HorizonOption(7, Loc.T("up_7")), new HorizonOption(31, Loc.T("up_31")), new HorizonOption(90, Loc.T("up_90")) };
        _horizon = Horizons[1];
    }

    public IReadOnlyList<HorizonOption> Horizons { get; }
    public ObservableCollection<UpcomingGroup> Groups { get; } = new();
    public ObservableCollection<AlertRow> Now { get; } = new();
    [ObservableProperty] private HorizonOption _horizon;
    [ObservableProperty] private bool _isEmpty;

    partial void OnHorizonChanged(HorizonOption value) => _ = LoadAsync();

    public override Task LoadAsync() => RunAsync(async () =>
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tasks = await _repo.ListAsync<GardenTask>(q => q.Where(t => !t.Done));
        var groups = await _repo.ListAsync<AnimalGroup>(q => q.Include(g => g.Logs));
        var treatments = groups.SelectMany(g => g.Logs.Where(l => l.Kind == "treatment").Select(l => (l, g.Name)));
        var items = ComingUpPlanner.Build(tasks, treatments, today, Horizon.Days);
        Groups.Clear();
        foreach (var bucket in ComingUpPlanner.Buckets)
        {
            var rows = items.Where(i => ComingUpPlanner.BucketOf(i.Date, today) == bucket).Select(i => new UpcomingRow(
                i.Date.ToString("ddd d MMM", Loc.Instance.Culture), i.Kind == "task" ? "✔" : "⚠",
                i.Kind == "task" ? i.Title : Loc.F("up_withdrawal_end", i.Title), i.Detail ?? "", i.Kind, i.RefId)).ToList();
            if (rows.Count > 0) Groups.Add(new UpcomingGroup(Loc.T("up_bucket_" + bucket), rows));
        }
        Now.Clear(); foreach (var a in await _alerts.LoadAsync()) Now.Add(a);
        IsEmpty = Groups.Count == 0 && Now.Count == 0;
    });

    [RelayCommand]
    private void Open(UpcomingRow row)
    {
        if (row.Kind == "task") _nav.GoTo<TasksViewModel>();
        else _nav.GoTo<AnimalsViewModel>();
    }
}

public sealed record BudgetCategoryOption(string Key, string Label) { public override string ToString() => Label; }
public sealed record BudgetRow(int Id, string Date, string Category, string Text, string Amount, bool IsIncome);
public sealed record BudgetLine(string Category, string Amount);

/// <summary>Income and expenses by month. Animal costs come from the Animals tab, so nothing is typed twice.</summary>
public sealed partial class BudgetViewModel : PageViewModel
{
    private readonly Repository _repo;
    private readonly IDialogs _dialogs;
    private Garden? _garden;
    private int _year = DateTime.Today.Year, _month = DateTime.Today.Month;

    public BudgetViewModel(Repository repo, IDialogs dialogs, ILogger<BudgetViewModel> log) : base(log)
    {
        _repo = repo; _dialogs = dialogs;
        Categories = BudgetCatalog.Categories.Select(k => new BudgetCategoryOption(k, Loc.T("bcat_" + k))).ToList();
        _category = Categories[0];
    }

    public IReadOnlyList<BudgetCategoryOption> Categories { get; }
    public ObservableCollection<BudgetLine> ByCategory { get; } = new();
    public ObservableCollection<BudgetRow> Rows { get; } = new();
    [ObservableProperty] private string _monthLabel = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _newText = "";
    [ObservableProperty] private string _newAmount = "";
    [ObservableProperty] private bool _newIsIncome;
    [ObservableProperty] private BudgetCategoryOption _category;
    [ObservableProperty] private DateTime _newDate = DateTime.Today;

    private static string Money(decimal v) => v.ToString("N2", Loc.Instance.Culture);

    public override Task LoadAsync() => RunAsync(async () =>
    {
        _garden = await _repo.ActiveGardenAsync();
        var entries = await _repo.ListAsync<BudgetEntry>(q => q.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id));
        var logs = await _repo.ListAsync<AnimalLog>();
        var s = BudgetRules.Summarize(entries, logs, _year, _month);
        MonthLabel = new DateTime(_year, _month, 1).ToString("MMMM yyyy", Loc.Instance.Culture);
        Summary = Loc.F("budget_summary", Money(s.Income), Money(s.Expenses), Money(s.Net));
        ByCategory.Clear();
        foreach (var c in s.ExpenseByCategory) ByCategory.Add(new BudgetLine(Loc.T("bcat_" + c.Category), Money(c.Amount)));
        Rows.Clear();
        foreach (var e in entries.Where(e => e.Date.Year == _year && e.Date.Month == _month))
            Rows.Add(new BudgetRow(e.Id, e.Date.ToString("d", Loc.Instance.Culture), Loc.T("bcat_" + e.Category), e.Text, (e.IsIncome ? "+" : "−") + Money(e.Amount), e.IsIncome));
    });

    [RelayCommand] private Task PrevAsync() { (_year, _month) = BudgetRules.AddMonths(_year, _month, -1); return LoadAsync(); }
    [RelayCommand] private Task NextAsync() { (_year, _month) = BudgetRules.AddMonths(_year, _month, 1); return LoadAsync(); }

    [RelayCommand]
    private Task AddAsync() => RunAsync(async () =>
    {
        if (!decimal.TryParse(NewAmount.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0 || amount > 100_000_000)
        { ErrorMessage = Loc.T("budget_bad_amount"); return; }
        _garden ??= await _repo.SaveAsync(new Garden { Name = Loc.T("site_default_name") });
        var date = DateOnly.FromDateTime(NewDate);
        await _repo.SaveAsync(new BudgetEntry { GardenId = _garden.Id, Date = date, Category = Category.Key, Amount = amount, IsIncome = NewIsIncome, Text = NewText.Trim() });
        NewText = NewAmount = "";
        (_year, _month) = (date.Year, date.Month);       // show the month the entry went to
        await LoadAsync();
        StatusMessage = Loc.T("budget_added");
    }, busy: false);

    [RelayCommand]
    private Task DeleteAsync(BudgetRow row) => RunAsync(async () =>
    {
        if (!_dialogs.Confirm(Loc.F("budget_delete_confirm", row.Text.Length > 0 ? row.Text : row.Category))) return;
        var e = (await _repo.ListAsync<BudgetEntry>(q => q.Where(x => x.Id == row.Id))).FirstOrDefault();
        if (e is not null) await _repo.DeleteAsync(e);
        await LoadAsync();
    }, busy: false);
}
