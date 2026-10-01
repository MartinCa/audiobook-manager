import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthorRefreshPendingList } from "./AuthorRefreshPendingList";
import { RouterTestWrapper } from "@/test-utils/routerTestUtils";
import { browseApi } from "@/services/api";
import type { ReactNode } from "react";

vi.mock("@/services/api", () => ({
  browseApi: {
    getAuthorPendingRefreshPage: vi.fn(),
    getAuthorPendingRefreshCount: vi.fn(),
    dismissAuthorPendingRefresh: vi.fn().mockResolvedValue(undefined),
  },
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/components/RenameValueDialog", () => ({
  RenameValueDialog: (props: { currentName: string; initialNewName?: string }): ReactNode => (
    <div data-testid="rename-dialog">
      {props.currentName}→{props.initialNewName}
    </div>
  ),
}));

function renderList() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterTestWrapper ui={<AuthorRefreshPendingList />} />
    </QueryClientProvider>,
  );
}

const item = {
  authorId: 7,
  authorName: "Robert Galbraith",
  proposedName: "J.K. Rowling",
  sourceName: "Hardcover",
  sourceUrl: null,
  fetchedAt: "2026-05-05T00:00:00Z",
};

describe("AuthorRefreshPendingList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(browseApi.getAuthorPendingRefreshCount).mockResolvedValue(1);
    vi.mocked(browseApi.getAuthorPendingRefreshPage).mockResolvedValue({
      items: [item],
      totalCount: 1,
    });
  });

  it("shows the current and proposed name with the count from the count endpoint", async () => {
    renderList();

    expect(await screen.findByText("Robert Galbraith")).toBeInTheDocument();
    expect(screen.getByText("J.K. Rowling")).toBeInTheDocument();
    expect(screen.getByText(/different name at the source \(1\)/i)).toBeInTheDocument();
  });

  it("explains the empty state when nothing is pending", async () => {
    vi.mocked(browseApi.getAuthorPendingRefreshCount).mockResolvedValue(0);
    vi.mocked(browseApi.getAuthorPendingRefreshPage).mockResolvedValue({
      items: [],
      totalCount: 0,
    });

    renderList();

    expect(await screen.findByText(/no author names to review/i)).toBeInTheDocument();
  });

  it("opens the rename dialog pre-filled with the proposal on Review", async () => {
    renderList();

    fireEvent.click(await screen.findByRole("button", { name: "Review" }));

    expect(screen.getByTestId("rename-dialog")).toHaveTextContent("Robert Galbraith→J.K. Rowling");
  });

  it("dismisses without renaming anything", async () => {
    renderList();

    fireEvent.click(await screen.findByRole("button", { name: "Dismiss" }));

    await waitFor(() => expect(browseApi.dismissAuthorPendingRefresh).toHaveBeenCalledWith(7));
    expect(screen.queryByTestId("rename-dialog")).not.toBeInTheDocument();
  });
});
