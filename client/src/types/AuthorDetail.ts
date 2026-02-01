import AuthorSummary from "./AuthorSummary";
import ManagedAudiobook from "./ManagedAudiobook";

export interface SeriesInfo {
  seriesName: string;
  bookCount: number;
}

export default interface AuthorDetail {
  author: AuthorSummary;
  series: SeriesInfo[];
  standaloneBooks: ManagedAudiobook[];
}
