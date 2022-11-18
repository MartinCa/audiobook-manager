using System.Reflection;
using AudiobookManager.Database.EntityMappings;
using AudiobookManager.Database.Models;
using AudiobookManager.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AudiobookManager.Database;
public class DatabaseContext : DbContext
{
    private readonly AudiobookManagerSettings _settings;

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
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = _settings?.DbLocation ?? "testdb.db";
        var connectionString = $"Data Source={dbPath}";
        optionsBuilder.UseSqlite(connectionString, options => options.MigrationsAssembly(Assembly.GetExecutingAssembly().FullName))
            .UseSnakeCaseNamingConvention();
    }

    public DbSet<SeriesMapping> SeriesMappings { get; set; }
    public DbSet<Audiobook> Audiobooks { get; set; }
    public DbSet<Genre> Genres { get; set; }
    public DbSet<Person> Persons { get; set; }
}
