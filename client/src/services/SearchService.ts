import { BookSearchResult } from "../types/BookSearchResult";
import { MultiSourceSearchResult } from "../types/MultiSourceSearchResult";
import { SearchServiceInfo } from "../types/SearchServiceInfo";
import BaseHttpService from "./BaseHttpService";

class SearchService extends BaseHttpService {
  searchSource(
    source: string,
    searchTerm: string,
  ): Promise<BookSearchResult[]> {
    return this.getData(
      `/search/${source}?q=${encodeURIComponent(searchTerm)}`,
    );
  }

  searchMultiple(
    sources: string[],
    searchTerm: string,
  ): Promise<MultiSourceSearchResult> {
    return this.postData("/search/multi", { sources, q: searchTerm });
  }

  getBookDetails(bookPath: string): Promise<BookSearchResult> {
    return this.postData("/search/details", { path: bookPath });
  }

  getServices(): Promise<SearchServiceInfo[]> {
    return this.getData("/search/services");
  }
}

export default new SearchService();
