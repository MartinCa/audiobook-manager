using System.Globalization;
using System.Text;
using System.Text.Json;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Extensions;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Scraping.Scrapers;

public class HardcoverScraper : IScraper
{
    private const string _hardcoverDomain = "hardcover.app";
    private const string _hardcoverBaseUrl = $"https://{_hardcoverDomain}";
    private const string _hardcoverApiUrl = "https://api.hardcover.app/v1/graphql";
    private const string _sourceName = "Hardcover";
    private const int _maxNumGenresToGet = 5;

    private static readonly IList<string> _ignoredGenres = new List<string> { "Fiction" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookSeriesMapper _bookSeriesMapper;
    private readonly ILogger<HardcoverScraper> _logger;
    private readonly AudiobookManagerSettings _settings;

    public HardcoverScraper(IHttpClientFactory httpClientFactory, IBookSeriesMapper bookSeriesMapper,
        ILogger<HardcoverScraper> logger, IOptions<AudiobookManagerSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _bookSeriesMapper = bookSeriesMapper;
        _logger = logger;
        _settings = settings.Value;
    }

    public string SourceName => _sourceName;

    public bool RequiresApiKey => true;

    public bool IsApiKeyConfigured => !string.IsNullOrEmpty(_settings.HardcoverApiKey);

    public bool IsSource(string sourceName) => string.Equals(sourceName, _sourceName, StringComparison.InvariantCultureIgnoreCase);

    public bool SupportsUrl(string url) => url.Contains(_hardcoverDomain);

    public async Task<IList<BookSearchResult>> Search(string searchTerm)
    {
        var query = """
            query SearchBooks($query: String!) {
              search(query: $query, query_type: "books", per_page: 15, page: 1) {
                results
              }
            }
            """;

        var variables = new { query = searchTerm };
        var responseElement = await ExecuteGraphqlQuery(query, variables);

        var resultsJson = responseElement.GetNestedProperty("data", "search", "results");

        if (resultsJson.ValueKind != JsonValueKind.Array)
        {
            return new List<BookSearchResult>();
        }

        var results = new List<BookSearchResult>();
        foreach (var hit in resultsJson.EnumerateArray())
        {
            try
            {
                var result = ParseSearchHit(hit);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Hardcover search result");
            }
        }

        return results;
    }

    public async Task<BookSearchResult> GetBookDetails(string bookUrl)
    {
        var bookId = ParseBookIdFromUrl(bookUrl);

        var query = """
            query GetBook($id: Int!) {
              books_by_pk(id: $id) {
                id
                title
                subtitle
                description
                slug
                release_date
                rating
                ratings_count
                cached_image
                cached_tags
                contributions {
                  contribution
                  author {
                    name
                  }
                }
                book_series {
                  position
                  series {
                    name
                  }
                }
                default_audio_edition {
                  isbn_13
                  asin
                  audio_seconds
                  publisher {
                    name
                  }
                  language {
                    language
                  }
                }
                default_physical_edition {
                  isbn_13
                  asin
                  publisher {
                    name
                  }
                  language {
                    language
                  }
                }
              }
            }
            """;

        var variables = new { id = bookId };
        var responseElement = await ExecuteGraphqlQuery(query, variables);

        var bookElement = responseElement.GetNestedProperty("data", "books_by_pk");

        if (bookElement.ValueKind == JsonValueKind.Null)
        {
            throw new Exception($"Book not found on Hardcover: {bookUrl}");
        }

        return await ParseBookDetails(bookElement, bookUrl);
    }

    private BookSearchResult? ParseSearchHit(JsonElement hit)
    {
        var document = hit;

        var idStr = document.GetPropertyValueOrNull("id");
        if (idStr is null)
        {
            return null;
        }

        var title = document.GetPropertyValueOrNull("title");
        if (title is null)
        {
            return null;
        }

        var slug = document.GetPropertyValueOrNull("slug");
        var url = $"{_hardcoverBaseUrl}/books/{slug ?? idStr}";

        var subtitle = document.GetPropertyValueOrNull("subtitle");

        var authors = new List<Person>();
        if (document.TryGetProperty("author_names", out var authorNamesElement) &&
            authorNamesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var authorName in authorNamesElement.EnumerateArray())
            {
                var name = authorName.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    authors.Add(new Person(name));
                }
            }
        }

