using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Domain;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Models;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using DbAudiobook = AudiobookManager.Database.Models.Audiobook;
using DbPerson = AudiobookManager.Database.Models.Person;
using DomainAudiobook = AudiobookManager.Domain.Audiobook;
using DomainPerson = AudiobookManager.Domain.Person;

namespace AudiobookManager.Test.Services;

/// <summary>
/// The main author / primary series choice rules through the refresh service: what an unattended run
/// settles, what it leaves for a person, and what an explicit pick on a stored row does.
/// </summary>
[TestClass]
public class MetadataRefreshChoiceTests
{
    private readonly Mock<IAudiobookRepository> _audiobookRepository = new();
    private readonly Mock<IPendingMetadataRefreshRepository> _pendingRepository = new();
    private readonly Mock<IPendingOnlineMatchRepository> _pendingOnlineMatchRepository = new();
    private readonly Mock<IBookConsistencyIssueRepository> _issueRepository = new();
    private readonly Mock<IScrapingService> _scrapingService = new();
    private readonly Mock<ILibrarySettingsRepository> _librarySettingsRepository = new();
    private readonly Mock<IAudiobookService> _audiobookService = new();
    private readonly IAudiobookSaveGate _saveGate = new AudiobookSaveGate();
    private readonly Mock<IBookSeriesMapper> _bookSeriesMapper = new();
    private readonly Mock<IServiceScopeFactory> _serviceScopeFactory = new();
    private readonly Mock<ILogger<MetadataRefreshService>> _logger = new();

    private readonly List<DomainAudiobook> _saved = new();
    private PendingMetadataRefresh? _stored;
    private DbAudiobook _book = null!;

    public MetadataRefreshChoiceTests()
    {
        _bookSeriesMapper
            .Setup(m => m.MapBookSeries(It.IsAny<IList<MetadataSeriesSearchResult>>()))
            .Returns<IList<MetadataSeriesSearchResult>>(results => Task.FromResult(results));
    }

    private MetadataRefreshService CreateService(IEnumerable<IScraper>? scrapers = null) =>
        new(
            _audiobookRepository.Object, _pendingRepository.Object, _pendingOnlineMatchRepository.Object,
            _issueRepository.Object, _scrapingService.Object, scrapers ?? Array.Empty<IScraper>(),
            _librarySettingsRepository.Object, _audiobookService.Object, _saveGate, _bookSeriesMapper.Object,
            _serviceScopeFactory.Object, _logger.Object);

    private static string FieldRules(params (string Field, AutomatedApplyRule Automated)[] rules) =>
        MetadataApplyRuleSet.From(rules.ToDictionary(
            r => r.Field, r => new FieldApplyRule(InteractiveApplyRule.AlwaysSelect, r.Automated))).Serialize()!;

    private static string ChoiceRules(string kind, SourceNamesMainRule named = SourceNamesMainRule.KeepCurrent, SourceSilentRule silent = SourceSilentRule.KeepCurrent) =>
        MetadataChoiceRuleSet.From(new Dictionary<string, ChoiceRule> { [kind] = new(named, silent) }).Serialize()!;

    /// <summary>Authors and Series settle on their own rules; everything else is already identical.</summary>
    private static string SettlingRules => FieldRules(
        (MetadataRefreshFields.Authors, AutomatedApplyRule.OverwriteUnlessSourceEmpty),
        (MetadataRefreshFields.Series, AutomatedApplyRule.OverwriteUnlessSourceEmpty));

