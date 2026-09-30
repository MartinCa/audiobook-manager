using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Moq;

namespace AudiobookManager.Test.Services;

[TestClass]
public class QualifierIndicatorServiceTests
{
    private Mock<IQualifierIndicatorRepository> _repository = null!;
    private List<QualifierIndicator> _stored = null!;
    private QualifierIndicatorService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _stored = new List<QualifierIndicator>();
        _repository = new Mock<IQualifierIndicatorRepository>();
        _repository.Setup(r => r.GetAllAsync()).ReturnsAsync(() => _stored);
        _repository.Setup(r => r.ReplaceAllAsync(It.IsAny<IReadOnlyCollection<QualifierIndicator>>()))
            .ReturnsAsync((IReadOnlyCollection<QualifierIndicator> rules) =>
            {
                _stored = rules.ToList();
                return _stored;
            });

        var audible = new Mock<IScraper>();
        audible.Setup(s => s.SourceName).Returns("Audible");
        var goodreads = new Mock<IScraper>();
        goodreads.Setup(s => s.SourceName).Returns("Goodreads");
        _service = new QualifierIndicatorService(_repository.Object, new[] { audible.Object, goodreads.Object });
    }

    [TestMethod]
    public async Task ReplaceRules_NormalizesTheIndicatorAndTheSourceAndKey()
    {
        var saved = await _service.ReplaceRulesAsync(new[]
        {
            new QualifierIndicatorRule("audible", " [Dramatized   Adaptation] ", "Dramatized"),
        });

        CollectionAssert.AreEqual(
            new[] { new QualifierIndicatorRule("Audible", "Dramatized Adaptation", "dramatized") },
            saved.ToArray());
    }

    [TestMethod]
    public async Task ReplaceRules_TheSameWordingTwiceForOneSourceIsStoredOnce()
    {
        var saved = await _service.ReplaceRulesAsync(new[]
        {
            new QualifierIndicatorRule("Audible", "Abridged", "abridged"),
            new QualifierIndicatorRule("Audible", "(abridged)", "abridged"),
            new QualifierIndicatorRule("Goodreads", "Abridged", "abridged"),
        });

        Assert.AreEqual(2, saved.Count);
    }

    [TestMethod]
    public async Task ReplaceRules_RefusesAnUnknownSourceAndStoresNothing()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.ReplaceRulesAsync(new[]
        {
            new QualifierIndicatorRule("Nowhere", "Abridged", "abridged"),
        }));

        StringAssert.Contains(ex.Message, "Nowhere");
        _repository.Verify(r => r.ReplaceAllAsync(It.IsAny<IReadOnlyCollection<QualifierIndicator>>()), Times.Never);
    }

    [TestMethod]
    public async Task ReplaceRules_RefusesAnUnknownQualifier()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.ReplaceRulesAsync(new[]
        {
            new QualifierIndicatorRule("Audible", "Abridged", "nope"),
        }));

        StringAssert.Contains(ex.Message, "nope");
    }

    [TestMethod]
    public async Task ReplaceRules_RefusesABlankIndicator()
    {
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => _service.ReplaceRulesAsync(new[]
        {
            new QualifierIndicatorRule("Audible", " [ ] ", "abridged"),
        }));
    }

    [TestMethod]
    public async Task ReplaceRules_AnEmptyListClearsTheRules()
    {
        _stored.Add(new QualifierIndicator(1, "Audible", "Abridged", "abridged"));

        var saved = await _service.ReplaceRulesAsync(Array.Empty<QualifierIndicatorRule>());

        Assert.AreEqual(0, saved.Count);
    }

    [TestMethod]
    public async Task GetRules_MapsStoredRows()
    {
        _stored.Add(new QualifierIndicator(1, "Audible", "Abridged", "abridged"));

        var rules = await _service.GetRulesAsync();

        CollectionAssert.AreEqual(new[] { new QualifierIndicatorRule("Audible", "Abridged", "abridged") }, rules.ToArray());
    }
}
