import { describe, it, expect, vi, beforeEach } from "vitest";
import apiClient from "../http-common";
import SeriesService from "./SeriesService";
import BrowseService from "./BrowseService";
import { SeriesExpectedBook } from "../types/Series";

vi.mock("../http-common", () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

const mockedApiClient = vi.mocked(apiClient, true);

// A series value is a raw m4b tag, so it can contain a "/". Encoded into a path segment as %2F
// it never reaches the action decoded - ASP.NET Core leaves it escaped - so every lookup missed
// and the series 404'd the moment it was opened. The name belongs in the query string.
const seriesWithSlash = "Sword Art Online / Progressive";
const encoded = "Sword%20Art%20Online%20%2F%20Progressive";

beforeEach(() => {
  vi.clearAllMocks();
  mockedApiClient.get.mockResolvedValue({ data: {} });
  mockedApiClient.post.mockResolvedValue({ data: {} });
});

describe("SeriesService series-name addressing", () => {
  it("_SendsTheSeriesNameAsAQueryParameterNotAPathSegment", async () => {
    await SeriesService.getSeriesDetail(seriesWithSlash);
    expect(mockedApiClient.get).toHaveBeenCalledWith(
      `/series/detail?seriesName=${encoded}`,
    );

    await SeriesService.getMatchCandidates(seriesWithSlash);
    expect(mockedApiClient.get).toHaveBeenLastCalledWith(
      `/series/match-candidates?seriesName=${encoded}`,
    );

    await SeriesService.searchMatchCandidates(seriesWithSlash, "sword art");
    expect(mockedApiClient.get).toHaveBeenLastCalledWith(
      `/series/match-candidates/search?seriesName=${encoded}&query=sword%20art`,
    );
  });

  it("posts to the fixed action paths with the name in the query string", async () => {
    await SeriesService.matchSeries(
      seriesWithSlash,
      "Hardcover",
      "42",
      0.9,
      true,
    );
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      `/series/match?seriesName=${encoded}`,
      {
        sourceName: "Hardcover",
        sourceId: "42",
        confidence: 0.9,
        includeOmnibusEditions: true,
      },
    );

    await SeriesService.setIncludeOmnibusEditions(seriesWithSlash, true);
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      `/series/include-omnibus-editions?seriesName=${encoded}`,
      { includeOmnibusEditions: true },
    );

    await SeriesService.startRefreshSeries(seriesWithSlash);
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      `/series/refresh?seriesName=${encoded}`,
      undefined,
    );

    const book: SeriesExpectedBook = {
      id: 1,
      title: "Aincrad",
      position: "1",
      year: 2009,
      sourceUrl: null,
      isIgnored: false,
    };
    await SeriesService.ignoreExpectedBook(seriesWithSlash, book);
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      `/series/expected-books/ignore?seriesName=${encoded}`,
      { position: "1", title: "Aincrad" },
    );

    await SeriesService.unignoreExpectedBook(seriesWithSlash, book);
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      `/series/expected-books/unignore?seriesName=${encoded}`,
      { position: "1", title: "Aincrad" },
    );
  });

  // The bulk endpoints take no series name, so they keep their own fixed paths - and those must
  // not collide with the per-series ones.
  it("leaves the bulk endpoints on their own paths", async () => {
    await SeriesService.startBulkMatch(0.85);
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      "/series/match/bulk",
      {
        confidenceThreshold: 0.85,
        seriesNames: undefined,
      },
    );

    await SeriesService.startRefreshAll();
    expect(mockedApiClient.post).toHaveBeenLastCalledWith(
      "/series/refresh-all",
      undefined,
    );
  });
});

describe("BrowseService.getSeriesBooks", () => {
  it("_SendsTheSeriesNameAsAQueryParameterNotAPathSegment", async () => {
    mockedApiClient.get.mockResolvedValue({ data: [] });

    await BrowseService.getSeriesBooks(seriesWithSlash);
    expect(mockedApiClient.get).toHaveBeenLastCalledWith(
      `/browse/series?seriesName=${encoded}`,
    );

    await BrowseService.getSeriesBooks(seriesWithSlash, 7);
    expect(mockedApiClient.get).toHaveBeenLastCalledWith(
      `/browse/series?seriesName=${encoded}&authorId=7`,
    );
  });
});
