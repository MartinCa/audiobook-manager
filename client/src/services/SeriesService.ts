import {
  SeriesDetail,
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

  ignoreExpectedBook(expectedBookId: number): Promise<void> {
    return this.postData(`/series/expected-books/${expectedBookId}/ignore`);
  }

  unignoreExpectedBook(expectedBookId: number): Promise<void> {
    return this.delete(`/series/expected-books/${expectedBookId}/ignore`);
  }
}

export default new SeriesService();
