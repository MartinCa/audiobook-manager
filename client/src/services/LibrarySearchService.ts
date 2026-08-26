import CombinedSearchResult from "../types/CombinedSearchResult";
import BaseHttpService from "./BaseHttpService";

class LibrarySearchService extends BaseHttpService {
  combinedSearch(query: string, limit = 5): Promise<CombinedSearchResult> {
    return this.getData(
      `/browse/search?q=${encodeURIComponent(query)}&limit=${limit}`,
    );
  }
}

export default new LibrarySearchService();
