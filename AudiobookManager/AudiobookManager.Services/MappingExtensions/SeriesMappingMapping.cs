namespace AudiobookManager.Services.MappingExtensions;
public static class SeriesMappingMapping
{
    public static Domain.SeriesMapping ToDomain(this Database.Models.SeriesMapping dbModel) => new Domain.SeriesMapping(dbModel.Id, dbModel.Regex, dbModel.MappedSeries, dbModel.WarnAboutPart);

    public static Database.Models.SeriesMapping ToDb(this Domain.SeriesMapping domainModel) => new Database.Models.SeriesMapping(domainModel.Id ?? default, domainModel.Regex, domainModel.MappedSeries, domainModel.WarnAboutParth);
}
