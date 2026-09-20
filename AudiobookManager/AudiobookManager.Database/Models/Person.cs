using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

[Table("persons")]
public class Person
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; }

    // Accent-folded shadow column, see Audiobook.BookNameFolded for why it is app-maintained
    // rather than SQLite-computed. Kept in sync by AccentFoldedColumnsInterceptor.
    [Column("name_folded")]
    public string? NameFolded { get; set; }

    public List<Audiobook> BooksAuthored { get; set; }

    public List<Audiobook> BooksNarrated { get; set; }

    /// <summary>
    /// The metadata source this person is matched to (e.g. "Hardcover" - currently the only
    /// source with author-lookup support, see IUpcomingReleaseService.AuthorLookupScraper), or
    /// null when unmatched. Mirrors <see cref="Series.MatchedSourceName"/>'s shape - a matched
    /// author is what lets the upcoming-releases worker poll that source for this person's
    /// future books.
    /// </summary>
    [Column("matched_source_name")]
    public string? MatchedSourceName { get; set; }

    /// <summary>The source-specific author identifier, e.g. a Hardcover author id.</summary>
    [Column("matched_source_id")]
    public string? MatchedSourceId { get; set; }

    [Column("matched_source_url")]
    public string? MatchedSourceUrl { get; set; }

    /// <summary>
    /// When this author's standalone-books roster (<see cref="AuthorExpectedBook"/>) was last
    /// refreshed from the matched source - the author-roster counterpart of
    /// <see cref="Series.LastRefreshedAt"/>. Null until the first refresh.
    /// </summary>
    [Column("last_refreshed_at")]
    public DateTime? LastRefreshedAt { get; set; }

    public List<AuthorExpectedBook> ExpectedBooks { get; set; } = new();

    public Person(long id, string name)
    {
        Id = id;
        Name = name;

        BooksAuthored = new List<Audiobook>();
        BooksNarrated = new List<Audiobook>();
    }
}
