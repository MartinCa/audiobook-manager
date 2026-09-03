import { toast } from "sonner";
import type { ConsistencyResolveResult } from "@/types/ConsistencyIssue";
import type { OrphanDirectoryResolveResult } from "@/types/OrphanDirectory";

export const ISSUE_TYPE_LABELS: Record<string, string> = {
  MissingMediaFile: "Missing Media Files",
  WrongFilePath: "Wrong File Paths",
  MissingDescTxt: "Missing Description Files",
  IncorrectDescTxt: "Incorrect Description Files",
  MissingReaderTxt: "Missing Reader Files",
  IncorrectReaderTxt: "Incorrect Reader Files",
  MissingCoverFile: "Missing Cover Files",
  MissingOpfFile: "Missing OPF Files",
  IncorrectOpfFile: "Incorrect OPF Files",
  TagMismatch: "Tag Mismatches",
};

export function getIssueTypeLabel(issueType: string): string {
  return ISSUE_TYPE_LABELS[issueType] ?? issueType;
}

export const BULK_RESOLVE_DESCRIPTIONS: Record<string, string> = {
  WrongFilePath:
    "Each audiobook file will be moved to its correct location based on library metadata.",
  MissingDescTxt:
    "A desc.txt sidecar file containing the book description will be created or updated for each affected book.",
  IncorrectDescTxt:
    "A desc.txt sidecar file containing the book description will be created or updated for each affected book.",
  MissingReaderTxt:
    "A reader.txt sidecar file containing narrator information will be created or updated for each affected book.",
  IncorrectReaderTxt:
    "A reader.txt sidecar file containing narrator information will be created or updated for each affected book.",
  MissingCoverFile: "The cover image will be extracted from each affected audiobook file.",
  MissingOpfFile: "A metadata.opf sidecar file will be created or updated for each affected book.",
  IncorrectOpfFile:
    "A metadata.opf sidecar file will be created or updated for each affected book.",
  TagMismatch:
    "Each audiobook file's m4b tags will be rewritten to match the library metadata (author, series, series part, year, etc.), and the file relocated if that changes its path.",
};

export const ISSUE_TYPE_INFO: Record<string, string> = {
  TagMismatch:
    "Each book's m4b tags differ from its library metadata. Resolve opens a dialog to choose, field by field, whether to keep the library value, the file's value, or clear the field. Bulk resolve rewrites every tag to match the library and moves the file if that changes its path.",
};

export function getIssueTypeInfo(issueType: string): string {
  return ISSUE_TYPE_INFO[issueType] ?? getBulkResolveDescription(issueType);
}

export function getBulkResolveDescription(issueType: string): string {
  return BULK_RESOLVE_DESCRIPTIONS[issueType] ?? "Continue?";
}

export function notifyConsistencyResolveResult(result: ConsistencyResolveResult): void {
  if (result.actionTaken === "file_recovered") {
    toast.info(
      result.message ||
        "Media file found on disk. Preserved audiobook and refreshed consistency status.",
    );
  } else if (result.actionTaken === "audiobook_deleted") {
    toast.success(result.message || "Audiobook removed from library");
  } else {
    toast.success(result.message || "Issue resolved");
  }
}

export function notifyOrphanResolveResult(result: OrphanDirectoryResolveResult): void {
  if (result.actionTaken === "retained_has_audio") {
    toast.info(
      result.message ||
        "Directory contains audio files; preserved on disk and removed from orphan list.",
    );
  } else {
    toast.success(result.message || "Orphan directory deleted from disk");
  }
}
