export interface SimilarValueCandidate {
  value: string;
  bookCount: number;
  audiobookIds: number[];
}

export interface SimilarValueGroup {
  candidates: SimilarValueCandidate[];
}
