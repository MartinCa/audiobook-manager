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
}

export default new LibraryService();
