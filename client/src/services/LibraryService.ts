import BookFileInfo from "../types/BookFileInfo";
import { PaginatedResult } from "../types/Common";
import ManagedAudiobook from "../types/ManagedAudiobook";
import BaseHttpService from "./BaseHttpService";

class LibraryService extends BaseHttpService {
  getBooks(
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<ManagedAudiobook>> {
    return this.getData(`/library/audiobooks?limit=${limit}&offset=${offset}`);
  }

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
