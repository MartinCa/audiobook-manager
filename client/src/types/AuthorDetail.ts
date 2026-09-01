import type AuthorSummary from "./AuthorSummary";
import type ManagedAudiobook from "./ManagedAudiobook";

export interface SeriesInfo {
  seriesName: string;
  bookCount: number;
}

export default interface AuthorDetail {
  author: AuthorSummary;
  series: SeriesInfo[];
  standaloneBooks: ManagedAudiobook[];
}
