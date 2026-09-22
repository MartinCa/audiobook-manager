using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class SimilarValuesController : ControllerBase
{
    private static readonly SemaphoreSlim _alignLock = new(1, 1);

    public const string OperationKey = "similar-value-align";

    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOperationStatusRegistry _statusRegistry;
    private readonly ISimilarValueService _similarValueService;
    private readonly IPersonRepository _personRepository;
    private readonly IAudiobookRepository _audiobookRepository;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly ILogger<SimilarValuesController> _logger;

    public SimilarValuesController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IOperationStatusRegistry statusRegistry,
        ISimilarValueService similarValueService,
        IPersonRepository personRepository,
        IAudiobookRepository audiobookRepository,
        IHostApplicationLifetime appLifetime,
        ILogger<SimilarValuesController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _statusRegistry = statusRegistry;
        _similarValueService = similarValueService;
        _personRepository = personRepository;
        _audiobookRepository = audiobookRepository;
        _appLifetime = appLifetime;
        _logger = logger;
    }

    [HttpGet("similar-authors")]
    public async Task<ActionResult<SimilarValueGroupsPageDto>> GetSimilarAuthors(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        var error = ValidatePageSelection(page, pageSize);
        if (error != null)
        {
            return error;
        }

        var groups = await _similarValueService.DetectSimilarAuthorsAsync(
            skip: (int)((long)page * pageSize), take: pageSize);
        return Ok(new SimilarValueGroupsPageDto(ToDto(groups.Items), groups.Total));
    }

    [HttpGet("similar-series")]
    public async Task<ActionResult<SimilarValueGroupsPageDto>> GetSimilarSeries(
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = PagingLimits.DefaultPageSize)
    {
        var error = ValidatePageSelection(page, pageSize);
        if (error != null)
        {
            return error;
        }

        var groups = await _similarValueService.DetectSimilarSeriesAsync(
            skip: (int)((long)page * pageSize), take: pageSize);
        return Ok(new SimilarValueGroupsPageDto(ToDto(groups.Items), groups.Total));
    }

    /// <summary>
    /// The bounded, server-side author/narrator/series type-ahead: existing names matching the
    /// typed query, accent-insensitively, capped at <paramref name="limit"/>. This is what the
    /// author, narrator and series entry fields feed their suggestion dropdowns from - replacing
    /// the unbounded flat name lists, whose size grew with the library. Narrator is supported
    /// where the narrator entry field uses it; every other valueType is refused. The query
    /// travels in the query string, never a path segment (see the note on SeriesController for
    /// why a free-text tag name cannot be addressed in a path).
    /// </summary>
    [HttpGet("autocomplete")]
    public async Task<ActionResult<List<string>>> GetAutocomplete(
        [FromQuery] string valueType,
        [FromQuery] string query,
        [FromQuery] int limit = 8)
    {
        if (valueType != "author" && valueType != "narrator" && valueType != "series")
        {
            return this.InvalidRequest("valueType must be 'author', 'narrator' or 'series'.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return this.InvalidRequest("query is required.");
        }

        if (limit < 1 || limit > 50)
        {
            return this.InvalidRequest("limit must be between 1 and 50.");
        }

        return valueType switch
        {
            "author" => (await _personRepository.SearchAuthorNamesAsync(query, limit))
                .Select(a => a.Name)
                .ToList(),
            "narrator" => (await _personRepository.SearchNarratorNamesAsync(query, limit))
                .Select(a => a.Name)
                .ToList(),
            _ => await _audiobookRepository.SearchSeriesValuesAsync(query, limit),
        };
    }

    /// <summary>
    /// The bounded, server-side classification of a single typed author/narrator/series entry
    /// for the entry fields' exact/new/similar indicators. One input value in, a capped result
    /// out - this is the bounded alternative to a client pulling the whole name list to classify
    /// locally. Narrator is supported where the narrator entry field uses it; every other
    /// valueType is refused. The series name travels in the query string, never a path segment
    /// (see the note on SeriesController for why a free-text m4b tag cannot be addressed in a
    /// path).
    /// </summary>
    [HttpGet("entry-status")]
    public async Task<ActionResult<EntryStatusDto>> GetEntryStatus(
        [FromQuery] string valueType,
        [FromQuery] string value,
        [FromQuery] int limit = 3)
    {
        if (valueType != "author" && valueType != "narrator" && valueType != "series")
        {
            return this.InvalidRequest("valueType must be 'author', 'narrator' or 'series'.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return this.InvalidRequest("value is required.");
        }

        if (limit < 1 || limit > 10)
        {
            return this.InvalidRequest("limit must be between 1 and 10.");
        }

        var kind = valueType switch
        {
            "author" => Domain.EntryValueKind.Author,
            "narrator" => Domain.EntryValueKind.Narrator,
            _ => Domain.EntryValueKind.Series,
        };
        var status = await _similarValueService.GetEntryStatusAsync(kind, value, limit);

        return new EntryStatusDto(
            status.Value,
            status.Kind switch
            {
                Domain.EntryValueStatusKind.Exact => "exact",
                Domain.EntryValueStatusKind.Similar => "similar",
                _ => "new",
            },
            status.ExactMatch is null ? null : new EntryMatchDto(status.ExactMatch.Id, status.ExactMatch.Name),
            status.SimilarMatches.Select(m => new EntryMatchDto(m.Id, m.Name)).ToList());
    }

    /// <summary>
    /// Every pair a user has explicitly marked as not similar for one kind - naturally small (one
    /// row per manual ignore), so this is unbounded only in the sense that a per-kind ignored-
    /// pairs table cannot realistically grow with the library the way a name list does.
    /// </summary>
    [HttpGet("ignored")]
    public async Task<ActionResult<List<IgnoredSimilarValuePairDto>>> GetIgnoredPairs(
        [FromQuery] string valueType)
    {
        var kind = ValidateValueType(valueType);
        if (kind is null)
        {
            return this.InvalidRequest("valueType must be 'author' or 'series'.");
        }

        var pairs = await _similarValueService.GetIgnoredPairsAsync(kind);
        return pairs
            .Select(p => new IgnoredSimilarValuePairDto(p.Id, p.ValueA, p.ValueB, p.IgnoredAtUtc))
            .ToList();
    }

    [HttpPost("ignore")]
    public async Task<IActionResult> IgnorePair([FromBody] IgnoreSimilarValuePairDto dto)
    {
        var kind = ValidateValueType(dto.ValueType);
        if (kind is null)
        {
            return this.InvalidRequest("ValueType must be 'author' or 'series'.");
        }

        if (string.IsNullOrWhiteSpace(dto.Value))
        {
            return this.InvalidRequest("Value is required.");
        }

        if (dto.AgainstValues == null || dto.AgainstValues.Count == 0)
        {
            return this.InvalidRequest("AgainstValues must contain at least one value.");
        }

        if (dto.AgainstValues.Any(string.IsNullOrWhiteSpace))
        {
            return this.InvalidRequest("AgainstValues must not contain a blank value.");
        }

        await _similarValueService.IgnorePairAsync(kind, dto.Value, dto.AgainstValues);
        return Ok();
    }

    [HttpDelete("ignore/{id:long}")]
    public async Task<IActionResult> RemoveIgnoredPair(long id, [FromQuery] string valueType)
    {
        var kind = ValidateValueType(valueType);
        if (kind is null)
        {
            return this.InvalidRequest("valueType must be 'author' or 'series'.");
        }

        await _similarValueService.RemoveIgnoredPairAsync(kind, id);
        return Ok();
    }

    /// <summary>Maps the wire "author"/"series" valueType to the service's "authors"/"series" kind, or null if invalid.</summary>
    private static string? ValidateValueType(string valueType) => valueType switch
    {
        "author" => SimilarValueService.AuthorGroupsKind,
        "series" => SimilarValueService.SeriesGroupsKind,
        _ => null,
    };

    [HttpPost("align")]
    public IActionResult StartAlign([FromBody] AlignSimilarValuesDto dto)
    {
        if (dto.ValueType != "author" && dto.ValueType != "series")
            return this.InvalidRequest("ValueType must be 'author' or 'series'.");
        if (dto.SourceValues == null || dto.SourceValues.Count == 0)
            return this.InvalidRequest("SourceValues must contain at least one value.");
        if (string.IsNullOrWhiteSpace(dto.TargetValue))
            return this.InvalidRequest("TargetValue is required.");

        return BackgroundOperationRunner.Start(
            _alignLock,
            _serviceScopeFactory,
            _logger,
            _statusRegistry,
            OperationKey,
            async sp =>
            {
                var similarValueService = sp.GetRequiredService<ISimilarValueService>();

                Task ProgressAction(int processed, int total, int succeeded, int failed)
                {
                    _statusRegistry.SetProgress(OperationKey, processed, total);
                    return _organizeHub.Clients.All.SimilarValueAlignProgress(
                        new SimilarValueAlignProgress(processed, total, succeeded, failed));
                }

                var (processed, succeeded, failed) = dto.ValueType == "author"
                    ? await similarValueService.AlignAuthorsAsync(dto.SourceValues, dto.TargetValue, ProgressAction)
                    : await similarValueService.AlignSeriesAsync(dto.SourceValues, dto.TargetValue, ProgressAction);

                await _organizeHub.Clients.All.SimilarValueAlignComplete(
                    new SimilarValueAlignComplete(processed, succeeded, failed));
            },
            () => _organizeHub.Clients.All.SimilarValueAlignComplete(new SimilarValueAlignComplete(0, 0, 0)),
            _appLifetime.ApplicationStopping);
    }

    private ObjectResult? ValidatePageSelection(int page, int pageSize)
    {
        if (page < 0)
        {
            return this.InvalidRequest("page must be zero or greater.");
        }

        if (pageSize < 1 || pageSize > PagingLimits.MaxPageSize)
        {
            return this.InvalidRequest($"pageSize must be between 1 and {PagingLimits.MaxPageSize}.");
        }

        // Widened before multiplying - see UrlCleanupController.GetDirtyUrls for why.
        var skip = (long)page * pageSize;
        if (skip > PagingLimits.MaxPageOffset)
        {
            return this.InvalidRequest($"page and pageSize together may not skip more than {PagingLimits.MaxPageOffset} groups.");
        }

        return null;
    }

    private static List<SimilarValueGroupDto> ToDto(List<Domain.SimilarValueGroup> groups)
    {
        return groups.Select(g => new SimilarValueGroupDto(
            g.Candidates.Select(c => new SimilarValueCandidateDto(
                c.Value,
                c.BookCount
            )).ToList()
        )).ToList();
    }
}
