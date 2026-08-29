using AudiobookManager.Database.Repositories;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Services;

public class MissingTagService : IMissingTagService
{
    private readonly IAudiobookRepository _audiobookRepository;

    // Keys must match the frontend's field selection values. Order here is the display order.
    private static readonly List<(string Key, string Label, bool IsCriticalByDefault, Func<DbAudiobook, bool> IsMissing)> Fields = new()
    {
        ("Authors", "Author", true, a => a.Authors.Count == 0 || a.Authors.All(p => string.IsNullOrWhiteSpace(p.Name))),
        ("BookName", "Book Name", true, a => string.IsNullOrWhiteSpace(a.BookName)),
        ("Year", "Year", true, a => a.Year == 0),
        ("Series", "Series", false, a => string.IsNullOrWhiteSpace(a.Series)),
        ("SeriesPart", "Series Part", false, a => string.IsNullOrWhiteSpace(a.SeriesPart)),
        ("Narrators", "Narrators", false, a => a.Narrators.Count == 0 || a.Narrators.All(p => string.IsNullOrWhiteSpace(p.Name))),
        ("Subtitle", "Subtitle", false, a => string.IsNullOrWhiteSpace(a.Subtitle)),
        ("Description", "Description", false, a => string.IsNullOrWhiteSpace(a.Description)),
        ("Genres", "Genres", false, a => a.Genres.Count == 0),
        ("Language", "Language", false, a => string.IsNullOrWhiteSpace(a.Language)),
        ("Cover", "Cover", false, a => string.IsNullOrWhiteSpace(a.CoverFilePath)),
    };

    public MissingTagService(IAudiobookRepository audiobookRepository)
    {
        _audiobookRepository = audiobookRepository;
    }

    public List<MissingTagField> GetTaggableFields() =>
        Fields.Select(f => new MissingTagField(f.Key, f.Label, f.IsCriticalByDefault)).ToList();

    public async Task<List<AudiobookMissingTags>> FindAudiobooksMissingTagsAsync(IEnumerable<string> fieldKeys)
    {
        var requestedFields = Fields.Where(f => fieldKeys.Contains(f.Key)).ToList();
        if (requestedFields.Count == 0)
        {
            return new List<AudiobookMissingTags>();
        }

        var audiobooks = await _audiobookRepository.GetAllWithIncludesAsync();

        var results = new List<AudiobookMissingTags>();
        foreach (var audiobook in audiobooks)
        {
            var missingFields = requestedFields.Where(f => f.IsMissing(audiobook)).Select(f => f.Key).ToList();
            if (missingFields.Count == 0)
            {
                continue;
            }

            results.Add(new AudiobookMissingTags(
                audiobook.Id,
                audiobook.BookName,
                audiobook.Authors.Select(p => p.Name).ToList(),
                missingFields));
        }

        return results;
    }
}
