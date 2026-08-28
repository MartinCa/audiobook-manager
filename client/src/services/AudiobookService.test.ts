import { describe, it, expect, vi, beforeEach } from "vitest";
import apiClient from "../http-common";
import AudiobookService from "./AudiobookService";
import { Audiobook } from "../types/Audiobook";

vi.mock("../http-common", () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedApiClient = vi.mocked(apiClient, true);

function makeBook(overrides: Partial<Audiobook> = {}): Audiobook {
  return {
    authors: [{ name: "Author One" }, { name: "Author Two" }],
    narrators: [{ name: "Narrator One" }],
    bookName: "Some Book",
    subtitle: "A Subtitle",
    series: "A Series",
    seriesPart: "1",
    year: 2020,
    genres: ["Fantasy"],
    description: "A description",
    copyright: "2020 Author",
    publisher: "A Publisher",
    rating: "4.5",
    asin: "ASIN123",
    www: "https://example.com",
    cover: { base64Data: "abc123", mimeType: "image/jpeg" },
    fileInfo: {
      fullPath: "/library/author/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 12345,
    } as any,
    ...overrides,
  };
}

// The save DTO carries the full metadata (plus the cover).
const expectedSaveFields = {
  authors: ["Author One", "Author Two"],
  narrators: ["Narrator One"],
  bookName: "Some Book",
  subtitle: "A Subtitle",
  series: "A Series",
  seriesPart: "1",
  year: 2020,
  genres: ["Fantasy"],
  description: "A description",
  copyright: "2020 Author",
  publisher: "A Publisher",
  rating: "4.5",
  asin: "ASIN123",
  www: "https://example.com",
  filePath: "/library/author/book.m4b",
  fileName: "book.m4b",
  sizeInBytes: 12345,
};

// The path-preview DTO carries only the fields the generated path is built from. It is sent on
// every debounced keystroke, so anything beyond these - the cover, but also the description -
// would be re-uploaded constantly for a value the endpoint never reads.
const expectedPathPreviewDto = {
  authors: ["Author One", "Author Two"],
  bookName: "Some Book",
  series: "A Series",
  seriesPart: "1",
  year: 2020,
  filePath: "/library/author/book.m4b",
  fileName: "book.m4b",
  sizeInBytes: 12345,
};

const expectedDto = {
  ...expectedSaveFields,
  cover: { base64Data: "abc123", mimeType: "image/jpeg" },
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe("AudiobookService", () => {
  describe("parseBookDetails", () => {
    it("POSTs the book path to /audiobook/details and resolves the response", async () => {
      const book = makeBook();
      mockedApiClient.post.mockResolvedValueOnce({ data: book });

      const result =
        await AudiobookService.parseBookDetails("/import/book.m4b");

      expect(mockedApiClient.post).toHaveBeenCalledWith("/audiobook/details", {
        path: "/import/book.m4b",
      });
      expect(result).toEqual(book);
    });

    it("rejects when the request fails", async () => {
      const error = new Error("network error");
      mockedApiClient.post.mockRejectedValueOnce(error);

      await expect(
        AudiobookService.parseBookDetails("/import/book.m4b"),
      ).rejects.toBe(error);
    });
  });

  describe("organizeBook", () => {
    it("POSTs the mapped DTO to /audiobook/organize", async () => {
      const book = makeBook();
      mockedApiClient.post.mockResolvedValueOnce({ data: "new/path" });

      const result = await AudiobookService.organizeBook(book);

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/audiobook/organize",
        expectedDto,
      );
      expect(result).toBe("new/path");
    });

    it("maps a book with no fileInfo to an empty file path/name and zero size", async () => {
      const book = makeBook({ fileInfo: undefined });
      mockedApiClient.post.mockResolvedValueOnce({ data: "new/path" });

      await AudiobookService.organizeBook(book);

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/audiobook/organize",
        expect.objectContaining({
          filePath: undefined,
          fileName: undefined,
          sizeInBytes: 0,
        }),
      );
    });
  });

  describe("generateNewPath", () => {
    it("POSTs only the path-shaping fields to /audiobook/generate_path", async () => {
      const book = makeBook();
      mockedApiClient.post.mockResolvedValueOnce({ data: "generated/path" });

      const result = await AudiobookService.generateNewPath(book);

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/audiobook/generate_path",
        expectedPathPreviewDto,
      );
      const requestBody = mockedApiClient.post.mock.calls[0][1] as Record<
        string,
        unknown
      >;
      expect(requestBody).not.toHaveProperty("cover");
      expect(result).toBe("generated/path");
    });
  });

  describe("checkTargetPath", () => {
    it("POSTs only the path-shaping fields to /audiobook/check_target_path and resolves the response", async () => {
      const book = makeBook();
      const checkResult = {
        targetPath: "/library/author/2020 - Some Book/book.m4b",
        exists: true,
        existing: {
          audiobookId: 7,
          sizeInBytes: 598_000_000,
          durationInSeconds: 39600,
        },
      };
      mockedApiClient.post.mockResolvedValueOnce({ data: checkResult });

      const result = await AudiobookService.checkTargetPath(book);

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/audiobook/check_target_path",
        expectedPathPreviewDto,
      );
      const requestBody = mockedApiClient.post.mock.calls[0][1] as Record<
        string,
        unknown
      >;
      expect(requestBody).not.toHaveProperty("cover");
      expect(result).toEqual(checkResult);
    });
  });

  describe("updateBook", () => {
    it("PUTs the mapped DTO to /audiobook/:id", async () => {
      const book = makeBook();
      mockedApiClient.put.mockResolvedValueOnce({ data: undefined });

      await AudiobookService.updateBook(42, book);

      expect(mockedApiClient.put).toHaveBeenCalledWith(
        "/audiobook/42",
        expectedDto,
      );
    });

    it("rejects when the request fails", async () => {
      const book = makeBook();
      const error = new Error("update failed");
      mockedApiClient.put.mockRejectedValueOnce(error);

      await expect(AudiobookService.updateBook(1, book)).rejects.toBe(error);
    });
  });
});
