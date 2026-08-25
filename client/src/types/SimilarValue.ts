export interface SimilarValueBook {
  id: number;
  bookName: string;
}

export interface SimilarValueCandidate {
  value: string;
  bookCount: number;
  books: SimilarValueBook[];
}

export interface SimilarValueGroup {
  candidates: SimilarValueCandidate[];
}
