using AudiobookManager.Database;
using AudiobookManager.Database.Search;
using AudiobookManager.Scraping;
using AudiobookManager.Scraping.Scrapers;
using AudiobookManager.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AudiobookManager.Test.Search;

/// <summary>
/// Guards against exactly the drift risk <see cref="MetadataSourceResolution"/>'s doc comment
/// warns about: its <see cref="MetadataSourceResolution.KnownSources"/> table is a hand-maintained
/// duplicate of each real <see cref="IScraper"/>'s domain (it cannot call the scrapers directly -
/// see that class's doc). If a scraper's domain ever changed, or a new scraper were added, without
/// a matching update here, every book from that source would silently fall into the book-list
/// filter's "Unsupported" bucket. This resolves the real scraper graph (the same
/// <see cref="DependencyInjection.SetupScraping"/> registration Program.cs uses) rather than
/// comparing the table to a second hardcoded copy of itself.
/// </summary>
[TestClass]
public class MetadataSourceResolutionScraperParityTests
{
    private static IReadOnlyList<IScraper> ResolveRegisteredScrapers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AudiobookManagerSettings>(_ => { });
        // BookSeriesMapper (behind IBookSeriesMapper, which every scraper takes) needs a
        // DatabaseContext - never actually queried here, since SourceName/SupportsUrl touch no DB.
        services.SetupDatabase();
        services.SetupScraping();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetServices<IScraper>().ToList();
    }

    [TestMethod]
    public void KnownSources_HasExactlyOneEntryPerRegisteredScraper()
    {
        var scrapers = ResolveRegisteredScrapers();

        Assert.IsTrue(scrapers.Count > 0, "expected at least one registered IScraper");

        var scraperSourceNames = scrapers.Select(s => s.SourceName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var knownSourceNames = MetadataSourceResolution.KnownSources
            .Select(k => k.SourceName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(scraperSourceNames, knownSourceNames,
            "MetadataSourceResolution.KnownSources must have exactly one entry per registered IScraper - " +
            "otherwise a scraper's books can never resolve to anything but \"Unsupported\", or the filter " +
            "dropdown offers a source no book can ever match.");
    }

    [TestMethod]
    public void KnownSources_EachDomainIsActuallyAcceptedByItsScrapersSupportsUrl()
    {
        var scrapers = ResolveRegisteredScrapers();
        var scrapersBySourceName = scrapers.ToDictionary(s => s.SourceName);

        foreach (var (sourceName, domain) in MetadataSourceResolution.KnownSources)
        {
            Assert.IsTrue(scrapersBySourceName.TryGetValue(sourceName, out var scraper),
                $"KnownSources names a source (\"{sourceName}\") with no registered IScraper of that SourceName.");

            var probeUrl = $"https://{domain}/probe";
            Assert.IsTrue(scraper!.SupportsUrl(probeUrl),
                $"KnownSources says \"{sourceName}\" is at \"{domain}\", but {scraper.GetType().Name}.SupportsUrl " +
                "disagrees - the two have drifted apart.");

            // And the resolver itself must actually produce that scraper's SourceName for the
            // same URL - the end-to-end behavior the book-list filter depends on.
            Assert.AreEqual(sourceName, MetadataSourceResolution.ResolvePlain(probeUrl));
        }
    }
}
