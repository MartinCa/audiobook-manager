import { describe, it, expect, vi, beforeEach } from "vitest";
import apiClient from "../http-common";
import SimilarValueService from "./SimilarValueService";

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
  vi.useRealTimers();
  SimilarValueService.invalidateNameCaches();
});

describe("SimilarValueService", () => {
  describe("getSimilarAuthors", () => {
    it("GETs /similar-values/similar-authors", async () => {
      const groups = [{ id: 1 }];
      mockedApiClient.get.mockResolvedValueOnce({ data: groups });

      const result = await SimilarValueService.getSimilarAuthors();

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/similar-values/similar-authors",
      );
      expect(result).toEqual(groups);
    });
  });

  describe("getSimilarSeries", () => {
    it("GETs /similar-values/similar-series", async () => {
      const groups = [{ id: 2 }];
      mockedApiClient.get.mockResolvedValueOnce({ data: groups });

      const result = await SimilarValueService.getSimilarSeries();

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/similar-values/similar-series",
      );
      expect(result).toEqual(groups);
    });
  });

  describe("startAlign", () => {
    it("POSTs valueType/sourceValues/targetValue to /similar-values/align", async () => {
      mockedApiClient.post.mockResolvedValueOnce({ data: undefined });

      await SimilarValueService.startAlign(
        "author",
        ["J.K. Rowling", "JK Rowling"],
        "J.K. Rowling",
      );

      expect(mockedApiClient.post).toHaveBeenCalledWith(
        "/similar-values/align",
        {
          valueType: "author",
          sourceValues: ["J.K. Rowling", "JK Rowling"],
          targetValue: "J.K. Rowling",
        },
      );
    });
  });

  describe("getAuthorNames", () => {
    it("GETs /similar-values/author-names on first call", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Alice", "Bob"] });

      const result = await SimilarValueService.getAuthorNames();

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/similar-values/author-names",
      );
      expect(result).toEqual(["Alice", "Bob"]);
    });

    it("serves subsequent calls from cache without another request", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Alice"] });

      await SimilarValueService.getAuthorNames();
      const result = await SimilarValueService.getAuthorNames();

      expect(mockedApiClient.get).toHaveBeenCalledTimes(1);
      expect(result).toEqual(["Alice"]);
    });

    it("refetches once the cache TTL has expired", async () => {
      vi.useFakeTimers();
      mockedApiClient.get.mockResolvedValue({ data: ["Alice"] });

      await SimilarValueService.getAuthorNames();
      vi.advanceTimersByTime(5 * 60 * 1000 + 1);
      await SimilarValueService.getAuthorNames();

      expect(mockedApiClient.get).toHaveBeenCalledTimes(2);
      vi.useRealTimers();
    });
  });

  describe("getSeriesNames", () => {
    it("GETs /similar-values/series-names on first call", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Series A"] });

      const result = await SimilarValueService.getSeriesNames();

      expect(mockedApiClient.get).toHaveBeenCalledWith(
        "/similar-values/series-names",
      );
      expect(result).toEqual(["Series A"]);
    });

    it("serves subsequent calls from cache without another request", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Series A"] });

      await SimilarValueService.getSeriesNames();
      await SimilarValueService.getSeriesNames();

      expect(mockedApiClient.get).toHaveBeenCalledTimes(1);
    });
  });

  describe("invalidateNameCaches", () => {
    it("forces a refetch of both author and series names", async () => {
      mockedApiClient.get.mockResolvedValue({ data: [] });

      await SimilarValueService.getAuthorNames();
      await SimilarValueService.getSeriesNames();
      SimilarValueService.invalidateNameCaches();
      await SimilarValueService.getAuthorNames();
      await SimilarValueService.getSeriesNames();

      expect(mockedApiClient.get).toHaveBeenCalledTimes(4);
    });
  });

  describe("addKnownAuthorNames / addKnownSeriesNames", () => {
    it("merges new names into the cached author names list", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Alice"] });
      await SimilarValueService.getAuthorNames();

      SimilarValueService.addKnownAuthorNames(["Bob", "Alice"]);
      const result = await SimilarValueService.getAuthorNames();

      expect(result).toEqual(["Alice", "Bob"]);
      // Still served from cache, no extra HTTP call.
      expect(mockedApiClient.get).toHaveBeenCalledTimes(1);
    });

    it("merges new names into the cached series names list", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Series B"] });
      await SimilarValueService.getSeriesNames();

      SimilarValueService.addKnownSeriesNames(["Series A"]);
      const result = await SimilarValueService.getSeriesNames();

      expect(result).toEqual(["Series A", "Series B"]);
    });

    it("is a no-op when there is no existing cache", async () => {
      SimilarValueService.addKnownAuthorNames(["Alice"]);

      mockedApiClient.get.mockResolvedValueOnce({ data: ["Bob"] });
      const result = await SimilarValueService.getAuthorNames();

      expect(result).toEqual(["Bob"]);
    });

    it("is a no-op when given an empty names array", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Alice"] });
      await SimilarValueService.getAuthorNames();

      SimilarValueService.addKnownAuthorNames([]);
      const result = await SimilarValueService.getAuthorNames();

      expect(result).toEqual(["Alice"]);
    });

    it("ignores falsy names while merging", async () => {
      mockedApiClient.get.mockResolvedValueOnce({ data: ["Alice"] });
      await SimilarValueService.getAuthorNames();

      SimilarValueService.addKnownAuthorNames(["", "Bob"]);
      const result = await SimilarValueService.getAuthorNames();

      expect(result).toEqual(["Alice", "Bob"]);
    });
  });
});
