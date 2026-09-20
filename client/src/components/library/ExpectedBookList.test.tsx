import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { ExpectedBookList, SectionPager } from "./ExpectedBookList";
import type { ExpectedBookRow } from "@/helpers/expectedBooks";

function row(overrides: Partial<ExpectedBookRow> = {}): ExpectedBookRow {
  return {
    id: 1,
    title: "The Bands of Mourning",
    isIgnored: false,
    position: null,
    seriesName: null,
    sourceSeriesName: null,
    year: 2026,
    releaseDate: null,
    sourceUrl: null,
    ...overrides,
  };
}

function renderList({
  section = "missing",
  items = [],
  ignoredItems = [],
  ignoredTotal,
  showIgnored = false,
  ignoredPager,
  ...rest
}: {
  section?: "missing" | "upcoming";
  items?: ExpectedBookRow[];
  ignoredItems?: ExpectedBookRow[];
  ignoredTotal?: number;
  showIgnored?: boolean;
  ignoredPager?: {
    currentPage: number;
    pageCount: number;
    onPageChange: (page: number) => void;
  };
  busyBookId?: number | null;
  emptyMessage?: string;
  onIgnore?: (book: ExpectedBookRow) => void;
  onUnignore?: (book: ExpectedBookRow) => void;
  onFindInLibrary?: (book: ExpectedBookRow) => void;
} = {}) {
  return render(
    <ExpectedBookList
      section={section}
      items={items}
      ignoredItems={ignoredItems}
      ignoredTotal={ignoredTotal ?? ignoredItems.length}
      showIgnored={showIgnored}
      ignoredPager={ignoredPager}
      busyBookId={rest.busyBookId ?? null}
      emptyMessage={rest.emptyMessage ?? "Nothing here."}
      onIgnore={rest.onIgnore ?? vi.fn()}
      onUnignore={rest.onUnignore ?? vi.fn()}
      onFindInLibrary={rest.onFindInLibrary}
    />,
  );
}

