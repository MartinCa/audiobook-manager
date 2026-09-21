import { describe, it, expect, vi, beforeEach } from "vitest";
import type { ReactNode } from "react";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SignalRContext } from "@/context/SignalRContext";
import { OwnedBookList } from "./OwnedBookList";
import { useBookSelection } from "@/hooks/useBookSelection";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";
import type { BookListFilters } from "@/types/EntityFilters";

vi.mock("@tanstack/react-router", () => ({
  Link: ({
    params,
    children,
    className,
  }: {
    to: string;
    params?: { bookId?: string };
    children: ReactNode;
    className?: string;
  }) => (
    <a href={`/library/book/${params?.bookId}`} className={className}>
      {children}
    </a>
  ),
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  browseApi: {
    getCoverUrl: vi.fn((id: number) => `/api/browse/audiobooks/${id}/cover`),
    getFilterOptions: vi
      .fn()
      .mockResolvedValue({ sources: ["Hardcover"], genres: ["Fantasy"], languages: ["en"] }),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  consistencyApi: {
    getIssueSummary: vi.fn().mockResolvedValue({}),
    checkSelected: vi.fn().mockResolvedValue(undefined),
  },
  metadataRefreshApi: {
    getPendingSummary: vi.fn().mockResolvedValue([]),
    refreshSelected: vi.fn().mockResolvedValue(undefined),
  },
  operationsApi: {
    getStatus: vi.fn().mockResolvedValue({ isRunning: false, processed: 0, total: 0 }),
  },
  bulkEditApi: {
    preview: vi.fn(),
    apply: vi.fn(),
  },
}));

import { consistencyApi, metadataRefreshApi } from "@/services/api";

const mockSignalRValue = {
  connection: null,
  isConnected: false,
  on: vi.fn(),
  off: vi.fn(),
  onReconnected: vi.fn(),
  offReconnected: vi.fn(),
};

function book(overrides: Partial<ManagedAudiobook> = {}): ManagedAudiobook {
  return {
    id: 1,
    bookName: "The Way of Kings",
    year: 2010,
    authors: ["Brandon Sanderson"],
    narrators: [],
    genres: [],
    isMatched: false,
    matchedSourceName: null,
    ...overrides,
  };
}

interface HarnessProps {
  books?: ManagedAudiobook[];
  totalCount?: number;
  loading?: boolean;
  search?: string;
  onSearchChange?: (v: string) => void;
  filters?: BookListFilters;
  onFiltersChange?: (f: BookListFilters) => void;
  page?: number;
  pageCount?: number;
  onPageChange?: (p: number) => void;
  showSeriesPart?: boolean;
  hideSeries?: boolean;
  showSearchBox?: boolean;
}

function Harness({
  books = [book()],
  totalCount = 1,
  loading = false,
  search = "",
  onSearchChange = vi.fn(),
  filters = {},
  onFiltersChange = vi.fn(),
  page = 0,
  pageCount = 1,
  onPageChange = vi.fn(),
  showSeriesPart,
  hideSeries,
  showSearchBox,
}: HarnessProps) {
  const selection = useBookSelection();
  return (
    <OwnedBookList
      books={books}
      totalCount={totalCount}
      loading={loading}
      emptyState={<p>No books.</p>}
      selection={selection}
      search={search}
      onSearchChange={onSearchChange}
      showSearchBox={showSearchBox}
      filters={filters}
      onFiltersChange={onFiltersChange}
      page={page}
      pageCount={pageCount}
      onPageChange={onPageChange}
      showSeriesPart={showSeriesPart}
      hideSeries={hideSeries}
    />
  );
}

function renderList(props: HarnessProps = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <SignalRContext.Provider value={mockSignalRValue}>
      <QueryClientProvider client={queryClient}>
        <Harness {...props} />
      </QueryClientProvider>
    </SignalRContext.Provider>,
  );
}