    private void SetUp(
        string[] bookAuthors, string? bookMainAuthor, string[] bookSeries, MetadataSearchResult fetched,
        string? applyRulesJson, string? choiceRulesJson)
    {
        _book = new DbAudiobook(
            42, "A Book", null, bookSeries.FirstOrDefault(), null, 2024,
            "Same description", null, null, "en", null, null, "https://www.audible.com/pd/whatever", null, null,
            "/library/book.m4b", "book.m4b", 1000);
        for (var i = 1; i < bookSeries.Length; i++)
        {
            _book.SeriesRelations!.Add(new AudiobookSeries { SeriesName = bookSeries[i], IsPrimary = false, SortOrder = i });
        }

        _book.Authors = bookAuthors.Select((n, i) => new DbPerson(i + 1, n)).ToList();
        _book.MainAuthorId = _book.Authors.FirstOrDefault(a => a.Name == bookMainAuthor)?.Id;

        _audiobookRepository.Setup(r => r.GetByIdsWithIncludesAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<DbAudiobook> { _book });
        _audiobookRepository.Setup(r => r.GetByIdWithIncludesAsync(42)).ReturnsAsync(_book);
        _audiobookRepository.Setup(r => r.UpdateLastMetadataRefreshedAtAsync(42, It.IsAny<DateTime>())).Returns(Task.CompletedTask);
        _librarySettingsRepository.Setup(r => r.GetOrCreateAsync())
            .ReturnsAsync(new Database.Models.LibrarySettings
            {
                MetadataRefreshDelayMs = 0,
                MetadataApplyRulesJson = applyRulesJson,
                MetadataChoiceRulesJson = choiceRulesJson,
            });

        _scrapingService.Setup(s => s.GetBookDetails(_book.Www!)).ReturnsAsync(fetched);

        _pendingRepository.Setup(r => r.UpsertAsync(It.IsAny<PendingMetadataRefresh>()))
            .Callback<PendingMetadataRefresh>(row => _stored = row)
            .ReturnsAsync((PendingMetadataRefresh row) => row);
        _pendingRepository.Setup(r => r.GetByAudiobookIdAsync(42)).ReturnsAsync(() => _stored);
        _pendingRepository.Setup(r => r.DeleteByAudiobookIdAsync(42)).ReturnsAsync(true);

        _audiobookService.Setup(s => s.UpdateAudiobook(42, It.IsAny<DomainAudiobook>()))
            .Callback<long, DomainAudiobook, Func<string, int, Task>?>((_, a, _) => _saved.Add(a))
            .ReturnsAsync((long _, DomainAudiobook a, Func<string, int, Task>? _) => a);

        var consistency = new Mock<ILibraryConsistencyService>();
        consistency.Setup(s => s.RecheckAudiobookAsync(42)).ReturnsAsync(new List<BookConsistencyIssue>());
        var provider = new Mock<IServiceProvider>();
        provider.Setup(sp => sp.GetService(typeof(ILibraryConsistencyService))).Returns(consistency.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        _serviceScopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
    }

    private MetadataSearchResult Source(
        string[] authors, string? mainAuthor = null, (string Name, string? Part)[]? series = null, string? mainSeries = null) =>
        new(_book?.Www ?? "https://www.audible.com/pd/whatever", "A Book")
        {
            Source = "Audible",
            Year = 2024,
            Language = "en",
            Description = "Same description",
            Authors = authors.Select(a => new DomainPerson(a)).ToList(),
            MainAuthor = mainAuthor,
            Narrators = new List<DomainPerson>(),
            Genres = new List<string>(),
            Series = series?.Select(s => new MetadataSeriesSearchResult(s.Name) { SeriesPart = s.Part }).ToList(),
            MainSeries = mainSeries,
        };

    private IScraper Scraper()
    {
        var scraper = new Mock<IScraper>();
        scraper.Setup(s => s.SupportsUrl(It.IsAny<string>())).Returns(true);
        scraper.Setup(s => s.RequiresApiKey).Returns(false);
        return scraper.Object;
    }

    private static Task NoProgress(int a, int b, int c, int d) => Task.CompletedTask;

    private async Task BulkRefreshAsync() =>
        await CreateService(new[] { Scraper() }).RefreshSelectedAudiobooksAsync(new List<long> { 42 }, NoProgress);

    private void Setup(string[] authors, string? main, string[] series, Func<MetadataSearchResult> fetched, string? applyRules, string? choiceRules)
    {
        // The fetched result needs the book's URL, which exists only once SetUp built the book.
        SetUp(authors, main, series, new MetadataSearchResult("https://www.audible.com/pd/whatever", "A Book"), applyRules, choiceRules);
        _scrapingService.Setup(s => s.GetBookDetails(_book.Www!)).ReturnsAsync(fetched());
    }

    #region Main author - unattended runs

    [TestMethod]
    public async Task Bulk_SourceNamesAMainAuthor_UseSources_SetsItWithoutReview()
    {
        Setup(["A One", "B Two"], null, [], () => Source(["A One", "B Two"], mainAuthor: "A One"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources));

        await BulkRefreshAsync();

        Assert.AreEqual(1, _saved.Count, "the Authors field settled on its own rule, so it was applied");
        Assert.AreEqual("A One", _saved[0].MainAuthor);
    }

    [TestMethod]
    public async Task Bulk_SourceNamesAMainAuthor_DefaultRules_ChangeNothing()
    {
        // Same book and source as above, but the choice rules are untouched: the source's designation is
        // ignored, there is nothing to apply, and no pending row is created for it.
        Setup(["A One", "B Two"], null, [], () => Source(["A One", "B Two"], mainAuthor: "A One"),
            SettlingRules, choiceRules: null);

        await BulkRefreshAsync();

        Assert.AreEqual(0, _saved.Count);
        _pendingRepository.Verify(r => r.UpsertAsync(It.IsAny<PendingMetadataRefresh>()), Times.Never);
    }

    [TestMethod]
    public async Task Bulk_SourceNamesAMainAuthor_KeepCurrent_LeavesTheBooksOwn()
    {
        Setup(["A One", "B Two"], "B Two", [], () => Source(["A One", "B Two"], mainAuthor: "A One"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.KeepCurrent));

        await BulkRefreshAsync();

        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public async Task Bulk_AuthorsChange_WithMainAuthorOnAskMe_IsLeftForReview_AndNothingIsApplied()
    {
        // The source adds an author and names a main one that differs from the book's: a real decision.
        Setup(["A One"], "A One", [], () => Source(["A One", "B Two"], mainAuthor: "B Two"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.AskMe));

        await BulkRefreshAsync();

        Assert.AreEqual(0, _saved.Count, "an Ask me choice sends the whole book to review");
        _pendingRepository.Verify(r => r.UpsertAsync(It.Is<PendingMetadataRefresh>(p => p.AudiobookId == 42)), Times.Once);
    }

    [TestMethod]
    public async Task Bulk_AuthorsChange_WithDefaultChoiceRules_IsStillAppliedAutomatically_AsBefore()
    {
        Setup(["A One"], "A One", [], () => Source(["A One", "B Two"], mainAuthor: "B Two"),
            SettlingRules, choiceRules: null);

        await BulkRefreshAsync();

        Assert.AreEqual(1, _saved.Count);
        Assert.AreEqual("A One", _saved[0].MainAuthor, "keep current: the book's main author survives the author list growing");
        CollectionAssert.AreEqual(new[] { "A One", "B Two" }, _saved[0].Authors.Select(a => a.Name).ToArray());
    }

    [TestMethod]
    public async Task Bulk_AskMeWhenSilent_OnlyAsksWhenTheBookWouldBeLeftWithNoMainAuthor()
    {
        var rules = ChoiceRules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.AskMe);

        // The book has no main author: the changeset is left for a person.
        Setup(["A One"], null, [], () => Source(["A One", "B Two"]), SettlingRules, rules);
        await BulkRefreshAsync();
        Assert.AreEqual(0, _saved.Count);

        // The book has one that the source still credits: nothing to decide, it is applied.
        _saved.Clear();
        Setup(["A One"], "A One", [], () => Source(["A One", "B Two"]), SettlingRules, rules);
        await BulkRefreshAsync();
        Assert.AreEqual(1, _saved.Count);
    }

    [TestMethod]
    public async Task Bulk_AnAskMeChoice_OnlyMattersWhenItsFieldIsBeingApplied()
    {
        // Authors is on Keep current, so it is declined and the Ask me choice never comes into play.
        Setup(["A One"], null, [], () => Source(["A One", "B Two"]),
            FieldRules((MetadataRefreshFields.Authors, AutomatedApplyRule.KeepCurrent)),
            ChoiceRules(MetadataChoiceKinds.MainAuthor, silent: SourceSilentRule.AskMe));

        await BulkRefreshAsync();

        Assert.AreEqual(0, _saved.Count);
        _pendingRepository.Verify(r => r.UpsertAsync(It.IsAny<PendingMetadataRefresh>()), Times.Never);
    }

    [TestMethod]
    public async Task SingleBookRefresh_IsAlwaysAReview_AndReportsTheMainAuthorOnlyChangeAsAnAuthorsDifference()
    {
        Setup(["A One", "B Two"], null, [], () => Source(["A One", "B Two"], mainAuthor: "A One"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources));

        var result = await CreateService(new[] { Scraper() }).RefreshAudiobookAsync(42);

        var diff = result.Differences.Single();
        Assert.AreEqual(MetadataRefreshFields.Authors, diff.Field);
        StringAssert.Contains(diff.LibraryValue!, "no main author");
        StringAssert.Contains(diff.SourceValue!, "main: A One");
        Assert.AreEqual(0, _saved.Count);
    }

    #endregion

    #region Primary series - unattended runs

    private static readonly (string, string?)[] CosmereAndSaga = [("Saga", "1"), ("Cosmere", "3")];

    [TestMethod]
    public async Task Bulk_SourceNamesAMainSeries_UseSources_MakesItPrimary()
    {
        Setup(["A One"], null, ["Saga"], () => Source(["A One"], series: CosmereAndSaga, mainSeries: "Cosmere"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.UseSources));

        await BulkRefreshAsync();

        Assert.AreEqual(1, _saved.Count);
        Assert.AreEqual("Cosmere", _saved[0].Series);
        Assert.AreEqual("Saga", _saved[0].AdditionalSeries!.Single().Name);
    }

    [TestMethod]
    public async Task Bulk_SourceNamesAMainSeries_DefaultRules_KeepTheBooksPrimaryAsAlways()
    {
        Setup(["A One"], null, ["Saga"], () => Source(["A One"], series: CosmereAndSaga, mainSeries: "Cosmere"),
            SettlingRules, choiceRules: null);

        await BulkRefreshAsync();

        Assert.AreEqual(1, _saved.Count);
        Assert.AreEqual("Saga", _saved[0].Series);
        Assert.AreEqual("Cosmere", _saved[0].AdditionalSeries!.Single().Name);
    }

    [TestMethod]
    public async Task Bulk_SourceNamesAMainSeries_AskMe_IsLeftForReviewWhenItDiffersFromThePrimary()
    {
        Setup(["A One"], null, ["Saga"], () => Source(["A One"], series: CosmereAndSaga, mainSeries: "Cosmere"),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.AskMe));

        await BulkRefreshAsync();

        Assert.AreEqual(0, _saved.Count);
        _pendingRepository.Verify(r => r.UpsertAsync(It.IsAny<PendingMetadataRefresh>()), Times.Once);
    }

