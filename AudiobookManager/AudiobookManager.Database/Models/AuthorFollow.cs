using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// Marks one <see cref="Person"/> as followed for upcoming releases. One row per person; the
/// worker polls only persons with a follow row (and a Hardcover match) for new releases.
/// </summary>
[Table("author_follows")]
public class AuthorFollow
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("person_id")]
    public long PersonId { get; set; }

    public Person Person { get; set; } = null!;

    [Required]
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
