using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbSeriesMapping = AudiobookManager.Database.Models.SeriesMapping;
using DbLibrarySettings = AudiobookManager.Database.Models.LibrarySettings;
using DbInitialsSpacing = AudiobookManager.Database.Models.InitialsSpacing;
using DomainSeriesMapping = AudiobookManager.Domain.SeriesMapping;
using DomainLibrarySettings = AudiobookManager.Domain.LibrarySettings;
using DomainInitialsSpacing = AudiobookManager.Domain.InitialsSpacing;

namespace AudiobookManager.Test.Services;

/// <summary>
/// SettingsService covers series-mapping CRUD (regex-to-series rules used elsewhere for series
/// normalization) and the UI-editable library settings (currently the initials-spacing
/// preference), both mapped between the Database and Domain shapes.
/// </summary>
[TestClass]
public class SettingsServiceTests
{
    private Mock<ISeriesMappingRepository> _repository = null!;
    private Mock<ILibrarySettingsRepository> _librarySettingsRepository = null!;
    private SettingsService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repository = new Mock<ISeriesMappingRepository>();
        _librarySettingsRepository = new Mock<ILibrarySettingsRepository>();
        _service = new SettingsService(_repository.Object, _librarySettingsRepository.Object);
    }

    [TestMethod]
    public async Task CreateSeriesMapping_MapsDomainToDbAndBackToDomain()
    {
        var domainMapping = new DomainSeriesMapping(null, "^Foo (\\d+)$", "Foo Series", true);

        _repository.Setup(r => r.CreateSeriesMapping(It.IsAny<DbSeriesMapping>()))
            .ReturnsAsync((DbSeriesMapping m) => new DbSeriesMapping(42, m.Regex, m.MappedSeries, m.WarnAboutPart));

        var result = await _service.CreateSeriesMapping(domainMapping);

        Assert.AreEqual(42, result.Id);
        Assert.AreEqual("^Foo (\\d+)$", result.Regex);
        Assert.AreEqual("Foo Series", result.MappedSeries);
        Assert.IsTrue(result.WarnAboutPart);

        _repository.Verify(r => r.CreateSeriesMapping(It.Is<DbSeriesMapping>(m =>
            m.Regex == "^Foo (\\d+)$" && m.MappedSeries == "Foo Series" && m.WarnAboutPart)), Times.Once);
    }

    [TestMethod]
    public async Task GetSeriesMapping_Found_ReturnsMappedDomainModel()
    {
        _repository.Setup(r => r.GetSeriesMapping(7))
            .ReturnsAsync(new DbSeriesMapping(7, "^Bar$", "Bar Series", false));

        var result = await _service.GetSeriesMapping(7);

        Assert.IsNotNull(result);
        Assert.AreEqual(7, result!.Id);
        Assert.AreEqual("^Bar$", result.Regex);
        Assert.AreEqual("Bar Series", result.MappedSeries);
        Assert.IsFalse(result.WarnAboutPart);
    }

    [TestMethod]
    public async Task GetSeriesMapping_NotFound_ReturnsNull()
    {
        _repository.Setup(r => r.GetSeriesMapping(999)).ReturnsAsync((DbSeriesMapping?)null);

        var result = await _service.GetSeriesMapping(999);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetSeriesMappings_ReturnsAllMappedToDomain()
    {
        _repository.Setup(r => r.GetSeriesMappings()).ReturnsAsync(new List<DbSeriesMapping>
        {
            new DbSeriesMapping(1, "^A$", "A Series", false),
            new DbSeriesMapping(2, "^B$", "B Series", true),
        });

        var result = await _service.GetSeriesMappings();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("A Series", result[0].MappedSeries);
        Assert.AreEqual("B Series", result[1].MappedSeries);
        Assert.IsTrue(result[1].WarnAboutPart);
    }

    [TestMethod]
    public async Task GetSeriesMappings_Empty_ReturnsEmptyList()
    {
        _repository.Setup(r => r.GetSeriesMappings()).ReturnsAsync(new List<DbSeriesMapping>());

        var result = await _service.GetSeriesMappings();

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task UpdateSeriesMapping_PersistsAndReturnsUpdatedValues()
    {
        var domainMapping = new DomainSeriesMapping(3, "^Updated$", "Updated Series", true);

        _repository.Setup(r => r.UpdateSeriesMapping(It.IsAny<DbSeriesMapping>()))
            .ReturnsAsync((DbSeriesMapping m) => m);

        var result = await _service.UpdateSeriesMapping(domainMapping);

        Assert.AreEqual(3, result.Id);
        Assert.AreEqual("^Updated$", result.Regex);
        Assert.AreEqual("Updated Series", result.MappedSeries);
        Assert.IsTrue(result.WarnAboutPart);

        _repository.Verify(r => r.UpdateSeriesMapping(It.Is<DbSeriesMapping>(m =>
            m.Id == 3 && m.Regex == "^Updated$" && m.MappedSeries == "Updated Series")), Times.Once);
    }

    [TestMethod]
    public async Task DeleteSeriesMapping_DelegatesToRepository()
    {
        await _service.DeleteSeriesMapping(5);

        _repository.Verify(r => r.DeleteSeriesMapping(5), Times.Once);
    }

    [TestMethod]
    public async Task GetLibrarySettings_ReturnsTheDbInitialsSpacing()
    {
        _librarySettingsRepository
            .Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(new DbLibrarySettings(1, DbInitialsSpacing.Spaced));

        var result = await _service.GetLibrarySettings();

        Assert.AreEqual(DomainInitialsSpacing.Spaced, result.InitialsSpacing);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_MapsDomainEnumToDbAndBack()
    {
        // The enum has no implicit conversion between layers, so a mixed-up mapping would throw
        // or silently write the wrong integer. Round-trip both values to pin the mapping.
        foreach (var spacing in new[] { DomainInitialsSpacing.Spaced, DomainInitialsSpacing.Unspaced })
        {
            _librarySettingsRepository
                .Setup(r => r.UpdateAsync(It.IsAny<DbInitialsSpacing>()))
                .ReturnsAsync((DbInitialsSpacing s) => new DbLibrarySettings(1, s));

            var result = await _service.UpdateLibrarySettings(new DomainLibrarySettings { InitialsSpacing = spacing });

            Assert.AreEqual(spacing, result.InitialsSpacing);
        }

        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Spaced), Times.Once);
        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Unspaced), Times.Once);
    }
}
