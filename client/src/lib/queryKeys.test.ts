import { describe, it, expect } from "vitest";
import { queryKeys } from "./queryKeys";

describe("queryKeys", () => {
  describe("seriesDetail", () => {
    it("keeps every narrower variant a literal prefix of the detail key, in order", () => {
      const seriesName = "Some Series";
      const authorId = 7;
      const detail = queryKeys.seriesDetail.detail(seriesName, authorId, 0, 1, 2, 3, 4);

      expect(queryKeys.seriesDetail.bySeries(seriesName)).toEqual(detail.slice(0, 2));
      expect(queryKeys.seriesDetail.byAuthor(seriesName, authorId)).toEqual(detail.slice(0, 3));
      expect(queryKeys.seriesDetail.all()).toEqual(detail.slice(0, 1));
    });

    it("matches SeriesDetail's query key and its own refresh/omnibus/apply invalidation exactly", () => {
      // Regression for the refresh/apply/ignore/omnibus handlers in SeriesDetail.tsx, which
      // invalidate byAuthor() rather than the full paged key - a page-scoped query key would
      // silently escape all of them.
      const seriesName = "Mistborn";
      const authorId = 3;
      const ownedPage = 2;
      const missingPage = 0;
      const ignoredPage = 1;
      const partMismatchPage = 0;

      const upcomingPage = 0;

      const queryKey = queryKeys.seriesDetail.detail(
        seriesName,
        authorId,
        ownedPage,
        missingPage,
        ignoredPage,
        partMismatchPage,
        upcomingPage,
      );
      const invalidationKey = queryKeys.seriesDetail.byAuthor(seriesName, authorId);

      expect(queryKey.slice(0, invalidationKey.length)).toEqual(invalidationKey);
    });
  });

  describe("bookDetail", () => {
    it("produces the same key for a query and its own invalidation call", () => {
      const id = 42;
      expect(queryKeys.bookDetail(id)).toEqual(queryKeys.bookDetail(id));
      expect(queryKeys.bookDetail(id)).toEqual(["bookDetail", id]);
    });
  });

  describe("series/seriesCounts/seriesPending family shapes", () => {
    it("keeps series.page and series.unmatched both prefixed by series.all", () => {
      expect(queryKeys.series.page("query", 1, {}).slice(0, 1)).toEqual(queryKeys.series.all());
      expect(queryKeys.series.unmatched(1).slice(0, 1)).toEqual(queryKeys.series.all());
    });

    it("keeps every seriesPending variant prefixed by seriesPending.all", () => {
      const all = queryKeys.seriesPending.all();
      expect(queryKeys.seriesPending.bySeries("A Series").slice(0, all.length)).toEqual(all);
      expect(queryKeys.seriesPending.page(1).slice(0, all.length)).toEqual(all);
      expect(queryKeys.seriesPending.count().slice(0, all.length)).toEqual(all);
    });
  });

  describe("distinct families that look alike", () => {
    it("keeps bookDetail (audiobook id) and bookDetails (filesystem path) from colliding", () => {
      expect(queryKeys.bookDetail(1)).not.toEqual(queryKeys.bookDetails("/some/path"));
      expect(queryKeys.bookDetail(1)[0]).not.toBe(queryKeys.bookDetails("/some/path")[0]);
    });

    it("keeps author (detail page) and authors (list page) from sharing a prefix", () => {
      expect(queryKeys.author.all()).not.toEqual(queryKeys.authors.all());
      // authors.all() must not be a prefix of an author.detail() key, or invalidating the
      // authors list would also invalidate every open author-detail page.
      const detail = queryKeys.author.detail(1, 0, 0, 0);
      expect(detail.slice(0, 1)).not.toEqual(queryKeys.authors.all());
    });
  });

  describe("shared families reused across multiple components by design", () => {
    it("gives every caller of languages() the same cache entry", () => {
      expect(queryKeys.languages()).toEqual(["languages"]);
    });

    it("gives every caller of directoryContents(path) the same cache entry for the same path", () => {
      expect(queryKeys.directoryContents("/a/b")).toEqual(queryKeys.directoryContents("/a/b"));
    });
  });

  describe("metadataRefresh", () => {
    it("keeps pendingPage and pendingForBook both prefixed by all(), for the same page/id space", () => {
      const all = queryKeys.metadataRefresh.all();
      expect(queryKeys.metadataRefresh.pendingPage(1).slice(0, all.length)).toEqual(all);
      expect(queryKeys.metadataRefresh.pendingForBook(42).slice(0, all.length)).toEqual(all);
    });
  });
});
