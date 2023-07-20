import { BookSearchResult } from "../types/BookSearchResult";
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
}

export default new SearchService();
