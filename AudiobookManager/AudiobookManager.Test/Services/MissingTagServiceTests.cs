using System.Linq.Expressions;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;

namespace AudiobookManager.Test.Services;

[TestClass]
public class MissingTagServiceTests
{
    private Mock<IAudiobookRepository> _audiobookRepository = null!;
    private MissingTagService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _audiobookRepository = new Mock<IAudiobookRepository>();
        _service = new MissingTagService(_audiobookRepository.Object);
    }

    private static readonly Expression<Func<DbAudiobook, bool>> AnySqlPredicate = a => true;

    /// <summary>
    /// The number of SQL predicates the service must push for a selection of that many fields.
    /// The repo tests pin each individual predicate against real SQLite; here the service is
    /// pinned to forward exactly one expression per selected field.
    /// </summary>
    internal static IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>> SqlPredicates(int count) =>
        Enumerable.Range(0, count).Select(_ => AnySqlPredicate).ToList();

    /// <summary>
    /// The compact per-book projection the repository now feeds the check (see MissingTagRow).
    /// Defaults model a fully-tagged book; tests flip the flags they care about.
    /// </summary>
    private static MissingTagRow MakeRow(
        long id,
        string bookName,
        List<string>? authors = null,
        bool hasNarrator = true,
        bool hasGenre = true,
        bool bookNameBlank = false,
        bool yearZero = false,
        bool seriesBlank = false,
        bool seriesPartBlank = false,
        bool subtitleBlank = false,
        bool descriptionBlank = false,
        bool languageBlank = false,
        bool coverBlank = false,
        bool copyrightBlank = false,
        bool publisherBlank = false,
        bool ratingBlank = false,
        bool asinBlank = false,
        bool wwwBlank = false)
    {
        var hasRealAuthor = (authors ?? new List<string>()).Any(a => a != "" && a != null);
        return new MissingTagRow(
            id, bookName, authors ?? new List<string>(),
            hasRealAuthor, hasNarrator, hasGenre,
            bookNameBlank, yearZero, seriesBlank, seriesPartBlank,
            subtitleBlank, descriptionBlank, languageBlank, coverBlank,
            copyrightBlank, publisherBlank, ratingBlank, asinBlank, wwwBlank);
    }

    /// <summary>Set up the repository to answer the paged query for the given fields/search/page, returning the page rows and total.</summary>
    private void StubPage(
        List<MissingTagRow> rows,
        int total,
        string? search = null,
        int skip = 0,
        int take = 50)
    {
        _audiobookRepository
            .Setup(r => r.GetMissingTagRowsPageAsync(
                It.IsAny<IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>>(),
                search, skip, take))
            .ReturnsAsync((rows, total));
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_FlagsBooksWithNoAuthor()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One", authors: new List<string> { "Author One" }),
            MakeRow(2, "Book Two", authors: new List<string>()),
        }, total: 1);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Authors" }, null, skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(2, results[0].AudiobookId);
        CollectionAssert.AreEquivalent(new List<string> { "Authors" }, results[0].MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_TreatsZeroYearAsMissing()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One", yearZero: true),
            MakeRow(2, "Book Two"),
        }, total: 1);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Year" }, null, skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1, results[0].AudiobookId);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_DoesNotFlagOptionalFieldsWhenNotRequested()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One", authors: new List<string> { "Author One" }),
        }, total: 0);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(
            new[] { "Authors", "BookName", "Year" }, null, skip: 0, take: 50);

        Assert.AreEqual(0, total);
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_ReturnsMultipleMissingFieldsPerBook()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One", authors: new List<string>(), yearZero: true, descriptionBlank: true),
        }, total: 1);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(
            new[] { "Authors", "Year", "Description" }, null, skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(1, results.Count);
        CollectionAssert.AreEquivalent(new List<string> { "Authors", "Year", "Description" }, results[0].MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_ReportsOnlyTheSelectedFieldsPerRow()
    {
        // The book is also missing an unselected field; the per-book list must stay the
        // intersection with the selected fields.
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One", yearZero: true, seriesBlank: true),
        }, total: 1);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Year" }, null, skip: 0, take: 50);

        Assert.AreEqual(1, total);
        CollectionAssert.AreEquivalent(new List<string> { "Year" }, results.Single().MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_ReturnsEmptyWhenNoFieldsRequested()
    {
        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(Array.Empty<string>(), null, skip: 0, take: 50);

        Assert.AreEqual(0, total);
        Assert.AreEqual(0, results.Count);
        _audiobookRepository.Verify(
            r => r.GetMissingTagRowsPageAsync(
                It.IsAny<IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_PushesOneSqlPredicatePerSelectedField()
    {
        IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>? captured = null;
        _audiobookRepository
            .Setup(r => r.GetMissingTagRowsPageAsync(
                It.IsAny<IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>>(),
                null, 0, 50))
            .Callback((IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>> predicates, string? _, int _, int _) => captured = predicates)
            .ReturnsAsync((new List<MissingTagRow>(), 0));

        await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Year", "Language", "Www" }, null, skip: 0, take: 50);

        Assert.IsNotNull(captured);
        Assert.AreEqual(3, captured!.Count, "each selected field contributes its SQL 'is missing' predicate to the WHERE clause");
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_PassesThePageThroughUnchanged()
    {
        var rows = new List<MissingTagRow>
        {
            MakeRow(11, "Zebra", yearZero: true),
            MakeRow(12, "Apple", yearZero: true),
        };
        StubPage(rows, total: 321, skip: 40, take: 25);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Year" }, null, skip: 40, take: 25);

        Assert.AreEqual(321, total, "the matched-set total comes from SQL, not from the slice");
        Assert.AreSequenceEqual(new List<long> { 11, 12 }, results.Select(r => r.AudiobookId),
            "the page rows are mapped 1:1; ordering and slicing are the repository's job now");
        CollectionAssert.AreEquivalent(new List<string> { "Year" }, results[0].MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_AppliesTheSearchInTheRepository()
    {
        _audiobookRepository
            .Setup(r => r.GetMissingTagRowsPageAsync(
                It.IsAny<IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>>(),
                "rene", 0, 50))
            .ReturnsAsync((new List<MissingTagRow>(), 0));

        await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Year" }, "rene", skip: 0, take: 50);

        _audiobookRepository.Verify(
            r => r.GetMissingTagRowsPageAsync(
                It.IsAny<IReadOnlyCollection<Expression<Func<DbAudiobook, bool>>>>(),
                "rene", 0, 50),
            Times.Once);
    }

    [TestMethod]
    public void GetTaggableFields_OffersLanguageAsANonCriticalField()
    {
        var fields = _service.GetTaggableFields();

        var language = fields.SingleOrDefault(f => f.Key == "Language");
        Assert.IsNotNull(language, "Language must be offered as a checkable field");
        Assert.AreEqual("Language", language.Label);
        // Language plays no part in path generation, so it is not one of the critical defaults.
        Assert.IsFalse(language.IsCriticalByDefault);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_FlagsBooksWithNoLanguage()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One"),
            MakeRow(2, "Book Two", languageBlank: true),
            MakeRow(3, "Book Three", languageBlank: true),
        }, total: 2);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(new[] { "Language" }, null, skip: 0, take: 50);

        Assert.AreEqual(2, total);
        CollectionAssert.AreEquivalent(
            new List<long> { 2, 3 },
            results.Select(r => r.AudiobookId).ToList());
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsPageAsync_FlagsBooksMissingCopyrightPublisherRatingAsinOrWww()
    {
        StubPage(new List<MissingTagRow>
        {
            MakeRow(1, "Book One"),
            MakeRow(2, "Book Two", copyrightBlank: true, publisherBlank: true, ratingBlank: true, asinBlank: true, wwwBlank: true),
        }, total: 1);

        var (results, total) = await _service.FindAudiobooksMissingTagsPageAsync(
            new[] { "Copyright", "Publisher", "Rating", "Asin", "Www" }, null, skip: 0, take: 50);

        Assert.AreEqual(1, total);
        Assert.AreEqual(2, results[0].AudiobookId);
        CollectionAssert.AreEquivalent(
            new List<string> { "Copyright", "Publisher", "Rating", "Asin", "Www" },
            results[0].MissingFields);
    }

    /// <summary>
    /// Regression guard for the missing-tags binding invariant in AGENTS.md: every field the tag
    /// writer (AudiobookTagHandler) persists to the m4b must have a corresponding checkable entry
    /// here, or a book missing that field becomes invisible to the Missing Tags feature. This list
    /// mirrors AudiobookTagHandler.SaveAudiobookTagsToFile's taggable, checkable fields (excluding
    /// derived/non-taggable data like DurationInSeconds and the file path columns).
    /// </summary>
    [TestMethod]
    public void GetTaggableFields_CoversEveryWritableTagField()
    {
        var expectedKeys = new List<string>
        {
            "Authors", "BookName", "Year", "Series", "SeriesPart", "Narrators", "Subtitle",
            "Description", "Genres", "Language", "Cover", "Copyright", "Publisher", "Rating", "Asin", "Www",
        };

        var actualKeys = _service.GetTaggableFields().Select(f => f.Key).ToList();

        CollectionAssert.AreEquivalent(expectedKeys, actualKeys);
    }
}