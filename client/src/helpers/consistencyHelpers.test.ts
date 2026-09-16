import { describe, it, expect, vi, beforeEach } from "vitest";
import { toast } from "@/components/ui/toast";
import {
  getIssueTypeLabel,
  getIssueTypeInfo,
  getBulkResolveDescription,
  notifyConsistencyResolveResult,
  notifyOrphanResolveResult,
} from "./consistencyHelpers";

vi.mock("@/components/ui/toast", () => ({
  toast: { add: vi.fn() },
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

      expect(toast.add).toHaveBeenCalledWith({ title: "File found on disk", type: "info" });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "success" }));
    });

    it("shows success toast when actionTaken is audiobook_deleted", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "audiobook_deleted",
        message: "Audiobook deleted from library",
      });

      expect(toast.add).toHaveBeenCalledWith({
        title: "Audiobook deleted from library",
        type: "success",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "info" }));
    });

    it("shows success toast when actionTaken is resolved", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "TagMismatch",
        actionTaken: "resolved",
        message: "Tags updated",
      });

      expect(toast.add).toHaveBeenCalledWith({ title: "Tags updated", type: "success" });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "info" }));
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

      expect(toast.add).toHaveBeenCalledWith({
        title: "The media file still cannot be read.",
        type: "warning",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "success" }));
    });

    it("reports success when the file can be read again", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "UnreadableFile",
        actionTaken: "file_readable",
        message: "The media file can be read again.",
      });

      expect(toast.add).toHaveBeenCalledWith({
        title: "The media file can be read again.",
        type: "success",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "warning" }));
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

      expect(toast.add).toHaveBeenCalledWith({
        title: "The directory still cannot be found.",
        type: "warning",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "success" }));
    });

    it("reports success when the directory is readable again", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "LibraryPathUnavailable",
        actionTaken: "directory_readable_again",
        message: "The directory is available again.",
      });

      expect(toast.add).toHaveBeenCalledWith({
        title: "The directory is available again.",
        type: "success",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "warning" }));
    });

    it("reports info (not success) when a MissingMediaFile resolve re-checks to a missing directory", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "directory_unavailable",
        message: "The record was kept.",
      });

      expect(toast.add).toHaveBeenCalledWith({ title: "The record was kept.", type: "info" });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "success" }));
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "warning" }));
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

      expect(toast.add).toHaveBeenCalledWith({
        title: "Directory still contains files",
        type: "info",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "success" }));
    });

    it("shows success toast when actionTaken is deleted", () => {
      notifyOrphanResolveResult({
        id: 1,
        directoryPath: "/path/to/dir",
        actionTaken: "deleted",
        message: "Orphan directory deleted from disk",
      });

      expect(toast.add).toHaveBeenCalledWith({
        title: "Orphan directory deleted from disk",
        type: "success",
      });
      expect(toast.add).not.toHaveBeenCalledWith(expect.objectContaining({ type: "info" }));
    });
  });
});
