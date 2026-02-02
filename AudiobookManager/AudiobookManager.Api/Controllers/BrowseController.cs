using AudiobookManager.Api.Dtos;
using AudiobookManager.Database.Repositories;
using Microsoft.AspNetCore.Mvc;

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
        var authors = await _personRepo.GetAllAuthorsAsync();
        return authors.Select(a => new AuthorSummaryDto(a.Id, a.Name, a.BooksAuthored.Count)).ToList();
    }

    [HttpGet("authors/{authorId}")]
    public async Task<ActionResult<AuthorDetailDto>> GetAuthorDetail(long authorId)
    {
        var author = await _personRepo.GetAuthorWithBooksAsync(authorId);
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