describe("ExpectedBookList", () => {
  it("renders title, year, precise release date, series name/position and a source link", () => {
    renderList({
      items: [
        row({
          id: 1,
          seriesName: "Mistborn",
          position: "4",
          year: 2026,
          releaseDate: "2026-11-01",
          sourceUrl: "https://hardcover.app/books/1",
        }),
      ],
    });

    expect(screen.getByText("Mistborn")).toBeInTheDocument();
    expect(screen.getByText(/Part 4 — The Bands of Mourning/)).toBeInTheDocument();
    expect(screen.getByText("(2026)")).toBeInTheDocument();
    expect(screen.getByText(/releases 2026-11-01/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Source" })).toHaveAttribute(
      "href",
      "https://hardcover.app/books/1",
    );
    expect(screen.getByRole("button", { name: "Ignore" })).toBeInTheDocument();
  });

  it("prefers the matched series name over the source's spelling", () => {
    renderList({
      items: [
        row({
          seriesName: "The Mistborn Saga",
          sourceSeriesName: "Mistborn (SA)",
        }),
      ],
    });

    expect(screen.getByText("The Mistborn Saga")).toBeInTheDocument();
    expect(screen.queryByText("Mistborn (SA)")).not.toBeInTheDocument();
  });

  it("shows the empty message when neither active nor (visible) ignored rows exist", () => {
    renderList({ emptyMessage: "No books yet." });

    expect(screen.getByText("No books yet.")).toBeInTheDocument();
  });

  it("invokes onIgnore for active rows and onUnignore for ignored rows", () => {
    const onIgnore = vi.fn();
    const onUnignore = vi.fn();
    const ignored = row({ id: 2, title: "The Lost Metal", isIgnored: true });
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: [ignored],
      showIgnored: true,
      onIgnore,
      onUnignore,
    });

    fireEvent.click(screen.getByRole("button", { name: "Ignore" }));
    expect(onIgnore).toHaveBeenCalledWith(expect.objectContaining({ id: 1 }));

    fireEvent.click(screen.getByRole("button", { name: "Unignore" }));
    expect(onUnignore).toHaveBeenCalledWith(expect.objectContaining({ id: 2 }));
  });

  it("keeps ignored rows hidden until showIgnored is on, then renders them faded", () => {
    const ignored = row({ id: 2, title: "The Lost Metal", isIgnored: true, year: 2019 });
    const { rerender } = renderList({ items: [row({ id: 1 })], ignoredItems: [ignored] });

    expect(screen.getByText("The Bands of Mourning")).toBeInTheDocument();
    expect(screen.queryByText("The Lost Metal")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Unignore" })).not.toBeInTheDocument();

    rerender(
      <ExpectedBookList
        section="missing"
        items={[row({ id: 1 })]}
        ignoredItems={[ignored]}
        ignoredTotal={1}
        showIgnored
        busyBookId={null}
        emptyMessage="Nothing here."
        onIgnore={vi.fn()}
        onUnignore={vi.fn()}
      />,
    );

    expect(screen.getByText("The Lost Metal")).toHaveClass("text-muted-foreground");
    expect(screen.getByRole("button", { name: "Unignore" })).toBeInTheDocument();
  });

  it("lands a scope's past-dated ignored rows in the Missing section", () => {
    const past = row({ id: 2, title: "Ignored Past Book", isIgnored: true, year: 2018 });
    const future = row({
      id: 3,
      title: "Ignored Future Book",
      isIgnored: true,
      releaseDate: "2031-05-05",
      year: null,
    });

    renderList({ section: "missing", items: [], ignoredItems: [past, future], showIgnored: true });
    expect(screen.getByText("Ignored Past Book")).toBeInTheDocument();
    expect(screen.queryByText("Ignored Future Book")).not.toBeInTheDocument();
  });

  it("lands a scope's future-dated ignored rows in the Upcoming section", () => {
    const past = row({ id: 2, title: "Ignored Past Book", isIgnored: true, year: 2018 });
    const future = row({
      id: 3,
      title: "Ignored Future Book",
      isIgnored: true,
      releaseDate: "2031-05-05",
      year: null,
    });

    renderList({ section: "upcoming", items: [], ignoredItems: [past, future], showIgnored: true });
    expect(screen.getByText("Ignored Future Book")).toBeInTheDocument();
    expect(screen.queryByText("Ignored Past Book")).not.toBeInTheDocument();
  });

  it("notes when the loaded ignored page does not carry every ignored entry", () => {
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: [],
      ignoredTotal: 3,
      showIgnored: true,
    });

    expect(screen.getByText("and 3 more ignored books...")).toBeInTheDocument();
  });

  it("renders the singular overflow note for a single further ignored entry", () => {
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: [],
      ignoredTotal: 1,
      showIgnored: true,
    });

    expect(screen.getByText("and 1 more ignored book...")).toBeInTheDocument();
  });

  // Regression for the review finding: with the ignored section paging server-side (SeriesDetail),
  // the pager is how the user reaches the remaining ignored entries. It must render in place of
  // the static overflow note - the note would otherwise claim those entries exist while the UI
  // offers no way to reach them.
  it("renders the ignored pager in place of the static overflow note when one is wired", () => {
    const onPageChange = vi.fn();
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: Array.from({ length: 50 }, (_, i) =>
        row({ id: 200 + i, title: `Ignored ${i}`, isIgnored: true, year: 2000 }),
      ),
      ignoredTotal: 60,
      showIgnored: true,
      ignoredPager: { currentPage: 0, pageCount: 2, onPageChange },
    });

    expect(screen.queryByText(/more ignored books\.\.\./)).not.toBeInTheDocument();
    expect(screen.getByText("Showing 1–50 of 60")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onPageChange).toHaveBeenCalledWith(1);
  });

  it("pages back through the ignored pager from a later page", () => {
    const onPageChange = vi.fn();
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: Array.from({ length: 10 }, (_, i) =>
        row({ id: 250 + i, title: `Ignored ${50 + i}`, isIgnored: true, year: 2000 }),
      ),
      ignoredTotal: 60,
      showIgnored: true,
      ignoredPager: { currentPage: 1, pageCount: 2, onPageChange },
    });

    expect(screen.getByText("Showing 51–60 of 60")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Previous" }));
    expect(onPageChange).toHaveBeenCalledWith(0);
  });

  it("keeps the static overflow note when no pager is wired (author detail)", () => {
    renderList({
      items: [row({ id: 1 })],
      ignoredItems: [],
      ignoredTotal: 3,
      showIgnored: true,
    });

    expect(screen.getByText("and 3 more ignored books...")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Next" })).not.toBeInTheDocument();
  });
});

describe("SectionPager", () => {
  it("clamps the previous/next buttons and reports the requested page", () => {
    const onPageChange = vi.fn();
    const { rerender } = render(
      <SectionPager currentPage={0} pageCount={3} totalCount={120} onPageChange={onPageChange} />,
    );

    expect(screen.getByText("Showing 1–50 of 120")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Previous" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onPageChange).toHaveBeenCalledWith(1);

    rerender(
      <SectionPager currentPage={2} pageCount={3} totalCount={120} onPageChange={onPageChange} />,
    );
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Previous" }));
    expect(onPageChange).toHaveBeenCalledWith(1);
  });
});
