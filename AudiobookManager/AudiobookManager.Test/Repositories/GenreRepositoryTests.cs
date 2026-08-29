using AudiobookManager.Database;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Repositories;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Test.Repositories;

[TestClass]
public class GenreRepositoryTests
{
    private string _dbPath = null!;
    private DatabaseContext _db = null!;
    private GenreRepository _repository = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"genrerepo-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
        _db = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
        _db.Database.EnsureCreated();
        _repository = new GenreRepository(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [TestMethod]
    public async Task GetOrCreateGenres_CreatesMissingAndReusesExisting()
    {
        var first = await _repository.GetOrCreateGenres(new[] { "Fantasy", "Sci-Fi" });
        var second = await _repository.GetOrCreateGenres(new[] { "Fantasy", "Horror" });

        Assert.AreEqual(first["Fantasy"].Id, second["Fantasy"].Id);
        Assert.AreEqual(3, await _db.Genres.CountAsync());
    }

    // Regression: genres.name had no unique index and GetOrCreateGenres had none of the
    // read-then-insert race handling its PersonRepository twin has, so two concurrent organizes
    // that both saw a genre as missing each inserted their own row. The name is now unique, and
    // the loser of that race adopts the winner's row instead of failing the whole save.
    [TestMethod]
    public async Task GetOrCreateGenres_ConcurrentFirstWrites_AllSucceedAndCreateOneRowPerName()
    {
        var contexts = new List<DatabaseContext>();

        try
        {
            var settings = Options.Create(new AudiobookManagerSettings { DbLocation = _dbPath });
            var calls = new List<Task<Dictionary<string, Genre>>>();
            for (var i = 0; i < 8; i++)
            {
                // A context per caller, as each request scope gets its own.
                var context = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings);
                contexts.Add(context);
                var repository = new GenreRepository(context);
                calls.Add(Task.Run(() => repository.GetOrCreateGenres(new[] { "Fantasy", "Sci-Fi" })));
            }

            var results = await Task.WhenAll(calls);

            var stored = await _db.Genres.AsNoTracking().ToListAsync();
            CollectionAssert.AreEquivalent(
                new[] { "Fantasy", "Sci-Fi" },
                stored.Select(g => g.Name).ToArray());

            // Every caller must come back with the row that actually landed, not a phantom.
            var fantasyId = stored.Single(g => g.Name == "Fantasy").Id;
            foreach (var result in results)
            {
                Assert.AreEqual(fantasyId, result["Fantasy"].Id);
            }
        }
        finally
        {
            foreach (var context in contexts)
            {
                context.Dispose();
            }
        }
    }

    // The unique index cannot be created over a database that already holds duplicates, so the
    // migration collapses them first. Rebuilt here through the migrator rather than
    // EnsureCreated, because EnsureCreated skips migrations entirely.
    [TestMethod]
    public async Task AddUniqueGenreNameIndexMigration_CollapsesPreExistingDuplicateGenres()
    {
        var migratedPath = Path.Combine(Path.GetTempPath(), $"genredup-{Guid.NewGuid():N}.db");
        var settings = Options.Create(new AudiobookManagerSettings { DbLocation = migratedPath });

        try
        {
            using (var seed = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings))
            {
                // Migrate up to (but not including) the index, then plant the duplicates the
                // index would reject.
                await seed.Database.MigrateAsync("AddLanguageToDiscoveredAudiobooks");

                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO genres (id, name) VALUES (1, 'Fantasy'), (2, 'Fantasy'), (3, 'Sci-Fi');");
                await seed.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO audiobooks (id, book_name, year, file_info_full_path, file_info_file_name, file_info_size_in_bytes)
                    VALUES (10, 'Book A', 2024, '/library/a.m4b', 'a.m4b', 1), (11, 'Book B', 2024, '/library/b.m4b', 'b.m4b', 1);
                    """);
                // Book A links to both duplicates (the link through the duplicate must be
                // dropped, not repointed); Book B links only to the duplicate (repointed).
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO audiobook_genre (books_id, genres_id) VALUES (10, 1), (10, 2), (11, 2), (11, 3);");
            }

            using (var migrated = new DatabaseContext(new DbContextOptions<DatabaseContext>(), settings))
            {
                await migrated.Database.MigrateAsync();

                var genres = await migrated.Genres.AsNoTracking().OrderBy(g => g.Id).ToListAsync();
                CollectionAssert.AreEqual(
                    new[] { "Fantasy", "Sci-Fi" },
                    genres.Select(g => g.Name).ToArray());

                var books = await migrated.Audiobooks.AsNoTracking()
                    .Include(a => a.Genres)
                    .OrderBy(a => a.Id)
                    .ToListAsync();

                CollectionAssert.AreEqual(
                    new[] { "Fantasy" },
                    books[0].Genres.Select(g => g.Name).OrderBy(n => n).ToArray());
                CollectionAssert.AreEqual(
                    new[] { "Fantasy", "Sci-Fi" },
                    books[1].Genres.Select(g => g.Name).OrderBy(n => n).ToArray());
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(migratedPath))
            {
                File.Delete(migratedPath);
            }
        }
    }
}
