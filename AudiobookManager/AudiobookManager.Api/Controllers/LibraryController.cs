using AudiobookManager.Api.Async;
using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class LibraryController : ControllerBase
{
    private readonly IHubContext<OrganizeHub, IOrganize> _organizeHub;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IDiscoveredAudiobookRepository _discoveredRepo;
    private readonly IAudiobookRepository _audiobookRepo;
    private readonly ILogger<LibraryController> _logger;

    public LibraryController(
        IHubContext<OrganizeHub, IOrganize> organizeHub,
        IServiceScopeFactory serviceScopeFactory,
        IDiscoveredAudiobookRepository discoveredRepo,
        IAudiobookRepository audiobookRepo,
        ILogger<LibraryController> logger)
    {
        _organizeHub = organizeHub;
        _serviceScopeFactory = serviceScopeFactory;
        _discoveredRepo = discoveredRepo;
        _audiobookRepo = audiobookRepo;
        _logger = logger;
    }

    [HttpPost("scan")]
    public IActionResult StartLibraryScan()
    {
        Task.Run(async () =>
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var scanService = scope.ServiceProvider.GetRequiredService<ILibraryScanService>();

            try
            {
                var totalFiles = 0;
                var newFilesDiscovered = 0;

                await scanService.ScanLibrary(async (message, filesScanned, total) =>
                {
                    totalFiles = total;
                    await _organizeHub.Clients.All.LibraryScanProgress(
                        new LibraryScanProgress(message, filesScanned, total));
                });

                var discoveredRepo = scope.ServiceProvider.GetRequiredService<Database.Repositories.IDiscoveredAudiobookRepository>();
                var discovered = await discoveredRepo.GetAllAsync();
                newFilesDiscovered = discovered.Count;

                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(totalFiles, newFilesDiscovered, totalFiles - newFilesDiscovered));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during library scan");
                await _organizeHub.Clients.All.LibraryScanComplete(
                    new LibraryScanComplete(0, 0, 0));
            }
        });

        return Ok();
    }

    [HttpGet("discovered")]
    public async Task<PaginatedResult<AudiobookFileInfo>> GetDiscovered(int limit = 20, int offset = 0)
    {
        var all = await _discoveredRepo.GetAllAsync();
        var ordered = all.OrderBy(d => d.FileInfoFullPath).ToList();
        var items = ordered
            .Skip(offset)
            .Take(limit)
            .Select(d => new AudiobookFileInfo(d.FileInfoFullPath, d.FileInfoFileName, d.FileInfoSizeInBytes))
            .ToList();

        return new PaginatedResult<AudiobookFileInfo>(items.Count, ordered.Count, items);
    }

    [HttpDelete("discovered")]
    public async Task<IActionResult> DeleteDiscovered([FromQuery] string path)
    {
        await _discoveredRepo.DeleteByPathAsync(path);
        return NoContent();
    }

    [HttpGet("audiobooks")]
    public async Task<PaginatedResult<AudiobookSummaryDto>> GetAudiobooks(int limit = 20, int offset = 0)
    {
        var (items, total) = await _audiobookRepo.GetAllAsync(limit, offset);
        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PaginatedResult<AudiobookSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("audiobooks/search")]
    public async Task<PaginatedResult<AudiobookSummaryDto>> SearchAudiobooks([FromQuery] string q, int limit = 20, int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return await GetAudiobooks(limit, offset);
        }

        var (items, total) = await _audiobookRepo.SearchAsync(q, limit, offset);
        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PaginatedResult<AudiobookSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("authors")]
    public async Task<List<AuthorSummaryDto>> GetAuthors()
    {
        var authors = await _audiobookRepo.GetAllAuthorsAsync();
        return authors.Select(a => new AuthorSummaryDto(a.Id, a.Name, a.BooksAuthored.Count)).ToList();
    }

    [HttpGet("authors/{authorId}")]
    public async Task<ActionResult<AuthorDetailDto>> GetAuthorDetail(long authorId)
    {
        var author = await _audiobookRepo.GetAuthorWithBooksAsync(authorId);
        if (author == null)
        {
            return NotFound();
        }

        var summary = new AuthorSummaryDto(author.Id, author.Name, author.BooksAuthored.Count);

        var series = author.BooksAuthored
            .Where(b => !string.IsNullOrEmpty(b.Series))
            .GroupBy(b => b.Series!)
            .Select(g => new SeriesInfo(g.Key, g.Count()))
            .OrderBy(s => s.SeriesName)
            .ToList();

        var standaloneBooks = author.BooksAuthored
            .Where(b => string.IsNullOrEmpty(b.Series))
            .Select(MapToSummaryDto)
            .OrderBy(b => b.BookName)
            .ToList();

        return new AuthorDetailDto(summary, series, standaloneBooks);
    }

    [HttpGet("series/{seriesName}")]
    public async Task<List<AudiobookSummaryDto>> GetSeriesBooks(string seriesName, [FromQuery] long? authorId)
    {
        var books = await _audiobookRepo.GetBooksBySeriesAsync(seriesName, authorId);
        return books.Select(MapToSummaryDto).ToList();
    }

    private static AudiobookSummaryDto MapToSummaryDto(Database.Models.Audiobook a)
    {
        return new AudiobookSummaryDto(
            a.Id,
            a.BookName,
            a.Subtitle,
            a.Series,
            a.SeriesPart,
            a.Year,
            a.Authors.Select(p => p.Name).ToList(),
            a.Narrators.Select(p => p.Name).ToList(),
            a.Genres.Select(g => g.Name).ToList(),
            a.CoverFilePath,
            a.DurationInSeconds
        );
    }
}
