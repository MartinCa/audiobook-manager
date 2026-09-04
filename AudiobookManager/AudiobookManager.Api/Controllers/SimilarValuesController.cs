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
    public async Task<List<SimilarValueGroupDto>> GetSimilarAuthors()
    {
        var groups = await _similarValueService.DetectSimilarAuthorsAsync();
        return ToDto(groups);
    }

    [HttpGet("similar-series")]
    public async Task<List<SimilarValueGroupDto>> GetSimilarSeries()
    {
        var groups = await _similarValueService.DetectSimilarSeriesAsync();
        return ToDto(groups);
    }

    [HttpGet("author-names")]
    public async Task<List<string>> GetAuthorNames()
    {
        return await _personRepository.GetAuthorNamesAsync();
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

    private static List<SimilarValueGroupDto> ToDto(List<Domain.SimilarValueGroup> groups)
    {
        return groups.Select(g => new SimilarValueGroupDto(
            g.Candidates.Select(c => new SimilarValueCandidateDto(
                c.Value,
                c.Books.Select(b => new SimilarValueBookDto(b.Id, b.BookName)).ToList()
            )).ToList()
        )).ToList();
    }
}