        string? imageUrl = null;
        if (document.TryGetProperty("image", out var imageElement))
        {
            if (imageElement.ValueKind == JsonValueKind.Object)
            {
                imageUrl = imageElement.GetPropertyValueOrNull("url");
            }
            else if (imageElement.ValueKind == JsonValueKind.String)
            {
                imageUrl = imageElement.GetString();
            }
        }

        int? year = null;
        var releaseDate = document.GetPropertyValueOrNull("release_date");
        if (releaseDate is not null && DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            year = parsedDate.Year;
        }
        else
        {
            var releaseYear = document.GetPropertyValueOrNull("release_year");
            if (releaseYear is not null && int.TryParse(releaseYear, out var parsedYear))
            {
                year = parsedYear;
            }
        }

        int? numberOfRatings = null;
        if (document.TryGetProperty("ratings_count", out var ratingsCountElement) &&
            ratingsCountElement.ValueKind == JsonValueKind.Number)
        {
            numberOfRatings = ratingsCountElement.GetInt32();
        }

        float? rating = null;
        if (document.TryGetProperty("rating", out var ratingElement) &&
            ratingElement.ValueKind == JsonValueKind.Number)
        {
            rating = ratingElement.GetSingle();
        }

        return new BookSearchResult(url, title)
        {
            Authors = authors,
            Narrators = new List<Person>(),
            Subtitle = subtitle,
            Year = year,
            ImageUrl = imageUrl,
            Rating = rating,
            NumberOfRatings = numberOfRatings,
            Series = new List<BookSeriesSearchResult>(),
            Genres = new List<string>(),
        };
    }

    private async Task<BookSearchResult> ParseBookDetails(JsonElement bookElement, string bookUrl)
    {
        string? bookName = null;
        string? subtitle = null;
        try
        {
            var fullTitle = bookElement.GetPropertyValueOrNull("title");
            if (fullTitle is not null)
            {
                var splitTitle = fullTitle.Split(":");
                bookName = splitTitle[0].Trim();
                if (splitTitle.Length > 1)
                {
                    subtitle = string.Join(":", splitTitle.Skip(1)).Trim();
                }
            }

            subtitle ??= bookElement.GetPropertyValueOrNull("subtitle");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse title for {BookUrl}", bookUrl);
        }

        IList<Person> authors = new List<Person>();
        IList<Person> narrators = new List<Person>();
        try
        {
            (authors, narrators) = ParseContributions(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse contributions for {BookUrl}", bookUrl);
        }

        string? imageUrl = null;
        try
        {
            imageUrl = ParseCachedImage(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse image for {BookUrl}", bookUrl);
        }

        int? year = null;
        try
        {
            var releaseDate = bookElement.GetPropertyValueOrNull("release_date");
            if (releaseDate is not null && DateTime.TryParse(releaseDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                year = parsedDate.Year;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse year for {BookUrl}", bookUrl);
        }

        string? description = null;
        try
        {
            description = SanitizeHtml(bookElement.GetPropertyValueOrNull("description"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse description for {BookUrl}", bookUrl);
        }

        IList<string> genres = new List<string>();
        try
        {
            genres = ParseGenres(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse genres for {BookUrl}", bookUrl);
        }

        float? rating = null;
        int? numberOfRatings = null;
        try
        {
            if (bookElement.TryGetProperty("rating", out var ratingElement) &&
                ratingElement.ValueKind == JsonValueKind.Number)
            {
                rating = ratingElement.GetSingle();
            }

            if (bookElement.TryGetProperty("ratings_count", out var ratingsCountElement) &&
                ratingsCountElement.ValueKind == JsonValueKind.Number)
            {
                numberOfRatings = ratingsCountElement.GetInt32();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse rating for {BookUrl}", bookUrl);
        }

        IList<BookSeriesSearchResult> series = new List<BookSeriesSearchResult>();
        try
        {
            series = await ParseSeries(bookElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse series for {BookUrl}", bookUrl);
        }

        string? publisher = null;
        string? language = null;
        string? isbn = null;
        string? asin = null;
        try
        {
            var audioEdition = GetEditionElement(bookElement, "default_audio_edition");
            var physicalEdition = GetEditionElement(bookElement, "default_physical_edition");

            var edition = audioEdition ?? physicalEdition;

            if (edition is not null)
            {
                isbn = edition.Value.GetPropertyValueOrNull("isbn_13");
                asin = edition.Value.GetPropertyValueOrNull("asin");

                if (edition.Value.TryGetProperty("publisher", out var publisherElement) &&
                    publisherElement.ValueKind == JsonValueKind.Object)
                {
                    publisher = publisherElement.GetPropertyValueOrNull("name");
                }

                if (edition.Value.TryGetProperty("language", out var languageElement) &&
                    languageElement.ValueKind == JsonValueKind.Object)
                {
                    language = languageElement.GetPropertyValueOrNull("language");
                }
            }

            // Fall back to physical edition for ISBN/publisher if audio edition didn't have them
            if (audioEdition is not null && physicalEdition is not null)
            {
                if (isbn is null)
                {
                    isbn = physicalEdition.Value.GetPropertyValueOrNull("isbn_13");
                }
                if (publisher is null && physicalEdition.Value.TryGetProperty("publisher", out var pubElement) &&
                    pubElement.ValueKind == JsonValueKind.Object)
                {
                    publisher = pubElement.GetPropertyValueOrNull("name");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse edition details for {BookUrl}", bookUrl);
        }

        string? duration = null;
        try
        {
            var audioEdition = GetEditionElement(bookElement, "default_audio_edition");
            if (audioEdition is not null &&
                audioEdition.Value.TryGetProperty("audio_seconds", out var audioSecondsElement) &&
                audioSecondsElement.ValueKind == JsonValueKind.Number)
            {
                var totalSeconds = audioSecondsElement.GetInt32();
                var hours = totalSeconds / 3600;
                var minutes = (totalSeconds % 3600) / 60;
                duration = hours > 0 ? $"{hours} hrs and {minutes} mins" : $"{minutes} mins";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse duration for {BookUrl}", bookUrl);
        }

        return new BookSearchResult(bookUrl, bookName)
        {
            Authors = authors,
            Narrators = narrators,
            Subtitle = subtitle,
            Duration = duration,
            Year = year,
            Language = language,
            ImageUrl = imageUrl,
            Series = series,
            Description = description,
            Genres = genres,
            Rating = rating,
            NumberOfRatings = numberOfRatings,
            Copyright = null,
            Publisher = publisher,
            Asin = asin,
            Isbn = isbn,
        };
    }

    private static (IList<Person> Authors, IList<Person> Narrators) ParseContributions(JsonElement bookElement)
    {
        var authors = new List<Person>();
        var narrators = new List<Person>();

        if (!bookElement.TryGetProperty("contributions", out var contributionsElement) ||
            contributionsElement.ValueKind != JsonValueKind.Array)
        {
            return (authors, narrators);
        }

        foreach (var contribution in contributionsElement.EnumerateArray())
        {
            var role = contribution.GetPropertyValueOrNull("contribution");
            string? name = null;

            if (contribution.TryGetProperty("author", out var authorElement) &&
                authorElement.ValueKind == JsonValueKind.Object)
            {
                name = authorElement.GetPropertyValueOrNull("name");
            }

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var person = new Person(name) { Role = role };

            if (string.Equals(role, "Narrator", StringComparison.InvariantCultureIgnoreCase))
            {
                narrators.Add(person);
            }
            else
            {
                authors.Add(person);
            }
        }

        return (authors, narrators);
    }

    private static string? ParseCachedImage(JsonElement bookElement)
    {
        if (!bookElement.TryGetProperty("cached_image", out var cachedImageElement))
        {
            return null;
        }

        if (cachedImageElement.ValueKind == JsonValueKind.String)
        {
            var jsonStr = cachedImageElement.GetString();
            if (jsonStr is not null)
            {
                var imageObj = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                return imageObj.GetPropertyValueOrNull("url");
            }
        }
        else if (cachedImageElement.ValueKind == JsonValueKind.Object)
        {
            return cachedImageElement.GetPropertyValueOrNull("url");
        }

        return null;
    }

    private IList<string> ParseGenres(JsonElement bookElement)
    {
        var genres = new List<string>();

        if (!bookElement.TryGetProperty("cached_tags", out var cachedTagsElement))
        {
            return genres;
        }

        JsonElement tagsObj;
        if (cachedTagsElement.ValueKind == JsonValueKind.String)
        {
            var jsonStr = cachedTagsElement.GetString();
            if (jsonStr is null)
            {
                return genres;
            }
            tagsObj = JsonSerializer.Deserialize<JsonElement>(jsonStr);
        }
        else if (cachedTagsElement.ValueKind == JsonValueKind.Object)
        {
            tagsObj = cachedTagsElement;
        }
        else
        {
            return genres;
        }

        if (tagsObj.TryGetProperty("Genre", out var genreElement) &&
            genreElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var genre in genreElement.EnumerateArray())
            {
                var tag = genre.GetPropertyValueOrNull("tag");
                if (!string.IsNullOrEmpty(tag))
                {
                    genres.Add(tag);
                }
            }
        }

        return genres
            .Where(g => !_ignoredGenres.Any(ig => string.Equals(ig, g, StringComparison.InvariantCultureIgnoreCase)))
            .Take(_maxNumGenresToGet)
            .ToList();
    }

    private async Task<IList<BookSeriesSearchResult>> ParseSeries(JsonElement bookElement)
    {
        var series = new List<BookSeriesSearchResult>();

        if (!bookElement.TryGetProperty("book_series", out var bookSeriesElement) ||
            bookSeriesElement.ValueKind != JsonValueKind.Array)
        {
            return series;
        }

        foreach (var bs in bookSeriesElement.EnumerateArray())
        {
            string? seriesName = null;
            if (bs.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Object)
            {
                seriesName = seriesElement.GetPropertyValueOrNull("name");
            }

            if (string.IsNullOrEmpty(seriesName))
            {
                continue;
            }

            string? position = null;
            if (bs.TryGetProperty("position", out var positionElement))
            {
                if (positionElement.ValueKind == JsonValueKind.Number)
                {
                    var posValue = positionElement.GetSingle();
                    position = posValue == Math.Floor(posValue)
                        ? ((int)posValue).ToString(CultureInfo.InvariantCulture)
                        : posValue.ToString(CultureInfo.InvariantCulture);
                }
                else if (positionElement.ValueKind == JsonValueKind.String)
                {
                    position = positionElement.GetString();
                }
            }

            series.Add(new BookSeriesSearchResult(seriesName)
            {
                SeriesPart = position
            });
        }

        return await _bookSeriesMapper.MapBookSeries(series);
    }

    private static JsonElement? GetEditionElement(JsonElement bookElement, string editionProperty)
    {
        if (bookElement.TryGetProperty(editionProperty, out var editionElement) &&
            editionElement.ValueKind == JsonValueKind.Object)
        {
            return editionElement;
        }

        return null;
    }

    private static int ParseBookIdFromUrl(string url)
    {
        // URL formats: https://hardcover.app/books/{slug} or just a numeric ID
        if (int.TryParse(url, out var directId))
        {
            return directId;
        }

        // Try to extract from URL path - the slug isn't numeric, so we need to handle this
        // The URL might contain the book ID as a query parameter or we may need to look it up
        var uri = new Uri(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Check if the last segment is numeric
        if (segments.Length > 0 && int.TryParse(segments.Last(), out var pathId))
        {
            return pathId;
        }

        // Try slug-based: slugs on hardcover often start with the ID like "12345-book-title"
        if (segments.Length > 0)
        {
            var lastSegment = segments.Last();
            var dashIndex = lastSegment.IndexOf('-');
            if (dashIndex > 0 && int.TryParse(lastSegment.Substring(0, dashIndex), out var slugId))
            {
                return slugId;
            }
        }

        throw new Exception($"Could not extract book ID from Hardcover URL: {url}");
    }

    private async Task<JsonElement> ExecuteGraphqlQuery(string query, object variables)
    {
        var httpClient = _httpClientFactory.CreateClient("hardcover");

        var requestBody = JsonSerializer.Serialize(new
        {
            query,
            variables
        });

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(_hardcoverApiUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Hardcover API returned status {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);

        if (responseElement.TryGetProperty("errors", out var errorsElement) &&
            errorsElement.ValueKind == JsonValueKind.Array &&
            errorsElement.GetArrayLength() > 0)
        {
            var firstError = errorsElement[0].GetPropertyValueOrNull("message") ?? "Unknown GraphQL error";
            throw new Exception($"Hardcover GraphQL error: {firstError}");
        }

        return responseElement;
    }

    private static string? SanitizeHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return html;
        }

        var result = html
            .Replace("<br />", "\n")
            .Replace("<br>", "\n")
            .Replace("<br/>", "\n");

        // Remove HTML tags
        result = System.Text.RegularExpressions.Regex.Replace(result, @"<[^>]+>", "");

        result = result
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"");

        return result.Trim();
    }
}
