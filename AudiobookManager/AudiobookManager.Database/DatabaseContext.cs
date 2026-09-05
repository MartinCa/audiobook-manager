using System.Reflection;
using AudiobookManager.Database.EntityMappings;
using AudiobookManager.Database.Models;
using AudiobookManager.Database.Search;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Database;
public class DatabaseContext : DbContext
{
    private readonly AudiobookManagerSettings? _settings;

    public DatabaseContext()
    {
    }

    public DatabaseContext(DbContextOptions<DatabaseContext> dbOptions, IOptions<AudiobookManagerSettings> settings) : base(dbOptions)
    {
        _settings = settings.Value;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SeriesMappingMapping).Assembly);
        modelBuilder.HasDbFunction(typeof(AccentFolding).GetMethod(nameof(AccentFolding.Fold))!)
            .HasName(AccentFolding.SqlFunctionName);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = _settings?.DbLocation ?? "testdb.db";
        var connectionStringBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 30
        };
        optionsBuilder.UseSqlite(connectionStringBuilder.ToString(), options => options.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new AccentFoldingConnectionInterceptor(), new SqlitePragmaInterceptor(), new AccentFoldedColumnsInterceptor());
    }

    public DbSet<SeriesMapping> SeriesMappings { get; set; }
    public DbSet<LibrarySettings> LibrarySettings { get; set; }
    public DbSet<Audiobook> Audiobooks { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<Person> Persons { get; set; }
    public DbSet<QueuedOrganizeTask> QueuedOrganizeTasks { get; set; }
    public DbSet<DiscoveredAudiobook> DiscoveredAudiobooks { get; set; }
    public DbSet<ConsistencyIssue> ConsistencyIssues { get; set; }
    public DbSet<OrphanDirectory> OrphanDirectories { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<SeriesExpectedBook> SeriesExpectedBooks { get; set; }
    public DbSet<HardcoverRequestQuota> HardcoverRequestQuotas { get; set; }
}
