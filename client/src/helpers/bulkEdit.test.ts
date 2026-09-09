import { describe, it, expect } from "vitest";
import { computeMultiFieldState, computeSingleFieldState } from "./bulkEdit";
import type { BulkEditPreviewBook } from "@/types/BulkEdit";

function book(overrides: Partial<BulkEditPreviewBook> = {}): BulkEditPreviewBook {
  return {
    id: 1,
    authors: [],
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
