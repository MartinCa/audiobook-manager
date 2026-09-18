import { describe, it, expect, vi, beforeEach } from "vitest";
import { notifications } from "@/lib/notifications";
import {
  getIssueTypeLabel,
  getIssueTypeInfo,
  getBulkResolveDescription,
  notifyConsistencyResolveResult,
  notifyOrphanResolveResult,
} from "./consistencyHelpers";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

describe("consistencyHelpers", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getIssueTypeLabel", () => {
    it("returns formatted label for known issue types", () => {
      expect(getIssueTypeLabel("MissingMediaFile")).toBe("Missing Media Files");
      expect(getIssueTypeLabel("WrongFilePath")).toBe("Wrong File Paths");
      expect(getIssueTypeLabel("TagMismatch")).toBe("Tag Mismatches");
    });

    it("falls back to raw string for unknown issue types", () => {
      expect(getIssueTypeLabel("UnknownIssueType")).toBe("UnknownIssueType");
    });
  });

  describe("getIssueTypeInfo", () => {
    it("describes the selective tag-mismatch dialog for TagMismatch", () => {
      expect(getIssueTypeInfo("TagMismatch")).toContain("dialog to choose");
      expect(getIssueTypeInfo("TagMismatch")).toContain("field by field");
      expect(getIssueTypeInfo("TagMismatch")).toContain("library value");
    });

    it("falls back to the bulk resolve description for other issue types", () => {
      expect(getIssueTypeInfo("WrongFilePath")).toBe(getBulkResolveDescription("WrongFilePath"));
    });
  });

  describe("getBulkResolveDescription", () => {
    it("returns description for known issue types", () => {
      expect(getBulkResolveDescription("MissingCoverFile")).toContain("cover image");
    });

    it("returns the bulk rewrite description for TagMismatch", () => {
      expect(getBulkResolveDescription("TagMismatch")).toContain(
        "rewritten to match the library metadata",
      );
    });

    it("falls back to default string for unknown issue types", () => {
      expect(getBulkResolveDescription("UnknownIssueType")).toBe("Continue?");
    });
  });

  describe("SeriesPartMismatch", () => {
    it("has a label, a bulk description and its own explanatory info", () => {
      expect(getIssueTypeLabel("SeriesPartMismatch")).toBe("Series Part Mismatches");
      expect(getBulkResolveDescription("SeriesPartMismatch")).toContain(
        "overwritten with the position its matched series assigns it",
      );
      // Its own entry rather than a generic "Continue?".
      expect(getIssueTypeInfo("SeriesPartMismatch")).not.toBe("Continue?");
    });
  });

  describe("notifyConsistencyResolveResult", () => {
    it("shows info toast when actionTaken is file_recovered", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "file_recovered",
        message: "File found on disk",
      });

      expect(notifications.info).toHaveBeenCalledWith("File found on disk");
      expect(notifications.success).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is audiobook_deleted", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "audiobook_deleted",
        message: "Audiobook deleted from library",
      });

      expect(notifications.success).toHaveBeenCalledWith("Audiobook deleted from library");
      expect(notifications.info).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is resolved", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "TagMismatch",
        actionTaken: "resolved",
        message: "Tags updated",
      });

      expect(notifications.success).toHaveBeenCalledWith("Tags updated");
      expect(notifications.info).not.toHaveBeenCalled();
    });
  });

  describe("UnreadableFile", () => {
    it("has a label, a bulk description and its own explanatory info", () => {
      expect(getIssueTypeLabel("UnreadableFile")).toBe("Unreadable Files");
      expect(getBulkResolveDescription("UnreadableFile")).not.toBe("Continue?");

      // Its own entry rather than falling through to the bulk description: this is the one issue
      // type resolving cannot fix, and the screen has to say so before the user clicks.
      expect(getIssueTypeInfo("UnreadableFile")).not.toBe(
        getBulkResolveDescription("UnreadableFile"),
      );
    });

    it("warns rather than reporting success when the file is still unreadable", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "UnreadableFile",
        actionTaken: "still_unreadable",
        message: "The media file still cannot be read.",
      });

      expect(notifications.warning).toHaveBeenCalledWith("The media file still cannot be read.");
      expect(notifications.success).not.toHaveBeenCalled();
    });

    it("reports success when the file can be read again", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "UnreadableFile",
        actionTaken: "file_readable",
        message: "The media file can be read again.",
      });

      expect(notifications.success).toHaveBeenCalledWith("The media file can be read again.");
      expect(notifications.warning).not.toHaveBeenCalled();
    });
  });

  describe("LibraryPathUnavailable", () => {
    it("has a label, a bulk description and its own explanatory info", () => {
      expect(getIssueTypeLabel("LibraryPathUnavailable")).toBe("Library Path Unavailable");
      expect(getBulkResolveDescription("LibraryPathUnavailable")).not.toBe("Continue?");

      // Its own entry rather than falling through to the bulk description: like UnreadableFile,
      // resolving cannot fix it - the directory has to come back. The screen says so up front.
      expect(getIssueTypeInfo("LibraryPathUnavailable")).not.toBe(
        getBulkResolveDescription("LibraryPathUnavailable"),
      );
    });

    it("warns rather than reporting success when the directory is still unavailable", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "LibraryPathUnavailable",
        actionTaken: "directory_still_unavailable",
        message: "The directory still cannot be found.",
      });

      expect(notifications.warning).toHaveBeenCalledWith("The directory still cannot be found.");
      expect(notifications.success).not.toHaveBeenCalled();
    });

    it("reports success when the directory is readable again", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "LibraryPathUnavailable",
        actionTaken: "directory_readable_again",
        message: "The directory is available again.",
      });

      expect(notifications.success).toHaveBeenCalledWith("The directory is available again.");
      expect(notifications.warning).not.toHaveBeenCalled();
    });

    it("reports info (not success) when a MissingMediaFile resolve re-checks to a missing directory", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "directory_unavailable",
        message: "The record was kept.",
      });

      expect(notifications.info).toHaveBeenCalledWith("The record was kept.");
      expect(notifications.success).not.toHaveBeenCalled();
      expect(notifications.warning).not.toHaveBeenCalled();
    });
  });

  describe("notifyOrphanResolveResult", () => {
    it("shows info toast when actionTaken is retained_not_empty", () => {
      notifyOrphanResolveResult({
        id: 1,
        directoryPath: "/path/to/dir",
        actionTaken: "retained_not_empty",
        message: "Directory still contains files",
      });

      expect(notifications.info).toHaveBeenCalledWith("Directory still contains files");
      expect(notifications.success).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is deleted", () => {
      notifyOrphanResolveResult({
        id: 1,
        directoryPath: "/path/to/dir",
        actionTaken: "deleted",
        message: "Orphan directory deleted from disk",
      });

      expect(notifications.success).toHaveBeenCalledWith("Orphan directory deleted from disk");
      expect(notifications.info).not.toHaveBeenCalled();
    });
  });
});
