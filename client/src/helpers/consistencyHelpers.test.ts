import { describe, it, expect, vi, beforeEach } from "vitest";
import { toast } from "sonner";
import {
  getIssueTypeLabel,
  getIssueTypeInfo,
  getBulkResolveDescription,
  notifyConsistencyResolveResult,
  notifyOrphanResolveResult,
} from "./consistencyHelpers";

vi.mock("sonner", () => ({
  toast: {
    success: vi.fn(),
    info: vi.fn(),
    error: vi.fn(),
  },
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

  describe("notifyConsistencyResolveResult", () => {
    it("shows info toast when actionTaken is file_recovered", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "file_recovered",
        message: "File found on disk",
      });

      expect(toast.info).toHaveBeenCalledWith("File found on disk");
      expect(toast.success).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is audiobook_deleted", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "MissingMediaFile",
        actionTaken: "audiobook_deleted",
        message: "Audiobook deleted from library",
      });

      expect(toast.success).toHaveBeenCalledWith("Audiobook deleted from library");
      expect(toast.info).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is resolved", () => {
      notifyConsistencyResolveResult({
        issueId: 1,
        issueType: "TagMismatch",
        actionTaken: "resolved",
        message: "Tags updated",
      });

      expect(toast.success).toHaveBeenCalledWith("Tags updated");
      expect(toast.info).not.toHaveBeenCalled();
    });
  });

  describe("notifyOrphanResolveResult", () => {
    it("shows info toast when actionTaken is retained_has_audio", () => {
      notifyOrphanResolveResult({
        id: 1,
        directoryPath: "/path/to/dir",
        actionTaken: "retained_has_audio",
        message: "Directory now contains audio files",
      });

      expect(toast.info).toHaveBeenCalledWith("Directory now contains audio files");
      expect(toast.success).not.toHaveBeenCalled();
    });

    it("shows success toast when actionTaken is deleted", () => {
      notifyOrphanResolveResult({
        id: 1,
        directoryPath: "/path/to/dir",
        actionTaken: "deleted",
        message: "Orphan directory deleted from disk",
      });

      expect(toast.success).toHaveBeenCalledWith("Orphan directory deleted from disk");
      expect(toast.info).not.toHaveBeenCalled();
    });
  });
});
