export interface MissingTagField {
  key: string;
  label: string;
  isCriticalByDefault: boolean;
}

export interface AudiobookMissingTags {
  audiobookId: number;
  bookName: string;
  authors: string[];
  missingFields: string[];
}
