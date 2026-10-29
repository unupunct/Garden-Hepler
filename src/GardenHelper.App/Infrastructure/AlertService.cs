using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Services;
using GardenHelper.Core.Site;

namespace GardenHelper.App.Infrastructure;

public sealed record AlertRow(GardenAlert Alert, string Message, string SeverityLabel, string Glyph)
{
    public string Why => Loc.F("alert_why", Alert.Why);
    public override string ToString() => SeverityLabel + ": " + Message;
}

/// <summary>Loads the garden, beds and seeds and runs the advisor, returning localized rows.</summary>
public sealed class AlertService
{
    private readonly Repository _repo;
    private readonly KnowledgeBase _kb;
    public AlertService(Repository repo, KnowledgeBase kb) { _repo = repo; _kb = kb; }

    public async Task<List<AlertRow>> LoadAsync(CancellationToken ct = default)
    {
        var garden = await _repo.ActiveGardenAsync(ct);
        if (garden is null) return new();
        var beds = await _repo.ListAsync<GardenBed>(q => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .Include(q.Where(b => b.GardenId == garden.Id), b => b.Plantings), ct);
        var seeds = await _repo.ListAsync<SeedItem>(null, ct);
        var alerts = ActionAdvisor.Evaluate(garden, beds, _kb, seeds, DateOnly.FromDateTime(DateTime.Today),
            ClimateSummary.FromJson(garden.ClimateJson), Loc.Instance.Language);
        return alerts.Select(Row).ToList();
    }

    public static AlertRow Row(GardenAlert a) => new(a, Loc.F(a.Key, a.Args.Cast<object>().ToArray()),
        Loc.T("sev_" + a.Severity), a.Severity switch { AlertSeverity.Urgent => "⚠", AlertSeverity.Soon => "◔", _ => "ℹ" });
}
