// Mirrors AudiobookManager.Domain.InteractiveApplyRule / AutomatedApplyRule, which the server
// serves by name; the lists of options (with their labels and explanations) come from
// GET api/settings/metadata-apply-rules rather than being held here.
export type InteractiveApplyRule =
  "AlwaysSelect" | "NeverSelect" | "SelectIfEmpty" | "SelectIfSourceHasValue";

export type AutomatedApplyRule =
  "AskMe" | "AlwaysOverwrite" | "FillBlanksOnly" | "OverwriteUnlessSourceEmpty" | "KeepCurrent";

// Hand-written because the generated DTO types every string as `string | null`; the shapes are
// AudiobookManager.Api.Dtos.MetadataApplyFieldDto / MetadataApplyOptionDto / MetadataApplyRulesDto,
// whose members are all plain non-nullable record properties except AlwaysOverwriteWarning.
export interface MetadataApplyField {
  /** The backend `MetadataRefreshFields` name, e.g. "BookName". */
  field: string;
  label: string;
  interactive: InteractiveApplyRule;
  automated: AutomatedApplyRule;
  alwaysOverwriteAllowed: boolean;
  alwaysOverwriteWarning?: string | null;
}

export interface MetadataApplyOption {
  key: string;
  label: string;
  description: string;
}

export interface MetadataApplyRules {
  fields: MetadataApplyField[];
  interactiveOptions: MetadataApplyOption[];
  automatedOptions: MetadataApplyOption[];
}

// Body of PUT api/settings/metadata-apply-rules; a field left out keeps its stored rules.
export interface UpdateMetadataApplyRules {
  rules: { field: string; interactive: InteractiveApplyRule; automated: AutomatedApplyRule }[];
}
