using System.ComponentModel.DataAnnotations;

namespace AudiobookManager.Api.Dtos;

/// <summary>
/// The chosen value per differing tag field for a selective tag-mismatch resolution. The key is
/// the <see cref="TagMismatchFieldDto.Field"/> name; the value is the serialized value to apply
/// (null/empty clears the field). Only fields present here are touched — anything omitted keeps
/// the library metadata. Structural fields (<see cref="TagMismatchFields.StructuralFields"/>:
/// Author, Book Name, Year) cannot be cleared; sending null/empty for one is rejected as a 400.
/// </summary>
public class ResolveTagMismatchRequest
{
    [Required]
    public Dictionary<string, string?> FieldValues { get; set; } = new();
}
