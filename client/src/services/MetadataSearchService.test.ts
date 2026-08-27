import { describe, it, expect, vi, beforeEach } from "vitest";
import apiClient from "../http-common";
import MetadataSearchService from "./MetadataSearchService";

vi.mock("../http-common", () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedApiClient = vi.mocked(apiClient, true);

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MetadataSearchService", () => {
  describe("searchSource", () => {
    it("GETs /metadata-search/:source with the URL-encoded search term", async () => {
      const results = [{ title: "Book" }];
      mockedApiClient.get.mockResolvedValueOnce({ data: results });

      const result = await MetadataSearchService.searchSource(
        "goodreads",
        "some book & stuff",
      );

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/metadata-search/goodreads?q=some%20book%20%26%20stuff",
      );
      expect(result).toEqual(results);
    });
  });

  describe("searchMultiple", () => {
    it("POSTs sources and search term to /metadata-search/multi", async () => {
      const multiResult = { results: [] };
      mockedApiClient.post.mockResolvedValueOnce({ data: multiResult });

      const result = await MetadataSearchService.searchMultiple(
        ["goodreads", "audible"],
        "some book",
      );

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/metadata-search/multi",
        { sources: ["goodreads", "audible"], q: "some book" },
      );
      expect(result).toEqual(multiResult);
    });
  });

  describe("getBookDetails", () => {
    it("POSTs the book path to /metadata-search/details", async () => {
      const details = { title: "Book" };
      mockedApiClient.post.mockResolvedValueOnce({ data: details });

      const result = await MetadataSearchService.getBookDetails(
        "https://example.com/book/1",
      );

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/metadata-search/details",
        { path: "https://example.com/book/1" },
      );
      expect(result).toEqual(details);
    });

    it("rejects when the request fails", async () => {
      const error = new Error("scrape failed");
      mockedApiClient.post.mockRejectedValueOnce(error);

      await expect(
        MetadataSearchService.getBookDetails("https://example.com/book/1"),
      ).rejects.toBe(error);
    });
  });

  describe("getServices", () => {
    it("GETs /metadata-search/services", async () => {
      const services = [{ sourceName: "Goodreads" }];
      mockedApiClient.get.mockResolvedValueOnce({ data: services });

      const result = await MetadataSearchService.getServices();

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/metadata-search/services",
      );
      expect(result).toEqual(services);
    });
  });
});
