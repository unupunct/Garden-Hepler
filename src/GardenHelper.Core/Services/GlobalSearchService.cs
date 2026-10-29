using GardenHelper.Core.Domain;
using GardenHelper.Core.Knowledge;
using GardenHelper.Core.Library;

namespace GardenHelper.Core.Services;

public enum SearchKind { Plant, Book, Journal, Task, Seed }

public sealed record SearchResult(SearchKind Kind, string Title, string Subtitle, string Snippet, string TargetId, int? Ordinal = null);

/// <summary>One search box over plants, library passages and the user's own records.</summary>
public sealed class GlobalSearchService
{
    private readonly KnowledgeBase _kb;
    private readonly LibraryService _library;
    private readonly Repository _repo;

    public GlobalSearchService(KnowledgeBase kb, LibraryService library, Repository repo)
    {
        _kb = kb; _library = library; _repo = repo;
    }

    public async Task<List<SearchResult>> SearchAsync(string query, string lang, CancellationToken ct = default)
    {
        var results = new List<SearchResult>();
        if (string.IsNullOrWhiteSpace(query)) return results;

        foreach (var m in _kb.Search(query).Take(15))
            results.Add(new SearchResult(SearchKind.Plant, m.Plant.DisplayName(lang), m.Plant.Scientific, Trim(m.Plant.SummaryText(lang)), m.Plant.Id));

        var f = SubjectExtractor.Fold(query);
        var journal = await _repo.ListAsync<JournalEntry>(q => q.OrderByDescending(e => e.Date), ct);
        foreach (var e in journal.Where(e => SubjectExtractor.Fold(e.Title + " " + e.Text + " " + e.Tags).Contains(f)).Take(10))
            results.Add(new SearchResult(SearchKind.Journal, e.Title, e.Date.ToString("d"), Trim(e.Text), e.Id.ToString()));

        var tasks = await _repo.ListAsync<GardenTask>(null, ct);
        foreach (var t in tasks.Where(t => SubjectExtractor.Fold(t.Title + " " + t.Notes).Contains(f)).Take(10))
            results.Add(new SearchResult(SearchKind.Task, t.Title, t.DueDate.ToString("d"), t.Notes ?? "", t.Id.ToString()));

        var seeds = await _repo.ListAsync<SeedItem>(null, ct);
        foreach (var s in seeds.Where(s => SubjectExtractor.Fold(s.Name + " " + s.Variety + " " + s.Supplier).Contains(f)).Take(10))
            results.Add(new SearchResult(SearchKind.Seed, s.Name + (s.Variety is null ? "" : " – " + s.Variety), s.Supplier ?? "", s.Notes ?? "", s.Id.ToString()));

        foreach (var h in await _library.SearchAsync(query, null, 30, ct))
            results.Add(new SearchResult(SearchKind.Book, h.BookTitle, h.SectionTitle + (h.Author is null ? "" : " · " + h.Author), h.Snippet, h.BookId.ToString(), h.Ordinal));
        return results;
    }

    private static string Trim(string s) => s.Length <= 180 ? s : s[..180] + "…";
}
