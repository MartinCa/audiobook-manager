using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbLibrarySettings = AudiobookManager.Database.Models.LibrarySettings;
using DbInitialsSpacing = AudiobookManager.Database.Models.InitialsSpacing;
using DbInitialsPunctuation = AudiobookManager.Database.Models.InitialsPunctuation;
using DomainLibrarySettings = AudiobookManager.Domain.LibrarySettings;
using DomainInitialsSpacing = AudiobookManager.Domain.InitialsSpacing;

namespace AudiobookManager.Test.Services;

/// <summary>
/// SettingsService covers the UI-editable library settings (currently the initials-spacing
/// preference plus the metadata-refresh delay). Series-mapping CRUD used to live here under the
/// "Settings page" surface; it is now owned by Series (see SeriesServiceTests) along with its
/// management UI on the series detail page.
/// </summary>
[TestClass]
public class SettingsServiceTests
{
    private Mock<ILibrarySettingsRepository> _librarySettingsRepository = null!;
    private SettingsService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _librarySettingsRepository = new Mock<ILibrarySettingsRepository>();
        _service = new SettingsService(_librarySettingsRepository.Object);
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
                .Setup(r => r.UpdateAsync(
                    It.IsAny<DbInitialsSpacing>(), It.IsAny<DbInitialsPunctuation>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AudiobookManager.Database.Models.SearchInitialsHandling>()))
                .ReturnsAsync((DbInitialsSpacing s, DbInitialsPunctuation p, int delayMs, bool enabled, string cron, int pageSize, AudiobookManager.Database.Models.SearchInitialsHandling search) =>
                    new DbLibrarySettings(1, s, p, delayMs, enabled, cron, pageSize, search));

            var result = await _service.UpdateLibrarySettings(
                new DomainLibrarySettings
                {
                    InitialsSpacing = spacing,
                    MetadataRefreshDelayMs = 2500,
                    UpcomingReleasesEnabled = true,
                    UpcomingReleasesCronSchedule = "0 3 * * *",
                    DefaultPageSize = 50,
                });

            Assert.AreEqual(spacing, result.InitialsSpacing);
        }

        // The delay rides along with the spacing through the same update - not a second write path.
        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Spaced, DbInitialsPunctuation.Dotted, 2500, true, "0 3 * * *", 50, AudiobookManager.Database.Models.SearchInitialsHandling.AsStored), Times.Once);
        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Unspaced, DbInitialsPunctuation.Dotted, 2500, true, "0 3 * * *", 50, AudiobookManager.Database.Models.SearchInitialsHandling.AsStored), Times.Once);
    }

    [TestMethod]
    public async Task UpdateLibrarySettings_MapsEverySearchInitialsHandlingBothWays()
    {
        _librarySettingsRepository
            .Setup(r => r.UpdateAsync(
                It.IsAny<DbInitialsSpacing>(), It.IsAny<DbInitialsPunctuation>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<AudiobookManager.Database.Models.SearchInitialsHandling>()))
            .ReturnsAsync((DbInitialsSpacing s, DbInitialsPunctuation p, int delayMs, bool enabled, string cron, int pageSize, AudiobookManager.Database.Models.SearchInitialsHandling search) =>
                new DbLibrarySettings(1, s, p, delayMs, enabled, cron, pageSize, search));

        foreach (var handling in Enum.GetValues<AudiobookManager.Domain.SearchInitialsHandling>())
        {
            var result = await _service.UpdateLibrarySettings(
                new DomainLibrarySettings { SearchInitialsHandling = handling });

            Assert.AreEqual(handling, result.SearchInitialsHandling);
            Assert.AreEqual(handling.ToString(), ((AudiobookManager.Database.Models.SearchInitialsHandling)(int)handling).ToString(),
                "Domain and database enums must keep the same names so the stored integer means the same thing");
        }
    }
}