    [TestMethod]
    public async Task Bulk_SourceSilent_FirstListed_UsesTheSourcesFirstInItsOwnOrderWhenNoneOfTheBooksSeriesSurvives()
    {
        // "Zeta" sorts after "Alpha" but was listed first: the source's order is what "first listed" means.
        Setup(["A One"], null, ["Elsewhere"],
            () => Source(["A One"], series: [("Zeta", "1"), ("Alpha", "2")]),
            SettlingRules, ChoiceRules(MetadataChoiceKinds.PrimarySeries, silent: SourceSilentRule.FirstListed));

        await BulkRefreshAsync();

        Assert.AreEqual(1, _saved.Count);
        Assert.AreEqual("Zeta", _saved[0].Series);
    }

    #endregion

    #region Explicit picks on a stored row

    private async Task<DomainAudiobook> ApplyStoredAsync(
        string[] bookAuthors, string? bookMain, MetadataSearchResult source, string? choiceRulesJson,
        string? mainAuthorName, string? primarySeriesName = null, string[]? bookSeries = null)
    {
        Setup(bookAuthors, bookMain, bookSeries ?? [], () => source, applyRules: null, choiceRulesJson);
        _stored = new PendingMetadataRefresh
        {
            AudiobookId = 42,
            FetchedAt = DateTime.UtcNow,
            PayloadJson = PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(source)),
            ChangedFieldsJson = "[]",
        };

