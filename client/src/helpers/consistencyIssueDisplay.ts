// Icon mapping is identical wherever a consistency issue is rendered (per-item in BookDetail,
// per-group in LibraryConsistency); label text is intentionally NOT shared here since the two
// call sites use different grammatical number (e.g. "Missing Media File" vs "Missing Media
// Files" for a group) - see each caller's own getIssueTypeLabel.
export function getIssueIcon(issueType: string): string {
  switch (issueType) {
    case "MissingMediaFile":
      return "mdi-file-remove";
    case "WrongFilePath":
      return "mdi-swap-horizontal";
    case "MissingDescTxt":
    case "IncorrectDescTxt":
    case "MissingReaderTxt":
    case "IncorrectReaderTxt":
    case "MissingOpfFile":
    case "IncorrectOpfFile":
      return "mdi-text-box-remove";
    case "MissingCoverFile":
      return "mdi-image-remove";
    case "TagMismatch":
      return "mdi-tag-off";
    default:
      return "mdi-alert";
  }
}
