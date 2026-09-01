import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";

type LanguageOptionDto = Require<
  components["schemas"]["LanguageOptionDto"],
  "code" | "displayName" | "aliases"
>;

// AudiobookManager.Api/Dtos/LanguageDtos.cs: every field on both records is non-nullable.
export interface LanguageOption extends LanguageOptionDto {
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
