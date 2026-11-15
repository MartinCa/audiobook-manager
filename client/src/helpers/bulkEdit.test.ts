import { describe, it, expect } from "vitest";
import {
  computeMultiFieldState,
  computeSharedAuthors,
  computeSharedSeries,
  computeSingleFieldState,
} from "./bulkEdit";
import type { BulkEditPreviewBook } from "@/types/BulkEdit";

function book(overrides: Partial<BulkEditPreviewBook> = {}): BulkEditPreviewBook {
  return {
    id: 1,
    authors: [],
    additionalSeries: [],
    narrators: [],
    genres: [],
    bookName: null,
    subtitle: null,
    series: null,
    seriesPart: null,
    year: null,
    description: null,
    copyright: null,
    publisher: null,
    language: null,
    rating: null,
    asin: null,
    www: null,
    ...overrides,
  };
}

describe("computeSingleFieldState", () => {
  it("reports a single distinct value as common", () => {
    const state = computeSingleFieldState(
      [book({ bookName: "The Way of Kings" }), book({ id: 2, bookName: "The Way of Kings" })],
      (b) => b.bookName,
    );

    expect(state).toEqual({ common: "The Way of Kings", mixed: false });
  });

  it("treats every book being empty as a common empty value, not a mixed state", () => {
    const state = computeSingleFieldState(
      [book({ publisher: null }), book({ id: 2, publisher: "" })],
      (b) => b.publisher,
    );

    expect(state).toEqual({ common: "", mixed: false });
  });

  it("normalizes null and undefined to the same empty value", () => {
    const state = computeSingleFieldState(
      [book({ asin: undefined }), book({ id: 2, asin: null })],
      (b) => b.asin,
    );

    expect(state).toEqual({ common: "", mixed: false });
  });

  it("reports disagreement between two different values as mixed", () => {
    const state = computeSingleFieldState(
      [book({ bookName: "The Way of Kings" }), book({ id: 2, bookName: "Words of Radiance" })],
      (b) => b.bookName,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("reports disagreement between a value and an empty field as mixed", () => {
    const state = computeSingleFieldState(
      [book({ series: "Stormlight Archive" }), book({ id: 2, series: null })],
      (b) => b.series,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("reports an empty book list as neither common nor mixed", () => {
    expect(computeSingleFieldState([], (b) => b.bookName)).toEqual({
      common: null,
      mixed: false,
    });
  });

  it("is case-sensitive: case differences are a real disagreement", () => {
    const state = computeSingleFieldState(
      [book({ publisher: "Tor" }), book({ id: 2, publisher: "TOR" })],
      (b) => b.publisher,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });
});

describe("computeMultiFieldState", () => {
  it("reports an exactly-equal sequence across books as common", () => {
    const state = computeMultiFieldState(
      [
        book({ authors: ["Brandon Sanderson", "Isaac Stewart"] }),
        book({ id: 2, authors: ["Brandon Sanderson", "Isaac Stewart"] }),
      ],
      (b) => b.authors,
    );

    expect(state).toEqual({
      common: ["Brandon Sanderson", "Isaac Stewart"],
      mixed: false,
    });
  });

  it("returns a copy of the shared sequence, not the book's own array reference", () => {
    const first = ["Brandon Sanderson"];
    const state = computeMultiFieldState([book({ narrators: first })], (b) => b.narrators);

    expect(state.common).toEqual(first);
    expect(state.common).not.toBe(first);
  });

  it("reports equally-valued but differently-ordered lists as mixed", () => {
    const state = computeMultiFieldState(
      [
        book({ authors: ["Brandon Sanderson", "Isaac Stewart"] }),
        book({ id: 2, authors: ["Isaac Stewart", "Brandon Sanderson"] }),
      ],
      (b) => b.authors,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("reports a strict subset as mixed", () => {
    const state = computeMultiFieldState(
      [
        book({ authors: ["Brandon Sanderson", "Isaac Stewart"] }),
        book({ id: 2, authors: ["Brandon Sanderson"] }),
      ],
      (b) => b.authors,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("treats every book's empty list as a common empty sequence", () => {
    const state = computeMultiFieldState(
      [book({ genres: [] }), book({ id: 2, genres: [] })],
      (b) => b.genres,
    );

    expect(state).toEqual({ common: [], mixed: false });
  });

  it("reports disagreement between an empty list and a list with values as mixed", () => {
    const state = computeMultiFieldState(
      [book({ narrators: [] }), book({ id: 2, narrators: ["Michael Kramer"] })],
      (b) => b.narrators,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("is case-sensitive: a differently-cased value is a real disagreement", () => {
    const state = computeMultiFieldState(
      [book({ genres: ["Fantasy"] }), book({ id: 2, genres: ["fantasy"] })],
      (b) => b.genres,
    );

    expect(state).toEqual({ common: null, mixed: true });
  });

  it("treats a missing (undefined) list as an empty list", () => {
    const state = computeMultiFieldState(
      [book({ genres: undefined }), book({ id: 2, genres: [] })],
      (b) => b.genres,
    );

    expect(state).toEqual({ common: [], mixed: false });
  });

  it("reports an empty book list as neither common nor mixed", () => {
    expect(computeMultiFieldState([], (b) => b.authors)).toEqual({
      common: null,
      mixed: false,
    });
  });
});

const authorNames = (books: BulkEditPreviewBook[]) =>
  computeSharedAuthors(books).map((o) => o.value);
const seriesNames = (books: BulkEditPreviewBook[]) =>
  computeSharedSeries(books).map((o) => o.value);
describe("computeSharedAuthors", () => {
  it("lists the authors every book has, in the first book's order", () => {
    const shared = authorNames([
      book({ authors: ["A One", "B Two", "C Three"] }),
      book({ id: 2, authors: ["C Three", "B Two"] }),
      book({ id: 3, authors: ["B Two", "C Three", "D Four"] }),
    ]);

    expect(shared).toEqual(["B Two", "C Three"]);
  });

  it("is empty when no author is in every book", () => {
    expect(
      authorNames([book({ authors: ["A One"] }), book({ id: 2, authors: ["B Two"] })]),
    ).toEqual([]);
  });

  it("offers a single book's whole author list", () => {
    expect(authorNames([book({ authors: ["A One", "B Two"] })])).toEqual(["A One", "B Two"]);
  });

  it("is empty for no books, and for books without authors", () => {
    expect(authorNames([])).toEqual([]);
    expect(authorNames([book({ authors: [] }), book({ id: 2, authors: [] })])).toEqual([]);
  });

  it("matches names exactly, since the main author is keyed by exact name", () => {
    expect(
      authorNames([book({ authors: ["Jane Doe"] }), book({ id: 2, authors: ["jane doe"] })]),
    ).toEqual([]);
  });

  it("lists a name once even if a book repeats it", () => {
    expect(
      authorNames([book({ authors: ["A One", "A One"] }), book({ id: 2, authors: ["A One"] })]),
    ).toEqual(["A One"]);
  });
});

describe("computeSharedSeries", () => {
  it("offers a series every book is in, as primary or additional", () => {
    const shared = seriesNames([
      book({ series: "Main", additionalSeries: ["Spinoff", "Other"] }),
      book({ id: 2, series: "Spinoff", additionalSeries: ["Main"] }),
    ]);

    // "Main" is primary on one book only, "Spinoff" on the other: both are real changes for someone.
    expect(shared).toEqual(["Main", "Spinoff"]);
  });

  it("lists a series that is already the primary of every selected book, flagged as already set", () => {
    const options = computeSharedSeries([
      book({ series: "Main", additionalSeries: ["Spinoff"] }),
      book({ id: 2, series: "Main", additionalSeries: ["Spinoff"] }),
    ]);

    expect(options).toEqual([
      { value: "Main", alreadySet: true },
      { value: "Spinoff", alreadySet: false },
    ]);
  });

  it("does not flag a series that is primary on only some of the books", () => {
    const options = computeSharedSeries([
      book({ series: "Main", additionalSeries: ["Spinoff"] }),
      book({ id: 2, series: "Spinoff", additionalSeries: ["Main"] }),
    ]);

    expect(options).toEqual([
      { value: "Main", alreadySet: false },
      { value: "Spinoff", alreadySet: false },
    ]);
  });

  it("flags a series that is primary everywhere when the spelling differs only by case or space", () => {
    expect(
      computeSharedSeries([book({ series: "Main Saga" }), book({ id: 2, series: " main saga " })]),
    ).toEqual([{ value: "Main Saga", alreadySet: true }]);
  });

  it("is empty when no series is in every book", () => {
    expect(
      seriesNames([
        book({ series: "Main", additionalSeries: [] }),
        book({ id: 2, series: "Other", additionalSeries: [] }),
      ]),
    ).toEqual([]);
  });

  it("never offers a series a book is not in - the action reorders, it cannot add", () => {
    // "Spinoff" is only on one book; "Main" is on both (and primary on both).
    expect(
      seriesNames([
        book({ series: "Main", additionalSeries: ["Spinoff"] }),
        book({ id: 2, series: "Main", additionalSeries: [] }),
      ]),
    ).toEqual(["Main"]);
  });

  it("matches names ignoring case and surrounding space, keeping the first book's spelling", () => {
    expect(
      seriesNames([
        book({ series: "Main", additionalSeries: ["Spinoff Saga"] }),
        book({ id: 2, series: "Main", additionalSeries: [" spinoff saga "] }),
      ]),
    ).toEqual(["Main", "Spinoff Saga"]);
  });

  it("treats a book with no primary series as having only its additional ones", () => {
    expect(
      seriesNames([
        book({ series: null, additionalSeries: ["Spinoff"] }),
        book({ id: 2, series: "Main", additionalSeries: ["Spinoff"] }),
      ]),
    ).toEqual(["Spinoff"]);
  });

  it("is empty for no books", () => {
    expect(seriesNames([])).toEqual([]);
  });
});

describe("computeSharedAuthors alreadySet", () => {
  it("flags an author who is already the main author of every selected book", () => {
    expect(
      computeSharedAuthors([
        book({ authors: ["A One", "B Two"], mainAuthor: "B Two" }),
        book({ id: 2, authors: ["B Two", "A One"], mainAuthor: "B Two" }),
      ]),
    ).toEqual([
      { value: "A One", alreadySet: false },
      { value: "B Two", alreadySet: true },
    ]);
  });

  it("does not flag an author who is the main author of only some of the books", () => {
    expect(
      computeSharedAuthors([
        book({ authors: ["A One", "B Two"], mainAuthor: "B Two" }),
        book({ id: 2, authors: ["A One", "B Two"] }),
      ]),
    ).toEqual([
      { value: "A One", alreadySet: false },
      { value: "B Two", alreadySet: false },
    ]);
  });
});
