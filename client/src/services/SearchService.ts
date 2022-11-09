import BookFileInfo from "../types/BookFileInfo";
import { BookSearchResult } from "../types/BookSearchResult";
import BaseHttpService from "./BaseHttpService";

const reAudible = /audible\.com/i;
const reGoodreads = /goodreads\.com/i;

class SearchService extends BaseHttpService {
  searchSource(source: string, searchTerm: string): Promise<BookSearchResult[]> {
    return this.getData(`/search/${source}?q=${encodeURIComponent(searchTerm)}`)
  }

  getBookDetails(bookPath: string): Promise<BookSearchResult> {
    if (reAudible.test(bookPath)) {
      return this.postData("/search/audible/details", { path: bookPath });
    } else {
      return this.postData("/search/goodreads/details", { path: bookPath });
    }
  }
}

export default new SearchService();
