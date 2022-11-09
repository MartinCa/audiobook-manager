import { PaginatedResult } from "../types/Common";
import ManagedAudiobook from "../types/ManagedAudiobook";
import BaseHttpService from "./BaseHttpService";


class LibraryService extends BaseHttpService {
  getBooks(limit: number, offset: number): Promise<PaginatedResult<ManagedAudiobook>> {
    return this.getData(`/library/audiobooks?limit=${limit}&offset=${offset}`);
  }
}

export default new LibraryService();