        var applied = await CreateService().ApplyPendingRefreshAsync(
            42, new[] { MetadataRefreshFields.Authors, MetadataRefreshFields.Series },
            splitTitleOnColon: false, primarySeriesName, replaceExisting: false, mainAuthorName);

        Assert.IsTrue(applied);
        return _saved.Single();
    }

    [TestMethod]
    public async Task Apply_AnExplicitMainAuthor_IsUsedWhateverTheRulesSay()
    {
        var saved = await ApplyStoredAsync(
            ["A One"], null, Source(["A One", "B Two"], mainAuthor: "A One"),
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.KeepCurrent), mainAuthorName: "B Two");

        Assert.AreEqual("B Two", saved.MainAuthor);
    }

    [TestMethod]
    public async Task Apply_AnExplicitEmptyMainAuthor_MeansNone_EvenWhenTheRulesWouldSetOne()
    {
        var saved = await ApplyStoredAsync(
            ["A One", "B Two"], "B Two", Source(["A One", "B Two"], mainAuthor: "A One"),
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources), mainAuthorName: "");

        Assert.IsNull(saved.MainAuthor);
    }

    [TestMethod]
    public async Task Apply_NoExplicitPick_FollowsTheRules_AndAnAskMeRuleFallsBackToTheBooksOwn()
    {
        // A bulk apply cannot ask: Ask me with nobody to ask applies the book's own main author.
        var saved = await ApplyStoredAsync(
            ["A One", "B Two"], "B Two", Source(["A One", "B Two"], mainAuthor: "A One"),
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.AskMe), mainAuthorName: null);

        Assert.AreEqual("B Two", saved.MainAuthor);
    }

    [TestMethod]
    public async Task Apply_AnExplicitPrimarySeries_BeatsTheRules()
    {
        var saved = await ApplyStoredAsync(
            ["A One"], null, Source(["A One"], series: CosmereAndSaga, mainSeries: "Cosmere"),
            ChoiceRules(MetadataChoiceKinds.PrimarySeries, named: SourceNamesMainRule.UseSources),
            mainAuthorName: null, primarySeriesName: "Saga", bookSeries: ["Saga"]);

        Assert.AreEqual("Saga", saved.Series);
    }

    [TestMethod]
    public async Task Apply_AMainAuthorThatIsNotAnAuthor_IsIgnored()
    {
        var saved = await ApplyStoredAsync(
            ["A One"], null, Source(["A One", "B Two"]), choiceRulesJson: null, mainAuthorName: "Nobody");

        Assert.IsNull(saved.MainAuthor);
    }

    #endregion

    #region Backlog: re-evaluate and settle by rules

    private void StorePendingRow(MetadataSearchResult source)
    {
        _stored = new PendingMetadataRefresh
        {
            AudiobookId = 42,
            FetchedAt = DateTime.UtcNow,
            PayloadJson = PendingRefreshPayload.Serialize(PendingRefreshPayload.FromSearchResult(source)),
            ChangedFieldsJson = "[]",
        };
        _pendingRepository.Setup(r => r.GetPendingAudiobookIdsAsync()).ReturnsAsync(new List<long> { 42 });
        _pendingRepository.Setup(r => r.GetByAudiobookIdsAsync(It.IsAny<IReadOnlyList<long>>()))
            .ReturnsAsync(new List<PendingMetadataRefresh> { _stored });
        _pendingRepository.Setup(r => r.UpdatePayloadAndChangedFieldsAsync(42, It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    [TestMethod]
    public async Task SettleByRules_APendingRowWhoseChoiceIsAskMe_IsLeftAloneForAPerson()
    {
        var source = Source(["A One", "B Two"], mainAuthor: "B Two");
        Setup(["A One"], "A One", [], () => source, SettlingRules,
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.AskMe));
        StorePendingRow(source);

        var result = await CreateService().ApplyPendingByRulesAsync(NoProgress);

        Assert.AreEqual(0, result.Processed, "a row that waits for a person is neither a success nor a failure");
        Assert.AreEqual(0, _saved.Count);
    }

    [TestMethod]
    public async Task SettleByRules_APendingRowTheChoiceRulesSettle_IsAppliedWithTheirMainAuthor()
    {
        var source = Source(["A One", "B Two"], mainAuthor: "B Two");
        Setup(["A One"], "A One", [], () => source, SettlingRules,
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources));
        StorePendingRow(source);

        var result = await CreateService().ApplyPendingByRulesAsync(NoProgress);

        Assert.AreEqual(1, result.Succeeded);
        Assert.AreEqual("B Two", _saved.Single().MainAuthor);
    }

    [TestMethod]
    public async Task Reevaluate_CountsARowAsSettleableOnlyWhenNoChoiceWantsAPerson()
    {
        var source = Source(["A One", "B Two"], mainAuthor: "B Two");

        Setup(["A One"], "A One", [], () => source, SettlingRules,
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.AskMe));
        StorePendingRow(source);
        var asking = await CreateService().ReevaluatePendingRefreshesAsync();

        Setup(["A One"], "A One", [], () => source, SettlingRules,
            ChoiceRules(MetadataChoiceKinds.MainAuthor, named: SourceNamesMainRule.UseSources));
        StorePendingRow(source);
        var settling = await CreateService().ReevaluatePendingRefreshesAsync();

        Assert.AreEqual(0, asking.Settleable);
        Assert.AreEqual(1, settling.Settleable);
    }

    #endregion
}
