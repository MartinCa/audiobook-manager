// Mirrors AudiobookManager.Domain.SourceNamesMainRule / SourceSilentRule, which the server serves by
// name; the lists of options (with their labels and explanations) come from
// GET api/settings/metadata-choice-rules rather than being held here.
export type SourceNamesMainRule = "KeepCurrent" | "UseSources" | "AskMe";

export type SourceSilentRule = "KeepCurrent" | "FirstListed" | "None" | "AskMe";

/** The two settings of one choice: one for when the source names a main one, one for when it is silent. */
export interface ChoiceRule {
  whenSourceNamesMain: SourceNamesMainRule;
  whenSourceSilent: SourceSilentRule;
}

// Hand-written because the generated DTO types every string as `string | null`; the shapes are
// AudiobookManager.Api.Dtos.MetadataChoiceDto / MetadataChoiceRulesDto, whose members are all plain
// non-nullable record properties except Warning.
export interface MetadataChoiceOption {
  key: string;
  label: string;
  description: string;
}

export interface MetadataChoice {
  /** The backend choice key: "MainAuthor" or "PrimarySeries". */
  choice: string;
  label: string;
  description: string;
  whenSourceNamesMain: SourceNamesMainRule;
  whenSourceSilent: SourceSilentRule;
  namesMainOptions: MetadataChoiceOption[];
  silentOptions: MetadataChoiceOption[];
  warning?: string | null;
}

export interface MetadataChoiceRules {
  choices: MetadataChoice[];
}

// Body of PUT api/settings/metadata-choice-rules; a choice left out keeps its stored rules.
export interface UpdateMetadataChoiceRules {
  rules: {
    choice: string;
    whenSourceNamesMain: SourceNamesMainRule;
    whenSourceSilent: SourceSilentRule;
  }[];
}
