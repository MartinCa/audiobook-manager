import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuthorConsistencyIssueList } from "./AuthorConsistencyIssueList";
import { browseApi } from "@/services/api";
import { notifications } from "@/lib/notifications";
import type * as ApiModule from "@/services/api";

vi.mock("@/services/api", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiModule>();
  return {
    ...actual,
    browseApi: {
      ...actual.browseApi,
      getAuthorConsistencyIssuesPage: vi.fn(),
      refreshAuthor: vi.fn(),
    },
  };
});

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

function renderList() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthorConsistencyIssueList />
    </QueryClientProvider>,
  );
}

describe("AuthorConsistencyIssueList", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("renders nothing once loaded when there are no failures", async () => {
    vi.mocked(browseApi.getAuthorConsistencyIssuesPage).mockResolvedValue({
      items: [],
      totalCount: 0,
    });

    const { container } = renderList();

    await waitFor(() => expect(browseApi.getAuthorConsistencyIssuesPage).toHaveBeenCalled());
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it("lists a failed author with its error and a retry action", async () => {
    vi.mocked(browseApi.getAuthorConsistencyIssuesPage).mockResolvedValue({
      items: [
        {
          id: 1,
          personId: 7,
          authorName: "Brandon Sanderson",
          errorMessage: "The source returned an error.",
          detectedAt: "2024-01-01T00:00:00Z",
        },
      ],
      totalCount: 1,
    });

    renderList();

    expect(await screen.findByText("Brandon Sanderson")).toBeInTheDocument();
    expect(screen.getByText("The source returned an error.")).toBeInTheDocument();
    expect(screen.getByText("Author Refresh Failures (1)")).toBeInTheDocument();
  });

  it("retrying a successful refresh clears the row and shows a success toast", async () => {
    vi.mocked(browseApi.getAuthorConsistencyIssuesPage)
      .mockResolvedValueOnce({
        items: [
          {
            id: 1,
            personId: 7,
            authorName: "Brandon Sanderson",
            errorMessage: "The source returned an error.",
            detectedAt: "2024-01-01T00:00:00Z",
          },
        ],
        totalCount: 1,
      })
      .mockResolvedValueOnce({ items: [], totalCount: 0 });
    vi.mocked(browseApi.refreshAuthor).mockResolvedValue({ success: true, lastRefreshedAt: null });

    renderList();

    const retryButton = await screen.findByRole("button", { name: /retry/i });
    await userEvent.click(retryButton);

    await waitFor(() => expect(browseApi.refreshAuthor).toHaveBeenCalledWith(7));
    await waitFor(() => expect(notifications.success).toHaveBeenCalled());
    await waitFor(() => expect(browseApi.getAuthorConsistencyIssuesPage).toHaveBeenCalledTimes(2));
  });

  it("retrying a failure that fails again shows an error toast", async () => {
    vi.mocked(browseApi.getAuthorConsistencyIssuesPage).mockResolvedValue({
      items: [
        {
          id: 1,
          personId: 7,
          authorName: "Brandon Sanderson",
          errorMessage: "The source returned an error.",
          detectedAt: "2024-01-01T00:00:00Z",
        },
      ],
      totalCount: 1,
    });
    vi.mocked(browseApi.refreshAuthor).mockRejectedValue(new Error("Still failing"));

    renderList();

    const retryButton = await screen.findByRole("button", { name: /retry/i });
    await userEvent.click(retryButton);

    await waitFor(() => expect(notifications.error).toHaveBeenCalled());
  });
});
