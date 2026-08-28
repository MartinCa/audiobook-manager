using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AudiobookManager.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class BrowseController : ControllerBase
{
    private readonly IAudiobookRepository _audiobookRepo;
    private readonly IPersonRepository _personRepo;

    public BrowseController(
        IAudiobookRepository audiobookRepo,
        IPersonRepository personRepo)
    {
        _audiobookRepo = audiobookRepo;
        _personRepo = personRepo;
    }

    [HttpGet("audiobooks")]
    public async Task<PaginatedResult<AudiobookSummaryDto>> GetAudiobooks(int limit = 20, int offset = 0)
    {
        var (items, total) = await _audiobookRepo.GetAllAsync(limit, offset);
        var dtos = items.Select(MapToSummaryDto).ToList();
        return new PaginatedResult<AudiobookSummaryDto>(dtos.Count, total, dtos);
    }

    [HttpGet("library-search")]
    public async Task<LibrarySearchResultDto> SearchLibrary([FromQuery] string q, int limit = 5)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return new LibrarySearchResultDto([], [], []);
        }

        var (books, _) = await _audiobookRepo.SearchAsync(q, limit, 0);
        var authors = await _personRepo.SearchAuthorSummariesAsync(q, limit);
        var series = await _audiobookRepo.SearchSeriesAsync(q, limit);

        var bookHits = RankByRelevance(books, q, a => a.BookName ?? "")
            .Select(a => new LibraryBookHitDto(
                a.Id,
                a.BookName,
                a.Subtitle,
                a.Authors.Select(p => p.Name).ToList(),
                a.Series,
                a.Year,
                a.CoverFilePath))
            .ToList();

        var authorHits = RankByRelevance(authors, q, p => p.Name)
            .Select(p => new LibraryAuthorHitDto(p.Id, p.Name, p.BookCount))
            .ToList();

        var seriesHits = RankByRelevance(series, q, s => s.Series)
            .Select(s => new LibrarySeriesHitDto(s.Series, s.BookCount))
            .ToList();

        return new LibrarySearchResultDto(bookHits, authorHits, seriesHits);
    }

    private static List<T> RankByRelevance<T>(List<T> items, string query, Func<T, string> keySelector) =>
        items
            .OrderByDescending(i => keySelector(i).StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(keySelector)
            .ToList();

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
        var authors = await _personRepo.GetAllAuthorSummariesAsync();
        return authors.Select(a => new AuthorSummaryDto(a.Id, a.Name, a.BookCount)).ToList();
    }

    [HttpGet("authors/{authorId}")]
    public async Task<ActionResult<AuthorDetailDto>> GetAuthorDetail(long authorId)
    {
        // Three narrow queries rather than one that materializes the author's entire catalogue:
        // the series section only needs a name and a count, so those books are never loaded.
        var author = await _personRepo.GetAuthorSummaryAsync(authorId);
        if (author == null)
        {
            return NotFound();
        }

        var seriesCounts = await _audiobookRepo.GetSeriesCountsByAuthorAsync(authorId);
        var standalone = await _audiobookRepo.GetStandaloneBooksByAuthorAsync(authorId);

        var summary = new AuthorSummaryDto(author.Id, author.Name, author.BookCount);
        var series = seriesCounts.Select(s => new SeriesInfo(s.Series, s.BookCount)).ToList();
        var standaloneBooks = standalone.Select(MapToSummaryDto).ToList();

        return new AuthorDetailDto(summary, series, standaloneBooks);
    }

    [HttpGet("audiobooks/{id}")]
    public async Task<ActionResult<AudiobookDetailDto>> GetAudiobookDetail(long id)
    {
        var audiobook = await _audiobookRepo.GetByIdWithIncludesAsync(id);
        if (audiobook == null)
        {
            return NotFound();
        }

        return new AudiobookDetailDto(
            audiobook.Id,
            audiobook.BookName,
            audiobook.Subtitle,
            audiobook.Series,
            audiobook.SeriesPart,
            audiobook.Year,
            audiobook.Authors.Select(p => p.Name).ToList(),
            audiobook.Narrators.Select(p => p.Name).ToList(),
            audiobook.Genres.Select(g => g.Name).ToList(),
            audiobook.Description,
            audiobook.Copyright,
            audiobook.Publisher,
            audiobook.Rating,
            audiobook.Asin,
            audiobook.Www,
            audiobook.CoverFilePath,
            audiobook.DurationInSeconds,
            audiobook.FileInfoFullPath,
            audiobook.FileInfoFileName,
            audiobook.FileInfoSizeInBytes
        );
    }

    [HttpGet("audiobooks/{id}/cover")]
    public async Task<IActionResult> GetAudiobookCover(long id)
    {
        var coverFilePath = await _audiobookRepo.GetCoverFilePathAsync(id);
        if (string.IsNullOrEmpty(coverFilePath) || !System.IO.File.Exists(coverFilePath))
            return NotFound();

        var mimeType = coverFilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        // Stream the file instead of buffering it, and let the browser revalidate rather than
        // re-download: a 50-row library page requests 50 of these. Deliberately no max-age -
        // a cover is rewritten in place whenever the book is saved, and nothing in the URL
        // changes when it is, so a freshness window would serve a stale image for its duration.
        // The ETag/Last-Modified pair makes the repeat request a cheap 304 instead.
        var fullPath = Path.GetFullPath(coverFilePath);
        var lastModified = new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero);
        var length = new FileInfo(fullPath).Length;
        var entityTag = new EntityTagHeaderValue($"\"{lastModified.ToUnixTimeMilliseconds():x}-{length:x}\"");

        Response.Headers.CacheControl = "private, no-cache";
        return PhysicalFile(fullPath, mimeType, lastModified, entityTag, enableRangeProcessing: true);
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
