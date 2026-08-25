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

        return clusters.Select(cluster => new SimilarValueGroup
        {
            Candidates = cluster.Select(name => new SimilarValueCandidate
            {
                Value = name,
                AudiobookIds = authors
                    .Where(a => a.Name == name)
                    .SelectMany(a => a.BooksAuthored.Select(b => b.Id))
                    .Distinct()
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
                AudiobookIds = seriesMap[value]
            }).ToList()
        }).ToList();
    }

    public async Task<(int Processed, int Succeeded, int Failed)> AlignAuthorsAsync(
        List<string> sourceNames,
        string targetName,
        Func<int, int, int, int, Task> progressAction)
    {
        var sourceSet = new HashSet<string>(sourceNames, StringComparer.Ordinal);
        var books = await _audiobookRepository.GetBooksByAuthorNamesAsync(sourceNames);

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = books.Count;

        foreach (var dbBook in books)
        {
            processed++;
            try
            {
                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;

                var newAuthors = new List<Person>();
                var targetAdded = false;
                foreach (var author in domain.Authors)
                {
                    if (sourceSet.Contains(author.Name))
                    {
                        if (!targetAdded && newAuthors.All(a => a.Name != targetName))
                        {
                            newAuthors.Add(new Person(targetName));
                            targetAdded = true;
                        }
                    }
                    else if (newAuthors.All(a => a.Name != author.Name))
                    {
                        newAuthors.Add(author);
                    }
                }
                domain.Authors = newAuthors;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to align author for audiobook {AudiobookId}", dbBook.Id);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        return (processed, succeeded, failed);
    }

    public async Task<(int Processed, int Succeeded, int Failed)> AlignSeriesAsync(
        List<string> sourceValues,
        string targetValue,
        Func<int, int, int, int, Task> progressAction)
    {
        var books = await _audiobookRepository.GetBooksBySeriesValuesAsync(sourceValues);

        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var total = books.Count;

        foreach (var dbBook in books)
        {
            processed++;
            try
            {
                var domain = AudiobookService.FromDb(dbBook);
                domain.Id = dbBook.Id;
                domain.Series = targetValue;

                await _audiobookService.UpdateAudiobook(dbBook.Id, domain);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to align series for audiobook {AudiobookId}", dbBook.Id);
                failed++;
            }

            await progressAction(processed, total, succeeded, failed);
        }

        return (processed, succeeded, failed);
    }
}
