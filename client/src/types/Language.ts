export interface LanguageOption {
  code: string;
  displayName: string;
  /**
   * Every lowercased spelling that folds to `code`, served by the backend so the client's fold
   * matches `Languages.Normalize` exactly instead of reimplementing the table.
   */
  aliases: string[];
}

export interface LanguageOptions {
  languages: LanguageOption[];
  defaultCode: string;
}
