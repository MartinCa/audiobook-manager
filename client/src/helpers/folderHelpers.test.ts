import { describe, it, expect } from "vitest";
import { normalizePath, getContainingFolderPath, getTotalSizeInBytes } from "./folderHelpers";
import type { BookFileInfo } from "@/types/BookFileInfo";

describe("folderHelpers", () => {
  describe("normalizePath", () => {
    it("converts backslashes to forward slashes", () => {
      expect(normalizePath("C:\\audiobooks\\Author\\Book")).toBe("C:/audiobooks/Author/Book");
    });

    it("trims trailing slashes", () => {
      expect(normalizePath("/audiobooks/Author/Book/")).toBe("/audiobooks/Author/Book");
      expect(normalizePath("C:\\audiobooks\\Author\\Book\\\\")).toBe("C:/audiobooks/Author/Book");
    });

    it("preserves root slash", () => {
      expect(normalizePath("/")).toBe("/");
    });
  });

  describe("getContainingFolderPath", () => {
    it("extracts directory from non-empty files list", () => {
      const files: BookFileInfo[] = [
        {
          fullPath: "/audiobooks/Isaac Asimov/Foundation/Foundation.m4b",
          fileName: "Foundation.m4b",
          sizeInBytes: 500000,
        },
        {
          fullPath: "/audiobooks/Isaac Asimov/Foundation/cover.jpg",
          fileName: "cover.jpg",
          sizeInBytes: 20000,
        },
      ];

      expect(
        getContainingFolderPath("/audiobooks/Isaac Asimov/Foundation/Foundation.m4b", files),
      ).toBe("/audiobooks/Isaac Asimov/Foundation");
    });

    it("extracts directory when Windows backslashes are used in file paths", () => {
      const files: BookFileInfo[] = [
        {
          fullPath: "D:\\Audiobooks\\Frank Herbert\\Dune\\Dune.m4b",
          fileName: "Dune.m4b",
          sizeInBytes: 500000,
        },
      ];

      expect(getContainingFolderPath("D:\\Audiobooks\\Frank Herbert\\Dune\\Dune.m4b", files)).toBe(
        "D:/Audiobooks/Frank Herbert/Dune",
      );
    });

    it("identifies targetPath as directory when folder name has dots and files are contained within", () => {
      const files: BookFileInfo[] = [
        {
          fullPath: "/audiobooks/Author/S.H.I.E.L.D./track.m4b",
          fileName: "track.m4b",
          sizeInBytes: 1000,
        },
      ];

      expect(getContainingFolderPath("/audiobooks/Author/S.H.I.E.L.D.", files)).toBe(
        "/audiobooks/Author/S.H.I.E.L.D.",
      );
    });

    it("extracts parent folder when file is located directly in import root", () => {
      const files: BookFileInfo[] = [
        {
          fullPath: "/import/Foundation.m4b",
          fileName: "Foundation.m4b",
          sizeInBytes: 1000,
        },
      ];

      expect(getContainingFolderPath("/import/Foundation.m4b", files)).toBe("/import");
    });

    it("falls back to parent directory when targetPath has an extension and files are empty", () => {
      expect(getContainingFolderPath("/import/Isaac Asimov - Foundation.m4b", [])).toBe("/import");
    });

    it("returns targetPath if it is a directory and files are empty", () => {
      expect(getContainingFolderPath("/audiobooks/Isaac Asimov/EmptyFolder", [])).toBe(
        "/audiobooks/Isaac Asimov/EmptyFolder",
      );
    });
  });

  describe("getTotalSizeInBytes", () => {
    it("sums file sizes correctly", () => {
      const files: BookFileInfo[] = [
        { fullPath: "/a/1.m4b", fileName: "1.m4b", sizeInBytes: 1000 },
        { fullPath: "/a/2.jpg", fileName: "2.jpg", sizeInBytes: 250 },
        { fullPath: "/a/3.txt", fileName: "3.txt", sizeInBytes: 50 },
      ];

      expect(getTotalSizeInBytes(files)).toBe(1300);
    });

    it("returns 0 for empty list", () => {
      expect(getTotalSizeInBytes([])).toBe(0);
    });
  });
});