describe("OwnedBookList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(consistencyApi.getIssueSummary).mockResolvedValue({});
    vi.mocked(metadataRefreshApi.getPendingSummary).mockResolvedValue([]);
  });

  it("renders the given books through BookListRow", async () => {
    renderList({ books: [book(), book({ id: 2, bookName: "Words of Radiance" })], totalCount: 2 });

    expect(await screen.findByText("The Way of Kings")).toBeInTheDocument();
    expect(screen.getByText("Words of Radiance")).toBeInTheDocument();
  });

  it("shows the issue-count and pending-refresh badges from its own summary fetches", async () => {
    vi.mocked(consistencyApi.getIssueSummary).mockResolvedValue({ 1: 2 });
    vi.mocked(metadataRefreshApi.getPendingSummary).mockResolvedValue([1]);

    renderList();

    expect(await screen.findByText("2 issues")).toBeInTheDocument();
    expect(screen.getByText("Pending refresh")).toBeInTheDocument();
  });

  it("shows the empty state when there are no books and it is not loading", () => {
    renderList({ books: [], totalCount: 0 });

    expect(screen.getByText("No books.")).toBeInTheDocument();
  });

  it("shows a loading skeleton instead of the empty state while loading with no rows yet", () => {
    renderList({ books: [], totalCount: 0, loading: true });

    expect(screen.queryByText("No books.")).not.toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
  });

  it("debounces typed search text into onSearchChange", async () => {
    const onSearchChange = vi.fn();
    renderList({ onSearchChange });

    const input = screen.getByPlaceholderText(/Search title, author/i);
    fireEvent.change(input, { target: { value: "Kings" } });

    expect(onSearchChange).not.toHaveBeenCalled();
    await waitFor(() => expect(onSearchChange).toHaveBeenCalledWith("Kings"), { timeout: 1000 });
  });

  it("commits search immediately on Enter without waiting for the debounce", () => {
    const onSearchChange = vi.fn();
    renderList({ onSearchChange });

    const input = screen.getByPlaceholderText(/Search title, author/i);
    fireEvent.change(input, { target: { value: "Kings" } });
    fireEvent.keyDown(input, { key: "Enter" });

    expect(onSearchChange).toHaveBeenCalledWith("Kings");
  });

  it("clears the search box and commits an empty value through the clear button", () => {
    const onSearchChange = vi.fn();
    renderList({ search: "Kings", onSearchChange });

    const input = screen.getByPlaceholderText(/Search title, author/i);
    expect(input).toHaveValue("Kings");

    fireEvent.click(screen.getByRole("button", { name: /clear search/i }));

    expect(input).toHaveValue("");
    expect(onSearchChange).toHaveBeenCalledWith("");
  });

  it("expands the filter panel and reports a filter change through onFiltersChange", async () => {
    const onFiltersChange = vi.fn();
    renderList({ onFiltersChange });

    fireEvent.click(screen.getByRole("button", { name: /Filters/ }));
    const minInput = await screen.findByLabelText("Duration (minutes) minimum");
    fireEvent.change(minInput, { target: { value: "10" } });

    expect(onFiltersChange).toHaveBeenCalledWith({ minDurationInSeconds: 600 });
  });

  it("starts with the filter panel expanded when a filter is already active", () => {
    renderList({ filters: { sources: ["Hardcover"] } });

    expect(screen.getByLabelText("Duration (minutes) minimum")).toBeInTheDocument();
  });

  it("selects and deselects the whole page through the select-page checkbox", () => {
    renderList({
      books: [book(), book({ id: 2, bookName: "Words of Radiance" })],
      totalCount: 2,
    });

    const selectAll = screen.getByRole("checkbox", { name: "Select page" });
    expect(selectAll).toHaveAttribute("aria-checked", "false");

    fireEvent.click(selectAll);
    expect(screen.getByRole("checkbox", { name: "Select The Way of Kings" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
    expect(screen.getByRole("checkbox", { name: "Select Words of Radiance" })).toHaveAttribute(
      "aria-checked",
      "true",
    );
  });

  it("prefixes the title with the series part and hides the series line when asked", () => {
    renderList({
      books: [book({ series: "The Stormlight Archive", seriesPart: "1" })],
      showSeriesPart: true,
      hideSeries: true,
    });

    expect(screen.getByText("#1 The Way of Kings")).toBeInTheDocument();
    expect(screen.queryByText(/Series: The Stormlight Archive/)).not.toBeInTheDocument();
  });

  it("renders no pager when there is only one page", () => {
    renderList({ pageCount: 1 });

    expect(screen.queryByRole("button", { name: "Next" })).not.toBeInTheDocument();
  });

  it("hides the search input when showSearchBox is false but keeps the filter button", () => {
    renderList({ showSearchBox: false });

    expect(screen.queryByPlaceholderText(/Search title, author/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Filters/ })).toBeInTheDocument();
  });

  it("renders the pager and reports page changes when there is more than one page", () => {
    const onPageChange = vi.fn();
    renderList({ page: 0, pageCount: 3, totalCount: 120, onPageChange });

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(onPageChange).toHaveBeenCalledWith(1);
  });
});
