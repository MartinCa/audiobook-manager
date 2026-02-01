import AuthorDetail from "../types/AuthorDetail";
import AuthorSummary from "../types/AuthorSummary";
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

  searchBooks(
    query: string,
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<ManagedAudiobook>> {
    return this.getData(
      `/library/audiobooks/search?q=${encodeURIComponent(query)}&limit=${limit}&offset=${offset}`,
    );
  }

  getAuthors(): Promise<AuthorSummary[]> {
    return this.getData("/library/authors");
  }

  getAuthorDetail(authorId: number): Promise<AuthorDetail> {
    return this.getData(`/library/authors/${authorId}`);
  }

  getSeriesBooks(
    seriesName: string,
    authorId?: number,
  ): Promise<ManagedAudiobook[]> {
    let url = `/library/series/${encodeURIComponent(seriesName)}`;
    if (authorId !== undefined) {
      url += `?authorId=${authorId}`;
    }
    return this.getData(url);
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
