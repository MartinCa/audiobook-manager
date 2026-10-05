using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.FileManager;
using AudiobookManager.Services;
using AudiobookManager.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AudiobookManager.Test.Services;

/// <summary>
/// <see cref="AudiobookService.GenerateLibraryPathAsync"/> is what the organize, save, preview and
/// collision-check paths use: it has to read the library's narrator-in-path setting, not default it.
/// </summary>
[TestClass]
public class AudiobookServiceNarratorPathTests
{
    private static AudiobookService MakeService(bool includeNarratorInPath, int maxNarratorsInPath = 3)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetLibrarySettings())
            .ReturnsAsync(new LibrarySettings
            {
                IncludeNarratorInPath = includeNarratorInPath,
                MaxNarratorsInPath = maxNarratorsInPath,
            });

        return new AudiobookService(
            Mock.Of<IAudiobookTagHandler>(),
            new AudiobookFileHandler(new FileOperations()),
            new FileOperations(),
            Options.Create(new AudiobookManagerSettings { AudiobookLibraryPath = "/library" }),
            Mock.Of<IAudiobookRepository>(),
            Mock.Of<IPersonRepository>(),
            Mock.Of<IGenreRepository>(),
            Mock.Of<IBookConsistencyIssueRepository>(),
            Mock.Of<IPendingOnlineMatchRepository>(),
            new SeriesReconciliationCache(),
            settingsService.Object,
            NullLogger<AudiobookService>.Instance);
    }

    private static Audiobook MakeBook() =>
        new(new List<Person> { new("Author") }, "Title", 2020, new AudiobookFileInfo("/import/b.m4b", "b.m4b", 1))
        {
            Narrators = new List<Person> { new("Narrator A") },
        };

    [TestMethod]
    public async Task GenerateLibraryPathAsync_SettingOn_PutsTheNarratorInTheFolder()
    {
        var path = await MakeService(true).GenerateLibraryPathAsync(MakeBook());

        Assert.IsTrue(path.Contains("2020 - Title {Narrator A}"), path);
    }

    [TestMethod]
    public async Task GenerateLibraryPathAsync_SettingOn_HonoursTheConfiguredNarratorLimit()
    {
        var book = MakeBook();
        book.Narrators = new List<Person> { new("Narrator A"), new("Narrator B"), new("Narrator C") };

        var one = await MakeService(true, 1).GenerateLibraryPathAsync(book);
        var two = await MakeService(true, 2).GenerateLibraryPathAsync(book);

        Assert.IsTrue(one.Contains("{Narrator A}"), one);
        Assert.IsTrue(two.Contains("{Narrator A, Narrator B}"), two);
    }

    [TestMethod]
    public async Task GenerateLibraryPathAsync_SettingOff_IgnoresTheNarratorLimit()
    {
        var path = await MakeService(false, 5).GenerateLibraryPathAsync(MakeBook());

        Assert.IsFalse(path.Contains('{'), path);
    }

    [TestMethod]
    public async Task GenerateLibraryPathAsync_SettingOff_LeavesTheNarratorOut()
    {
        var path = await MakeService(false).GenerateLibraryPathAsync(MakeBook());

        Assert.IsFalse(path.Contains('{'), path);
    }

    [TestMethod]
    public async Task GenerateLibraryPathAsync_TwoNarrationsOfOneBook_DoNotCollideWhenTheSettingIsOn()
    {
        var service = MakeService(true);
        var other = MakeBook();
        other.Narrators = new List<Person> { new("Narrator B") };

        var first = await service.GenerateLibraryPathAsync(MakeBook());
        var second = await service.GenerateLibraryPathAsync(other);

        Assert.AreNotEqual(first, second);
    }
}
