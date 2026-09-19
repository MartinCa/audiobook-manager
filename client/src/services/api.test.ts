import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  audiobookApi,
  browseApi,
  metadataRefreshApi,
  seriesApi,
  filesApi,
  upcomingReleasesApi,
  toAudiobookDto,
  toPathPreviewDto,
} from "./api";
import type { Audiobook } from "@/types/Audiobook";

describe("api service mappings and contracts", () => {
  const sampleAudiobook: Audiobook = {
    bookName: "The Way of Kings",
    subtitle: "Book One of the Stormlight Archive",
    authors: [{ name: "Brandon Sanderson" }],
    narrators: [{ name: "Michael Kramer" }, { name: "Kate Reading" }],
    series: "The Stormlight Archive",
    seriesPart: "1",
    year: 2010,
    genres: ["Fantasy", "Epic Fantasy"],
    description: "An epic masterpiece.",
    copyright: "2010 Brandon Sanderson",
    publisher: "Tor Books",
    language: "en",
    rating: "5",
    asin: "B003ZWFO7E",
    www: "https://brandonsanderson.com",
    fileInfo: {
      fullPath: "/audiobooks/Sanderson/The Way of Kings.m4b",
      fileName: "The Way of Kings.m4b",
      sizeInBytes: 1048576000,
    },
    durationInSeconds: 162000,
  };

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("DTO serialization", () => {
    it("serializes Audiobook to DTO with flattened author/narrator strings", () => {
      const dto = toAudiobookDto(sampleAudiobook);
      expect(dto.authors).toEqual(["Brandon Sanderson"]);
      expect(dto.narrators).toEqual(["Michael Kramer", "Kate Reading"]);
      expect(dto.filePath).toBe("/audiobooks/Sanderson/The Way of Kings.m4b");
      expect(dto.fileName).toBe("The Way of Kings.m4b");
      expect(dto.sizeInBytes).toBe(1048576000);
      expect(dto.replaceExisting).toBe(false);

      const replaceDto = toAudiobookDto({ ...sampleAudiobook, replaceExisting: true });
      expect(replaceDto.replaceExisting).toBe(true);
    });

    it("forwards the one-shot metadataAppliedFromSearch signal only when set", () => {
      // Default (a save that carried no applied search result) must send false explicitly - the
      // backend's DTO default is false anyway, but sending the field keeps the contract visible.
      const plain = toAudiobookDto(sampleAudiobook);
      expect(plain.metadataAppliedFromSearch).toBe(false);

      const applied = toAudiobookDto({ ...sampleAudiobook, metadataAppliedFromSearch: true });
      expect(applied.metadataAppliedFromSearch).toBe(true);
    });

    it("serializes Audiobook to lightweight path preview DTO", () => {
      const previewDto = toPathPreviewDto(sampleAudiobook);
      expect(previewDto.authors).toEqual(["Brandon Sanderson"]);
      expect(previewDto.bookName).toBe("The Way of Kings");
      expect(previewDto.series).toBe("The Stormlight Archive");
      expect(previewDto.seriesPart).toBe("1");
      expect(previewDto.year).toBe(2010);
      expect((previewDto as Record<string, unknown>).description).toBeUndefined();
      expect((previewDto as Record<string, unknown>).cover).toBeUndefined();
      // Send only what the endpoint reads: the preview endpoints are hit on a debounced
      // keystroke watcher, so the bookkeeping signal must not ride along.
      expect((previewDto as Record<string, unknown>).metadataAppliedFromSearch).toBeUndefined();
    });
  });

  describe("Audiobook API endpoints", () => {
    it("calls generate_path with underscore endpoint", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify("/audiobooks/generated/path.m4b"), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await audiobookApi.generateNewPath(sampleAudiobook);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/audiobook/generate_path",
        expect.objectContaining({
          method: "POST",
        }),
      );
    });

    it("calls check_target_path with underscore endpoint", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(
          JSON.stringify({
            exists: false,
            targetPath: "/audiobooks/target.m4b",
          }),
          {
            status: 200,
            headers: { "Content-Type": "application/json" },
          },
        ),
      );

      await audiobookApi.checkTargetPath(sampleAudiobook);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/audiobook/check_target_path",
        expect.objectContaining({
          method: "POST",
        }),
      );
    });
  });

  describe("Series API endpoints", () => {
    it("calls refresh with seriesName in query string", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.refreshSeries("Mistborn");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/refresh?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
        }),
      );
    });

    it("calls refresh-all for bulk series refresh", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.startRefreshAll();

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/refresh-all",
        expect.objectContaining({
          method: "POST",
        }),
      );
    });

    it("calls expected-books/ignore with query param and body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.ignoreExpectedBook("Mistborn", "4", "Secret History");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/ignore?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ position: "4", title: "Secret History" }),
        }),
      );
    });

    it("calls expected-books/unignore with query param and body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.unignoreExpectedBook("Mistborn", "4", "Secret History");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/unignore?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ position: "4", title: "Secret History" }),
        }),
      );
    });

    it("calls include-omnibus-editions with query and body", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ isMatched: true }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.setIncludeOmnibusEditions("Mistborn", true);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/include-omnibus-editions?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ includeOmnibusEditions: true }),
        }),
      );
    });

    it("calls expected-books/candidates with correct query params", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getMissingBookCandidates("Mistborn", "4", "Secret History");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/candidates?seriesName=Mistborn&position=4&title=Secret+History",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("calls expected-books/apply with query param and body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.applyMissingBook("Mistborn", 42, "4", "Secret History");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/apply?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ audiobookId: 42, position: "4", title: "Secret History" }),
        }),
      );
    });

    it("omits null position and title from apply request body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.applyMissingBook("Mistborn", 42, null, null);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/apply?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ audiobookId: 42 }),
        }),
      );
    });

    it("calls expected-books/bulk-candidates with paged query params", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ items: [], totalCount: 0 }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getBulkMissingBookCandidates("Mistborn", 3, 50);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/bulk-candidates?seriesName=Mistborn&page=3&pageSize=50",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("calls expected-books/apply-bulk with only the accepted selections", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.startBulkMissingBookApply("Mistborn", [
        { audiobookId: 42, position: "4", title: "Secret History" },
        { audiobookId: 43, position: null, title: "The Lost Metal" },
      ]);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/expected-books/apply-bulk?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({
            selections: [
              { audiobookId: 42, position: "4", title: "Secret History" },
              { audiobookId: 43, position: null, title: "The Lost Metal" },
            ],
          }),
        }),
      );
    });

    it("calls pending page with paged query params", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ items: [], total: 0 }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getSeriesPendingPage(2, 50);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/pending?page=2&pageSize=50",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("calls pending count for the list header badge", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response("3", {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getSeriesPendingCount();

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/pending/count",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("calls pending detail with seriesName in query", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ changes: [] }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getSeriesPending("Mistborn");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/pending/detail?seriesName=Mistborn",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("calls pending dismiss with seriesName in query", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.dismissSeriesPending("Mistborn");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/pending/dismiss?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
        }),
      );
    });

    it("calls pending apply with seriesName in query and the request body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.applySeriesPending("Mistborn", {
        adoptSourceSeriesName: true,
        selections: [
          { changeType: "PartUpdate", audiobookId: 5 },
          { changeType: "MissingBook", audiobookId: 9, position: "4", title: "Book B" },
        ],
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/pending/apply?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({
            adoptSourceSeriesName: true,
            selections: [
              { changeType: "PartUpdate", audiobookId: 5 },
              { changeType: "MissingBook", audiobookId: 9, position: "4", title: "Book B" },
            ],
          }),
        }),
      );
    });

    it("calls series mappings list with seriesName in the query string", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify([{ id: 1, regex: "^mistborn.*$", warnAboutPart: false }]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.getSeriesMappings("Mistborn");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/mappings?seriesName=Mistborn",
        expect.objectContaining({
          method: "GET",
        }),
      );
    });

    it("creates a series mapping without a target field", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ id: 1, regex: "^mistborn.*$", warnAboutPart: true }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.createSeriesMapping("Mistborn", {
        regex: "^mistborn.*$",
        warnAboutPart: true,
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/mappings?seriesName=Mistborn",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ regex: "^mistborn.*$", warnAboutPart: true }),
        }),
      );
    });

    it("updates a series mapping under the series-scoped endpoint", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ id: 7, regex: "^new.*$", warnAboutPart: false }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await seriesApi.updateSeriesMapping("Mistborn", 7, {
        id: 7,
        regex: "^new.*$",
        warnAboutPart: false,
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/mappings/7?seriesName=Mistborn",
        expect.objectContaining({
          method: "PUT",
          body: JSON.stringify({ id: 7, regex: "^new.*$", warnAboutPart: false }),
        }),
      );
    });

    it("deletes a series mapping under the series-scoped endpoint", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await seriesApi.deleteSeriesMapping("Mistborn", 7);

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/series/mappings/7?seriesName=Mistborn",
        expect.objectContaining({
          method: "DELETE",
        }),
      );
    });
  });

  describe("Files API endpoints", () => {
    it("calls delete_directory on filesApi.deleteBook", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await filesApi.deleteBook("/audiobooks/Corrupted");

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/files/delete_directory",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ path: "/audiobooks/Corrupted" }),
        }),
      );
    });
  });

  describe("Optional pending resources normalize a 404 to undefined", () => {
    it("getPendingForAudiobook resolves undefined on a 404", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(null, { status: 404, statusText: "Not Found" }),
      );

      await expect(metadataRefreshApi.getPendingForAudiobook(42)).resolves.toBeUndefined();
    });

    it("getPendingForAudiobook preserves a snapshot payload when one exists", async () => {
      const snapshot = {
        audiobookId: 42,
        fetchedAt: "2026-09-01T12:00:00Z",
        sourceName: "Goodreads",
        sourceUrl: "https://example.com/book",
        payload: { url: "https://example.com/book" },
      };
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify(snapshot), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await expect(metadataRefreshApi.getPendingForAudiobook(42)).resolves.toEqual(snapshot);
    });

    it("getPendingForAudiobook still throws on a 500", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(null, { status: 500, statusText: "Internal Server Error" }),
      );

      await expect(metadataRefreshApi.getPendingForAudiobook(42)).rejects.toMatchObject({
        status: 500,
      });
    });

    it("getSeriesPending resolves undefined on a 404", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(null, { status: 404, statusText: "Not Found" }),
      );

      await expect(seriesApi.getSeriesPending("Mistborn")).resolves.toBeUndefined();
    });

    it("getSeriesPending preserves a snapshot payload when one exists", async () => {
      const snapshot = {
        seriesName: "Mistborn",
        sourceName: "Hardcover",
        sourceUrl: "https://hardcover.app/series/42",
        sourceSeriesName: "Mistborn",
        fetchedAt: "2026-09-01T12:00:00Z",
        changes: [],
      };
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify(snapshot), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await expect(seriesApi.getSeriesPending("Mistborn")).resolves.toEqual(snapshot);
    });

    it("getSeriesPending still throws on a 500", async () => {
      vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(null, { status: 500, statusText: "Internal Server Error" }),
      );

      await expect(seriesApi.getSeriesPending("Mistborn")).rejects.toMatchObject({
        status: 500,
      });
    });
  });

  describe("author refresh (upcoming-releases feature)", () => {
    it("calls the single-author refresh endpoint and returns the result", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ success: true, lastRefreshedAt: "2026-09-19T00:00:00Z" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await expect(browseApi.refreshAuthor(7)).resolves.toEqual({
        success: true,
        lastRefreshedAt: "2026-09-19T00:00:00Z",
      });
      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/browse/authors/7/refresh",
        expect.objectContaining({ method: "POST" }),
      );
    });

    it("calls the sweep-all-authors refresh endpoint and returns the counters", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify({ processed: 5, succeeded: 4, failed: 1 }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await expect(browseApi.refreshAllAuthors()).resolves.toEqual({
        processed: 5,
        succeeded: 4,
        failed: 1,
      });
      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/browse/authors/refresh-all",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });

  describe("upcomingReleasesApi.dismissRosterUpcomingRelease", () => {
    it("posts the series-scoped dismiss body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await upcomingReleasesApi.dismissRosterUpcomingRelease({
        seriesName: "Mistborn",
        seriesPosition: "5",
        title: "The Lost Metal",
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/upcoming-releases/dismiss-roster",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({
            seriesName: "Mistborn",
            seriesPosition: "5",
            title: "The Lost Metal",
          }),
        }),
      );
    });

    it("posts the author-scoped dismiss body", async () => {
      const fetchSpy = vi
        .spyOn(globalThis, "fetch")
        .mockResolvedValue(new Response(null, { status: 200 }));

      await upcomingReleasesApi.dismissRosterUpcomingRelease({
        authorId: 9,
        title: "Standalone Novella",
      });

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/upcoming-releases/dismiss-roster",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ authorId: 9, title: "Standalone Novella" }),
        }),
      );
    });
  });

  describe("seriesApi.getSeriesDetail", () => {
    it("includes the upcoming-books page cursor alongside the existing sections", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(
          JSON.stringify({
            overview: { name: "Mistborn" },
            ownedBooks: { items: [], totalCount: 0 },
            missingBooks: { items: [], totalCount: 0 },
            ignoredBooks: { items: [], totalCount: 0 },
            partMismatches: { items: [], totalCount: 0 },
            upcomingBooks: { items: [], totalCount: 0 },
          }),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
      );

      await seriesApi.getSeriesDetail("Mistborn", { upcomingPage: 1, upcomingPageSize: 25 });

      expect(fetchSpy).toHaveBeenCalledWith(
        expect.stringContaining("upcomingPage=1"),
        expect.anything(),
      );
      expect(fetchSpy).toHaveBeenCalledWith(
        expect.stringContaining("upcomingPageSize=25"),
        expect.anything(),
      );
    });
  });
});
