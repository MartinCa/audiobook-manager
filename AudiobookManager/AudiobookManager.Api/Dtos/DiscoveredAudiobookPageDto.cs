using AudiobookManager.Database.Models;

namespace AudiobookManager.Api.Dtos;

public class DiscoveredAudiobookPageDto
{
    public int Count { get; set; }
    public int Total { get; set; }
    public int WellTaggedTotal { get; set; }
    public IList<DiscoveredAudiobookDto> Items { get; set; }

    public DiscoveredAudiobookPageDto(
        int count,
        int total,
        int wellTaggedTotal,
        IList<DiscoveredAudiobookDto> items)
    {
        Count = count;
        Total = total;
        WellTaggedTotal = wellTaggedTotal;
        Items = items;
    }
}
