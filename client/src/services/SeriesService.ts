import {
  SeriesDetail,
  SeriesExpectedBook,
  SeriesMatchCandidate,
  SeriesOverview,
} from "../types/Series";
import BaseHttpService from "./BaseHttpService";

class SeriesService extends BaseHttpService {
  getAllSeries(): Promise<SeriesOverview[]> {
    return this.getData("/series");
  }

  getSeriesDetail(seriesName: string): Promise<SeriesDetail> {
    return this.getData(`/series/${encodeURIComponent(seriesName)}`);
  }

  getMatchCandidates(seriesName: string): Promise<SeriesMatchCandidate[]> {
    return this.getData(
      `/series/${encodeURIComponent(seriesName)}/match-candidates`,
    );
  }

  searchMatchCandidates(
    seriesName: string,
    query: string,
  ): Promise<SeriesMatchCandidate[]> {
    return this.getData(
      `/series/${encodeURIComponent(seriesName)}/match-candidates/search?query=${encodeURIComponent(query)}`,
    );
  }

  matchSeries(
    seriesName: string,
    sourceName: string,
    sourceId: string,
    confidence?: number,
  ): Promise<SeriesOverview> {
    return this.postData(`/series/${encodeURIComponent(seriesName)}/match`, {
      sourceName,
      sourceId,
      confidence,
    });
  }

  startBulkMatch(
    confidenceThreshold: number,
    seriesNames?: string[],
  ): Promise<void> {
    return this.postData("/series/match/bulk", {
      confidenceThreshold,
      seriesNames,
    });
  }

  startRefreshSeries(seriesName: string): Promise<void> {
    return this.postData(`/series/${encodeURIComponent(seriesName)}/refresh`);
  }

  startRefreshAll(): Promise<void> {
    return this.postData("/series/refresh-all");
  }

  // Roster entries are addressed by their natural key (position and/or title within the
  // series), not by row id: the backend deletes and re-inserts the whole roster on every
  // match/refresh, so a cached id can point at a different book by the time it is used.
  ignoreExpectedBook(
    seriesName: string,
    book: SeriesExpectedBook,
  ): Promise<void> {
    return this.postData(
      `/series/${encodeURIComponent(seriesName)}/expected-books/ignore`,
      { position: book.position, title: book.title },
    );
  }

  unignoreExpectedBook(
    seriesName: string,
    book: SeriesExpectedBook,
  ): Promise<void> {
    return this.postData(
      `/series/${encodeURIComponent(seriesName)}/expected-books/unignore`,
      { position: book.position, title: book.title },
    );
  }
}

export default new SeriesService();
