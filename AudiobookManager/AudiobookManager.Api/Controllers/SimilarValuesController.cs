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

    [HttpGet("author-names")]
    public async Task<List<string>> GetAuthorNames()
    {
        return await _personRepository.GetAuthorNamesAsync();
    }

    [HttpGet("narrator-names")]
    public async Task<List<string>> GetNarratorNames()
    {
        return await _personRepository.GetNarratorNamesAsync();
    }

    [HttpGet("series-names")]
    public async Task<List<string>> GetSeriesNames()
    {
        return await _audiobookRepository.GetSeriesNamesAsync();
    }

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
