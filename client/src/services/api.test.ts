import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  audiobookApi,
  seriesApi,
  settingsApi,
  filesApi,
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

      await seriesApi.startRefresh("Mistborn");

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
  });

  describe("Settings API endpoints", () => {
    it("calls series_mappings with underscore endpoint", async () => {
      const fetchSpy = vi.spyOn(globalThis, "fetch").mockResolvedValue(
        new Response(JSON.stringify([]), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );

      await settingsApi.getSeriesMappings();

      expect(fetchSpy).toHaveBeenCalledWith(
        "/api/settings/series_mappings",
        expect.objectContaining({
          method: "GET",
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
});
