import { SeriesMapping, SeriesMappingBase } from "../types/SeriesMapping";
import BaseHttpService from "./BaseHttpService";

const seriesMappingBaseUrl = "/settings/series_mappings/"

class SettingsService extends BaseHttpService {
    getSeriesMappings(): Promise<SeriesMapping[]> {
        return this.getData(seriesMappingBaseUrl);
    }

    deleteSeriesMapping(id: number): Promise<void> {
        return this.delete(`${seriesMappingBaseUrl}${id}`);
    }

    updateSeriesMapping(mapping: SeriesMapping): Promise<SeriesMapping> {
        return this.putData(`${seriesMappingBaseUrl}${mapping.id}`, mapping);
    }

    createSeriesMapping(mapping: SeriesMappingBase): Promise<SeriesMapping> {
        return this.postData(seriesMappingBaseUrl, mapping);
    }
}

export default new SettingsService();
