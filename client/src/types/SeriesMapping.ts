export interface SeriesMappingBase {
    regex: string,
    mapped_series: string,
    warn_about_part: boolean
}

export interface SeriesMapping extends SeriesMappingBase {
    id: number
}

export interface GroupMapping {
    mapped_series: string,
    mappings: SeriesMapping[]
}
