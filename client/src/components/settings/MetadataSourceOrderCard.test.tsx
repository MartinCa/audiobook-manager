import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MetadataSourceOrderCard } from "./MetadataSourceOrderCard";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  settingsApi: { updateMetadataSourceOrder: vi.fn() },
  metadataSearchApi: { getServices: vi.fn() },
}));

import { metadataSearchApi, settingsApi } from "@/services/api";
import { notifications } from "@/lib/notifications";

const services = [
  { name: "Audible", enabled: true },
  { name: "Goodreads", enabled: true },
  { name: "Hardcover", enabled: false, disabledReason: "API key not configured" },
];

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const invalidate = vi.spyOn(queryClient, "invalidateQueries");
  render(
    <QueryClientProvider client={queryClient}>
      <MetadataSourceOrderCard />
    </QueryClientProvider>,
  );
  return { invalidate };
}

function listedNames() {
  return within(screen.getByRole("list"))
    .getAllByRole("listitem")
    .map((li) => li.textContent?.replace(/^\d+/, "").trim());
}

describe("MetadataSourceOrderCard", () => {
  beforeEach(() => {
    vi.mocked(metadataSearchApi.getServices).mockReset().mockResolvedValue(services);
    vi.mocked(settingsApi.updateMetadataSourceOrder).mockReset();
    vi.mocked(notifications.success).mockReset();
  });

  it("lists the sources in the order the server serves them", async () => {
    renderCard();

    await screen.findByText("Goodreads");
    expect(listedNames()).toEqual(["Audible", "Goodreads", "Hardcover"]);
  });

  it("moves a source and saves exactly the new order, then refreshes everything that lists sources", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.updateMetadataSourceOrder).mockResolvedValue({
      sources: ["Hardcover", "Audible", "Goodreads"],
    });
    const { invalidate } = renderCard();
    await screen.findByText("Goodreads");

    expect(screen.getByRole("button", { name: "Save order" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Move Hardcover up" }));
    await user.click(screen.getByRole("button", { name: "Move Hardcover up" }));
    expect(listedNames()).toEqual(["Hardcover", "Audible", "Goodreads"]);

    await user.click(screen.getByRole("button", { name: "Save order" }));

    await waitFor(() =>
      expect(settingsApi.updateMetadataSourceOrder).toHaveBeenCalledWith([
        "Hardcover",
        "Audible",
        "Goodreads",
      ]),
    );
    await waitFor(() => expect(notifications.success).toHaveBeenCalledWith("Source order saved"));
    const invalidatedKeys = invalidate.mock.calls.map(
      (c) => (c[0] as { queryKey: readonly string[] }).queryKey[0],
    );
    expect(invalidatedKeys).toEqual(
      expect.arrayContaining(["metadataServices", "browseFilterOptions", "pendingOnlineMatch"]),
    );
  });

  it("cannot move the first source up or the last one down", async () => {
    renderCard();
    await screen.findByText("Goodreads");

    expect(screen.getByRole("button", { name: "Move Audible up" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move Hardcover down" })).toBeDisabled();
  });

  it("does not offer to save once a move has been undone", async () => {
    const user = userEvent.setup();
    renderCard();
    await screen.findByText("Goodreads");

    await user.click(screen.getByRole("button", { name: "Move Goodreads up" }));
    expect(screen.getByRole("button", { name: "Save order" })).toBeEnabled();
    await user.click(screen.getByRole("button", { name: "Move Goodreads down" }));

    expect(listedNames()).toEqual(["Audible", "Goodreads", "Hardcover"]);
    expect(screen.getByRole("button", { name: "Save order" })).toBeDisabled();
  });
});
