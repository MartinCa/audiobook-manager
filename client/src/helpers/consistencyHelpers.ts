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
  UnreadableFile: "Unreadable Files",
  LibraryPathUnavailable: "Library Path Unavailable",
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
  UnreadableFile:
    "Each affected file will be read again. Files that can be read are re-checked normally; files that still cannot be read stay listed. Nothing is deleted or modified.",
  LibraryPathUnavailable:
    "Each affected book's directory will be looked for again. Books whose directory is back are re-checked normally; books whose directory is still missing stay listed. Nothing is deleted or modified.",
};

export const ISSUE_TYPE_INFO: Record<string, string> = {
  UnreadableFile:
    "The media file exists but could not be read \u2014 most often a corrupt or incompletely copied m4b, or one the application does not have permission to open. There is nothing to repair from here, so resolving simply reads the file again: if it works, the book is re-checked normally; if not, it stays listed. The library record is never deleted, unlike a missing media file.",
  LibraryPathUnavailable:
    "The media file is missing and so is its directory \u2014 the shape of an unmounted drive or share rather than a deleted book (deleting a book leaves its directory behind). The library record is kept and nothing is deleted: resolving simply looks again, and the book is re-checked normally once the directory is back.",
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
  if (result.actionTaken === "still_unreadable") {
    // Not a success: the resolve ran, found the file no more readable than before, and left the
    // issue in place. A green "Issue resolved" would say the opposite of what happened.
    toast.warning(
      result.message ||
        "The media file still cannot be read. It is most likely corrupt, incompletely copied, or not readable by the user this application runs as.",
    );
  } else if (result.actionTaken === "file_readable") {
    toast.success(
      result.message || "The media file can be read again. Refreshed consistency status.",
    );
  } else if (result.actionTaken === "file_recovered") {
    toast.info(
      result.message ||
        "Media file found on disk. Preserved audiobook and refreshed consistency status.",
    );
  } else if (result.actionTaken === "directory_still_unavailable") {
    // Not a success: the directory is still gone, so the issue stays in place. Warn, exactly
    // like still_unreadable - the record was preserved, but nothing was fixed.
    toast.warning(
      result.message ||
        "The media file's directory still cannot be found. It is most likely an unmounted drive or share - check the mount and re-run.",
    );
  } else if (result.actionTaken === "directory_readable_again") {
    toast.success(
      result.message ||
        "The media file's directory is available again. Refreshed consistency status.",
    );
  } else if (result.actionTaken === "directory_unavailable") {
    // A stored MissingMediaFile re-resolved against a now-missing directory: the record was
    // preserved (a share may have died), but the finding changed. Info, not success.
    toast.info(
      result.message ||
        "Media file not found and its directory is also unavailable - the record was kept, but if the book really is gone, delete it once the directory is back.",
    );
  } else if (result.actionTaken === "audiobook_deleted") {
    toast.success(result.message || "Audiobook removed from library");
  } else {
    toast.success(result.message || "Issue resolved");
  }
}

export function notifyOrphanResolveResult(result: OrphanDirectoryResolveResult): void {
  if (result.actionTaken === "retained_not_empty") {
    toast.info(
      result.message ||
        "Directory still contains files; preserved on disk and removed from orphan list.",
    );
  } else {
    toast.success(result.message || "Orphan directory deleted from disk");
  }
}
