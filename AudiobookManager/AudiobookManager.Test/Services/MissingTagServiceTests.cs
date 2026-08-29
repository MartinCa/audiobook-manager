using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;

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

    private static DbAudiobook MakeDbAudiobook(
        long id,
        string bookName,
        int year = 2024,
        string? series = null,
        List<DbPerson>? authors = null,
        string? description = null,
        string? coverFilePath = null,
        string? language = null)
    {
        var audiobook = new DbAudiobook(id, bookName, null, series, null, year, description, null, null, language, null, null, null,
            coverFilePath, null, $"/library/{bookName}.m4b", $"{bookName}.m4b", 1000)
        {
            Authors = authors ?? new List<DbPerson>()
        };
        return audiobook;
    }

    [TestMethod]
    public void GetTaggableFields_MarksAuthorBookNameAndYearAsCriticalByDefault()
    {
        var fields = _service.GetTaggableFields();

        var critical = fields.Where(f => f.IsCriticalByDefault).Select(f => f.Key).ToList();
        CollectionAssert.AreEquivalent(new List<string> { "Authors", "BookName", "Year" }, critical);

        Assert.IsTrue(fields.Any(f => f.Key == "Series" && !f.IsCriticalByDefault));
        Assert.IsTrue(fields.Any(f => f.Key == "SeriesPart" && !f.IsCriticalByDefault));
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsAsync_FlagsEmptyRequestedFields()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", authors: new List<DbPerson> { new(1, "Author One") }),
            MakeDbAudiobook(2, "Book Two", authors: new List<DbPerson>())
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindAudiobooksMissingTagsAsync(new[] { "Authors" });

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(2, results[0].AudiobookId);
        CollectionAssert.AreEquivalent(new List<string> { "Authors" }, results[0].MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsAsync_TreatsZeroYearAsMissing()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", year: 0),
            MakeDbAudiobook(2, "Book Two", year: 2020)
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindAudiobooksMissingTagsAsync(new[] { "Year" });

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(1, results[0].AudiobookId);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsAsync_DoesNotFlagOptionalFieldsWhenNotRequested()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", series: null, authors: new List<DbPerson> { new(1, "Author One") })
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindAudiobooksMissingTagsAsync(new[] { "Authors", "BookName", "Year" });

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsAsync_ReturnsMultipleMissingFieldsPerBook()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", year: 0, description: null, authors: new List<DbPerson>())
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindAudiobooksMissingTagsAsync(new[] { "Authors", "Year", "Description" });

        Assert.AreEqual(1, results.Count);
        CollectionAssert.AreEquivalent(new List<string> { "Authors", "Year", "Description" }, results[0].MissingFields);
    }

    [TestMethod]
    public async Task FindAudiobooksMissingTagsAsync_ReturnsEmptyListWhenNoFieldsRequested()
    {
        var results = await _service.FindAudiobooksMissingTagsAsync(Array.Empty<string>());

        Assert.AreEqual(0, results.Count);
        _audiobookRepository.Verify(r => r.GetAllWithIncludesAsync(), Times.Never);
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
    public async Task FindAudiobooksMissingTagsAsync_FlagsBooksWithNoLanguage()
    {
        var books = new List<DbAudiobook>
        {
            MakeDbAudiobook(1, "Book One", language: "en"),
            MakeDbAudiobook(2, "Book Two", language: null),
            MakeDbAudiobook(3, "Book Three", language: "  ")
        };
        _audiobookRepository.Setup(r => r.GetAllWithIncludesAsync()).ReturnsAsync(books);

        var results = await _service.FindAudiobooksMissingTagsAsync(new[] { "Language" });

        CollectionAssert.AreEquivalent(
            new List<long> { 2, 3 },
            results.Select(r => r.AudiobookId).ToList());
        CollectionAssert.AreEquivalent(new List<string> { "Language" }, results[0].MissingFields);
    }
}
