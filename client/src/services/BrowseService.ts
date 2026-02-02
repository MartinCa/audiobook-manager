import AudiobookDetail from "../types/AudiobookDetail";
import AuthorDetail from "../types/AuthorDetail";
import AuthorSummary from "../types/AuthorSummary";
import { PaginatedResult } from "../types/Common";
import ManagedAudiobook from "../types/ManagedAudiobook";
import BaseHttpService from "./BaseHttpService";

class BrowseService extends BaseHttpService {
  getBooks(
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<ManagedAudiobook>> {
    return this.getData(`/browse/audiobooks?limit=${limit}&offset=${offset}`);
  }

  searchBooks(
    query: string,
    limit: number,
    offset: number,
  ): Promise<PaginatedResult<ManagedAudiobook>> {
    return this.getData(
      `/browse/audiobooks/search?q=${encodeURIComponent(query)}&limit=${limit}&offset=${offset}`,
    );
  }

  getAuthors(): Promise<AuthorSummary[]> {
    return this.getData("/browse/authors");
  }

  getAuthorDetail(authorId: number): Promise<AuthorDetail> {
    return this.getData(`/browse/authors/${authorId}`);
  }

  getBookDetail(id: number): Promise<AudiobookDetail> {
    return this.getData(`/browse/audiobooks/${id}`);
  }

  getSeriesBooks(
    seriesName: string,
    authorId?: number,
  ): Promise<ManagedAudiobook[]> {
    let url = `/browse/series/${encodeURIComponent(seriesName)}`;
    if (authorId !== undefined) {
      url += `?authorId=${authorId}`;
    }
    return this.getData(url);
  }
}

export default new BrowseService();
