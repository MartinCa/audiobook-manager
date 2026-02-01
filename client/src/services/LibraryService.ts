import BookFileInfo from "../types/BookFileInfo";
import { PaginatedResult } from "../types/Common";
import BaseHttpService from "./BaseHttpService";

class LibraryService extends BaseHttpService {
  startLibraryScan(): Promise<void> {
    return this.postData("/library/scan");
  }

  getDiscoveredBooks(
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<BookFileInfo>> {
    return this.getData(`/library/discovered?limit=${limit}&offset=${offset}`);
  }

  deleteDiscoveredBook(path: string): Promise<void> {
    return this.delete(`/library/discovered?path=${encodeURIComponent(path)}`);
  }
}

export default new LibraryService();
