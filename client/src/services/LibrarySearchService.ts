import LibrarySearchResult from "../types/LibrarySearchResult";
import BaseHttpService from "./BaseHttpService";

class LibrarySearchService extends BaseHttpService {
  searchLibrary(query: string, limit = 5): Promise<LibrarySearchResult> {
    return this.getData(
      `/browse/library-search?q=${encodeURIComponent(query)}&limit=${limit}`,
    );
  }
}

export default new LibrarySearchService();
