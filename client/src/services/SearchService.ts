import { BookSearchResult } from "../types/BookSearchResult";
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

  getBookDetails(bookPath: string): Promise<BookSearchResult> {
    return this.postData("/search/details", { path: bookPath });
  }

  getServices(): Promise<SearchServiceInfo[]> {
    return this.getData("/search/services");
  }
}

export default new SearchService();
