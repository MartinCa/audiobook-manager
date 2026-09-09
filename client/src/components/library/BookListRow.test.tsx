import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import type { ReactNode } from "react";
import { BookListRow } from "./BookListRow";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

const mockNavigatedTo: string[] = [];

vi.mock("@tanstack/react-router", () => ({
  Link: ({
    to,
    params,
    children,
    className,
  }: {
    to: string;
    params?: { bookId?: string };
    children: ReactNode;
    className?: string;
  }) => (
    <a
      href={`/library/book/${params?.bookId}`}
      className={className}
      onClick={() => mockNavigatedTo.push(to)}
    >
      {children}
    </a>
  ),
}));

vi.mock("@/services/api", () => ({
  browseApi: {
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
  },
}));

function book(overrides: Partial<ManagedAudiobook> = {}): ManagedAudiobook {
  return {
    id: 1,
    bookName: "The Way of Kings",
    year: 2010,
    authors: ["Brandon Sanderson"],
    series: "The Stormlight Archive",
    seriesPart: "1",
    narrators: ["Michael Kramer", "Kate Reading"],
    genres: ["Fantasy"],
    durationInSeconds: 3661,
    coverFilePath: "/covers/1.jpg",
    ...overrides,
  };
}

describe("BookListRow", () => {
  beforeEach(() => {
    mockNavigatedTo.splice(0);
  });

  it("renders the title, year, series, narrators, and duration", () => {
    render(<BookListRow book={book()} />);

    expect(screen.getByText("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("(2010)")).toBeInTheDocument();
    expect(screen.getByText("By Brandon Sanderson ·")).toBeInTheDocument();
    expect(screen.getByText("Series: The Stormlight Archive #1 ·")).toBeInTheDocument();
    expect(screen.getByText("Narrated by Michael Kramer, Kate Reading ·")).toBeInTheDocument();
    expect(screen.getByText("1h 1m 1s")).toBeInTheDocument();
  });

  it("renders the cover image for a book with cover art", () => {
    render(<BookListRow book={book()} />);

    const img = screen.getByAltText<HTMLImageElement>("The Way of Kings");
    expect(img).toHaveAttribute("src", "/api/browse/audiobooks/1/cover");
    expect(img.closest("a")).toHaveAttribute("href", "/library/book/1");
  });

  it("hides a cover that fails to load instead of showing a broken image", () => {
    render(<BookListRow book={book()} />);

    const img = screen.getByAltText<HTMLImageElement>("The Way of Kings");
    fireEvent.error(img);
    expect(img).toHaveStyle({ display: "none" });
  });

  it("renders a placeholder icon when the book has no cover art", () => {
    const { container } = render(<BookListRow book={book({ coverFilePath: null })} />);

    expect(screen.queryByAltText("The Way of Kings")).not.toBeInTheDocument();
    expect(container.querySelector("svg.lucide-library")).not.toBeNull();
  });

  it("shows the issue-count badge and the pending-refresh badge", () => {
    render(<BookListRow book={book()} issueCount={2} hasPendingRefresh />);

    expect(screen.getByText("2 issues")).toBeInTheDocument();
    expect(screen.getByText("Pending refresh")).toBeInTheDocument();
  });

  it("keeps the issue badge singular for a single issue", () => {
    render(<BookListRow book={book()} issueCount={1} />);

    expect(screen.getByText("1 issue")).toBeInTheDocument();
  });

  it("prefixes the title with the series part when showSeriesPart is set", () => {
    render(<BookListRow book={book()} showSeriesPart />);

    expect(screen.getByText("#1 The Way of Kings")).toBeInTheDocument();
  });

  it("omits the series span when hideSeries is set but keeps the rest of the meta line", () => {
    render(<BookListRow book={book()} hideSeries />);

    expect(screen.queryByText(/Series: The Stormlight Archive/)).not.toBeInTheDocument();
    expect(screen.getByText("By Brandon Sanderson ·")).toBeInTheDocument();
  });

  it("renders no checkbox when not selectable", () => {
    render(<BookListRow book={book()} />);

    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
  });

  it("renders a labelled checkbox before the row content when selectable", () => {
    render(<BookListRow book={book()} selectable selected={false} onSelectedChange={vi.fn()} />);

    const checkbox = screen.getByRole("checkbox", { name: "Select The Way of Kings" });
    expect(checkbox).toHaveAttribute("aria-checked", "false");
    // The checkbox lives outside the row link, so checking it can never navigate.
    expect(checkbox.closest("a")).toBeNull();
  });

  it("toggles the selection through onSelectedChange without navigating", () => {
    const onSelectedChange = vi.fn();
    render(
      <BookListRow book={book()} selectable selected={false} onSelectedChange={onSelectedChange} />,
    );

    const checkbox = screen.getByRole("checkbox", { name: "Select The Way of Kings" });
    fireEvent.click(checkbox);

    expect(onSelectedChange).toHaveBeenCalledWith(true);
    expect(mockNavigatedTo).toEqual([]);
  });

  it("reports an unchecked row as selected through the checkbox state", () => {
    const onSelectedChange = vi.fn();
    render(<BookListRow book={book()} selectable selected onSelectedChange={onSelectedChange} />);

    const checkbox = screen.getByRole("checkbox", { name: "Select The Way of Kings" });
    fireEvent.click(checkbox);

    expect(onSelectedChange).toHaveBeenCalledWith(false);
  });

  it("navigates to the book when the row content itself is clicked", () => {
    render(<BookListRow book={book()} />);

    fireEvent.click(screen.getByText("The Way of Kings"));

    expect(mockNavigatedTo).toEqual(["/library/book/$bookId"]);
  });
});
