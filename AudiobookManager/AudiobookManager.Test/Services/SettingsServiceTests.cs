using AudiobookManager.Database.Repositories;
using AudiobookManager.Services;
using Moq;
using DbLibrarySettings = AudiobookManager.Database.Models.LibrarySettings;
using DbInitialsSpacing = AudiobookManager.Database.Models.InitialsSpacing;
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
                .Setup(r => r.UpdateAsync(It.IsAny<DbInitialsSpacing>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<string>()))
                .ReturnsAsync((DbInitialsSpacing s, int delayMs, bool enabled, string cron) =>
                    new DbLibrarySettings(1, s, delayMs, enabled, cron));

            var result = await _service.UpdateLibrarySettings(
                new DomainLibrarySettings
                {
                    InitialsSpacing = spacing,
                    MetadataRefreshDelayMs = 2500,
                    UpcomingReleasesEnabled = true,
                    UpcomingReleasesCronSchedule = "0 3 * * *",
                });

            Assert.AreEqual(spacing, result.InitialsSpacing);
        }

        // The delay rides along with the spacing through the same update - not a second write path.
        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Spaced, 2500, true, "0 3 * * *"), Times.Once);
        _librarySettingsRepository.Verify(
            r => r.UpdateAsync(DbInitialsSpacing.Unspaced, 2500, true, "0 3 * * *"), Times.Once);
    }
}
