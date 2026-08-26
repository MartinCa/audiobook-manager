import { MetadataSearchResult } from "../types/MetadataSearchResult";
import { MetadataMultiSourceSearchResult } from "../types/MetadataMultiSourceSearchResult";
import { MetadataSearchServiceInfo } from "../types/MetadataSearchServiceInfo";
import BaseHttpService from "./BaseHttpService";

class MetadataSearchService extends BaseHttpService {
  searchSource(
    source: string,
    searchTerm: string,
  ): Promise<MetadataSearchResult[]> {
    return this.getData(
      `/metadata-search/${source}?q=${encodeURIComponent(searchTerm)}`,
    );
  }

  searchMultiple(
    sources: string[],
    searchTerm: string,
  ): Promise<MetadataMultiSourceSearchResult> {
    return this.postData("/metadata-search/multi", { sources, q: searchTerm });
  }

  getBookDetails(bookPath: string): Promise<MetadataSearchResult> {
    return this.postData("/metadata-search/details", { path: bookPath });
  }

  getServices(): Promise<MetadataSearchServiceInfo[]> {
    return this.getData("/metadata-search/services");
  }
}

export default new MetadataSearchService();
