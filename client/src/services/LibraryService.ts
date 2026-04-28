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
    search?: string,
  ): Promise<PaginatedResult<BookFileInfo>> {
    const params = new URLSearchParams({
      limit: String(limit),
      offset: String(offset),
    });
    if (search) params.set("search", search);
    return this.getData(`/library/discovered?${params}`);
  }

  deleteDiscoveredBook(path: string): Promise<void> {
    return this.delete(`/library/discovered?path=${encodeURIComponent(path)}`);
  }
}

export default new LibraryService();
