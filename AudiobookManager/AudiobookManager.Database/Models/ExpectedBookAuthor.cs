using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AudiobookManager.Database.Models;

/// <summary>
/// One author attribution of an <see cref="ExpectedBook"/>. <see cref="AuthorName"/> is always
/// stored so a source author with no local <see cref="Person"/> row is not lost; <see cref="PersonId"/>
/// is set when the name can be resolved to a library person. A book carries one link per distinct
/// author credited by the source.
/// </summary>
[Table("expected_book_authors")]
public class ExpectedBookAuthor
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [Column("expected_book_id")]
    public long ExpectedBookId { get; set; }

    public ExpectedBook ExpectedBook { get; set; } = null!;

    [Column("person_id")]
    public long? PersonId { get; set; }

    public Person? Person { get; set; }

    [Required]
    [Column("author_name")]
    public string AuthorName { get; set; } = string.Empty;
}