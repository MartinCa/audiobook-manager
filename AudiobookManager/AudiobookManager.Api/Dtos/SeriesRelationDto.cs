namespace AudiobookManager.Api.Dtos;

/// <summary>A series a book belongs to besides its primary one, with the book's optional part in it.</summary>
public class SeriesRelationDto
{
    [System.ComponentModel.DataAnnotations.Required]
    public string SeriesName { get; set; } = string.Empty;

    public string? SeriesPart { get; set; }
}
