export interface SeriesMappingBase {
  regex: string;
  mappedSeries: string;
  warnAboutPart: boolean;
}

export interface SeriesMapping extends SeriesMappingBase {
  id: number;
}

export interface GroupMapping {
  mappedSeries: string;
  mappings: SeriesMapping[];
}
