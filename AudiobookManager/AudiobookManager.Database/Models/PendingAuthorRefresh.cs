using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// A name the matched source uses for an author that differs from the library's spelling, stored
/// until the user accepts (renaming the author and every book) or dismisses it - the author
/// counterpart of <see cref="PendingSeriesRefresh"/>. At most one row per author: the next
/// refresh overwrites it, and a refresh that finds the names agreeing (including after the
/// library's initials convention is applied to the source's name) deletes it.
///
/// The proposed name is stored already formatted to the library's initials convention, so
/// accepting it cannot introduce a name the initials-spacing consistency check would flag.
/// </summary>
[Table("pending_author_refresh")]
public class PendingAuthorRefresh
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("person_id")]
    public long PersonId { get; set; }

    public Person Person { get; set; } = null!;

    /// <summary>The name the source reports, formatted to the library's initials convention.</summary>
    [Required]
    [Column("proposed_name")]
    public string ProposedName { get; set; } = string.Empty;

    /// <summary>Scraper source the name came from (e.g. "Hardcover").</summary>
    [Required]
    [Column("source_name")]
    public string SourceName { get; set; } = string.Empty;

    /// <summary>The source's author page, when the source reported one.</summary>
    [Column("source_url")]
    public string? SourceUrl { get; set; }

    /// <summary>UTC timestamp of the fetch that produced this proposal.</summary>
    [Required]
    [Column("fetched_at")]
    public DateTime FetchedAt { get; set; }
}
