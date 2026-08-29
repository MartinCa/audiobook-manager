import {
  SeriesDetail,
  SeriesExpectedBook,
  SeriesMatchCandidate,
  SeriesOverview,
} from "../types/Series";
import BaseHttpService from "./BaseHttpService";

// The series name goes in the query string, never in the path. A series value is a raw m4b tag,
// so it can contain a "/" - and ASP.NET Core leaves %2F encoded in a path segment rather than
// decoding it, so such a series was listed on the overview and then 404'd the moment it was
// opened. See the note on SeriesController.
const nameQuery = (seriesName: string) =>
  `seriesName=${encodeURIComponent(seriesName)}`;

class SeriesService extends BaseHttpService {
  getAllSeries(): Promise<SeriesOverview[]> {
    return this.getData("/series");
  }

  getSeriesDetail(seriesName: string): Promise<SeriesDetail> {
    return this.getData(`/series/detail?${nameQuery(seriesName)}`);
  }

  getMatchCandidates(seriesName: string): Promise<SeriesMatchCandidate[]> {
    return this.getData(`/series/match-candidates?${nameQuery(seriesName)}`);
  }

  searchMatchCandidates(
    seriesName: string,
    query: string,
  ): Promise<SeriesMatchCandidate[]> {
    return this.getData(
      `/series/match-candidates/search?${nameQuery(seriesName)}&query=${encodeURIComponent(query)}`,
    );
  }

  matchSeries(
    seriesName: string,
    sourceName: string,
    sourceId: string,
    confidence?: number,
    includeOmnibusEditions?: boolean,
  ): Promise<SeriesOverview> {
    return this.postData(`/series/match?${nameQuery(seriesName)}`, {
      sourceName,
      sourceId,
      confidence,
      includeOmnibusEditions,
    });
  }

  setIncludeOmnibusEditions(
    seriesName: string,
    includeOmnibusEditions: boolean,
  ): Promise<SeriesOverview> {
    return this.postData(
      `/series/include-omnibus-editions?${nameQuery(seriesName)}`,
      { includeOmnibusEditions },
    );
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
    return this.postData(`/series/refresh?${nameQuery(seriesName)}`);
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
      `/series/expected-books/ignore?${nameQuery(seriesName)}`,
      { position: book.position, title: book.title },
    );
  }

  unignoreExpectedBook(
    seriesName: string,
    book: SeriesExpectedBook,
  ): Promise<void> {
    return this.postData(
      `/series/expected-books/unignore?${nameQuery(seriesName)}`,
      { position: book.position, title: book.title },
    );
  }
}

export default new SeriesService();
