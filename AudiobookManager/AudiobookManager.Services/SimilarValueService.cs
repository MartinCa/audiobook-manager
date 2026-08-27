using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services.Similarity;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Services;

public class SimilarValueService : ISimilarValueService
{
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IPersonRepository _personRepository;
    private readonly IAudiobookService _audiobookService;
    private readonly AudiobookManagerSettings _settings;
    private readonly ILogger<SimilarValueService> _logger;

    public SimilarValueService(
        IAudiobookRepository audiobookRepository,
        IPersonRepository personRepository,
        IAudiobookService audiobookService,
        IOptions<AudiobookManagerSettings> settings,
        ILogger<SimilarValueService> logger)
    {
        _audiobookRepository = audiobookRepository;
        _personRepository = personRepository;
        _audiobookService = audiobookService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<List<SimilarValueGroup>> DetectSimilarAuthorsAsync()
    {
        var authors = await _personRepository.GetAllAuthorsAsync();
        var names = authors.Select(a => a.Name).Distinct().ToList();

        var clusters = SimilarityGrouper.GroupSimilarValues(names, _settings);
        var authorsByName = authors.ToLookup(a => a.Name);

        return clusters.Select(cluster => new SimilarValueGroup
        {
            Candidates = cluster.Select(name => new SimilarValueCandidate
            {
                Value = name,
                Books = authorsByName[name]
                    .SelectMany(a => a.BooksAuthored.Select(b => new SimilarValueBook { Id = b.Id, BookName = b.BookName }))
                    .DistinctBy(b => b.Id)
                    .ToList()
            }).ToList()
        }).ToList();
    }

    public async Task<List<SimilarValueGroup>> DetectSimilarSeriesAsync()
    {
        var seriesMap = await _audiobookRepository.GetDistinctSeriesAsync();
        var values = seriesMap.Keys.ToList();

        var clusters = SimilarityGrouper.GroupSimilarValues(values, _settings);

        return clusters.Select(cluster => new SimilarValueGroup
        {
            Candidates = cluster.Select(value => new SimilarValueCandidate
            {
                Value = value,
                Books = seriesMap[value].Select(b => new SimilarValueBook { Id = b.Id, BookName = b.BookName }).ToList()
            }).ToList()
        }).ToList();
    }

    public async Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction)
    {
        // sourceNames is the full candidate group, which includes targetName itself. Excluding it
        // before querying avoids re-processing books that already only have the target author name.
        var namesToAlign = sourceNames.Where(n => n != targetName).ToList();
        if (namesToAlign.Count == 0)
        {
            return (0, 0, 0);
        }

        var sourceSet = new HashSet<string>(namesToAlign, StringComparer.Ordinal);
        var books = await _audiobookRepository.GetBooksByAuthorNamesAsync(namesToAlign);

        return await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;

                // Track whether the target name is already present in newAuthors via a bool
                // that is kept in sync on every insertion (including when the target is already
                // a literal author on the book, not just when it's added to replace a source
                // name) so a source name encountered later never re-adds it as a duplicate.
                var newAuthors = new List<Person>();
                var targetPresent = false;
                foreach (var author in domain.Authors)
                {
                    if (sourceSet.Contains(author.Name))
                    {
                        if (!targetPresent)
                        {
                            newAuthors.Add(new Person(targetName));
                            targetPresent = true;
                        }
                    }
                    else if (newAuthors.All(a => a.Name != author.Name))
                    {
                        newAuthors.Add(author);
                        if (author.Name == targetName)
                        {
                            targetPresent = true;
                        }
                    }
                }
                domain.Authors = newAuthors;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
            },
            _logger,
            dbBook => $"Failed to align author for audiobook {dbBook.Id}",
            progressAction);
    }

    public async Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction)
    {
        // sourceValues is the full candidate group, which includes targetValue itself. Excluding it
        // before querying avoids re-processing books whose series already matches the target.
        var valuesToAlign = sourceValues.Where(v => v != targetValue).ToList();
        if (valuesToAlign.Count == 0)
        {
            return (0, 0, 0);
        }

        var books = await _audiobookRepository.GetBooksBySeriesValuesAsync(valuesToAlign);

        return await BulkOperationRunner.RunAsync(
            books,
            async dbBook =>
            {
                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;
                domain.Series = targetValue;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
            },
            _logger,
            dbBook => $"Failed to align series for audiobook {dbBook.Id}",
            progressAction);
    }
}
