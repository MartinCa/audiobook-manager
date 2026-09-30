import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { QualifierIndicatorsCard } from "./QualifierIndicatorsCard";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getQualifierIndicators: vi.fn(),
    updateQualifierIndicators: vi.fn(),
    getBookQualifiers: vi.fn(),
  },
  metadataSearchApi: { getServices: vi.fn() },
}));

import { settingsApi, metadataSearchApi } from "@/services/api";

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <QualifierIndicatorsCard />
    </QueryClientProvider>,
  );
}

describe("QualifierIndicatorsCard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(settingsApi.getBookQualifiers).mockResolvedValue({
      qualifiers: [
        { key: "abridged", label: "Abridged", suffix: " (Abridged)" },
        { key: "dramatized", label: "Dramatized", suffix: " (Dramatized)" },
      ],
    });
    vi.mocked(metadataSearchApi.getServices).mockResolvedValue([
      { name: "Audible", enabled: true },
      { name: "Goodreads", enabled: true },
    ]);
    vi.mocked(settingsApi.getQualifierIndicators).mockResolvedValue({
      indicators: [
        { source: "Audible", indicator: "Dramatized Adaptation", qualifierKey: "dramatized" },
      ],
    });
    vi.mocked(settingsApi.updateQualifierIndicators).mockImplementation((indicators) =>
      Promise.resolve({ indicators }),
    );
  });

  it("lists the saved rules", async () => {
    renderCard();

    expect(await screen.findByDisplayValue("Dramatized Adaptation")).toBeInTheDocument();
  });

  it("saves an added rule together with the existing ones, keyed by source and qualifier", async () => {
    const user = userEvent.setup();
    renderCard();
    await screen.findByDisplayValue("Dramatized Adaptation");
    // The add button needs the source list, which loads separately.
    const add = await screen.findByRole("button", { name: /add indicator/i });
    await waitFor(() => expect(add).toBeEnabled());

    await user.click(add);
    const inputs = await screen.findAllByLabelText("Indicator");
    await user.type(inputs[1]!, "[[Abridged]"); // user-event reads a lone "[" as a key descriptor
    await user.click(screen.getByRole("button", { name: /save indicators/i }));

    await waitFor(() =>
      expect(settingsApi.updateQualifierIndicators).toHaveBeenCalledWith([
        { source: "Audible", indicator: "Dramatized Adaptation", qualifierKey: "dramatized" },
        { source: "Audible", indicator: "[Abridged]", qualifierKey: "abridged" },
      ]),
    );
  });

  it("does not let an empty row be saved", async () => {
    const user = userEvent.setup();
    renderCard();
    await screen.findByDisplayValue("Dramatized Adaptation");
    const add = await screen.findByRole("button", { name: /add indicator/i });
    await waitFor(() => expect(add).toBeEnabled());

    await user.click(add);

    expect(screen.getByRole("button", { name: /save indicators/i })).toBeDisabled();
    expect(screen.getByText(/fill in or remove empty rows/i)).toBeInTheDocument();
  });

  it("removes a rule and saves the list without it", async () => {
    const user = userEvent.setup();
    renderCard();
    await screen.findByDisplayValue("Dramatized Adaptation");

    await user.click(screen.getByRole("button", { name: /remove indicator/i }));
    await user.click(screen.getByRole("button", { name: /save indicators/i }));

    await waitFor(() => expect(settingsApi.updateQualifierIndicators).toHaveBeenCalledWith([]));
  });

  it("has nothing to save until something is changed", async () => {
    renderCard();
    await screen.findByDisplayValue("Dramatized Adaptation");

    expect(screen.getByRole("button", { name: /save indicators/i })).toBeDisabled();
  });

  it("blocks saving two rows that are the same rule, however they are written", async () => {
    vi.mocked(settingsApi.getQualifierIndicators).mockResolvedValue({
      indicators: [
        { source: "Audible", indicator: "Dramatized Adaptation", qualifierKey: "dramatized" },
        { source: "Audible", indicator: "[dramatized  adaptation]", qualifierKey: "dramatized" },
      ],
    });
    const user = userEvent.setup();
    renderCard();
    await screen.findByDisplayValue("Dramatized Adaptation");
    await user.type(screen.getAllByLabelText("Indicator")[0]!, "x");
    await user.type(screen.getAllByLabelText("Indicator")[0]!, "{Backspace}");

    expect(screen.getByText(/remove duplicate rows/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save indicators/i })).toBeDisabled();
  });

  it("blocks saving a row whose source is no longer registered", async () => {
    vi.mocked(settingsApi.getQualifierIndicators).mockResolvedValue({
      indicators: [{ source: "Gone", indicator: "Abridged", qualifierKey: "abridged" }],
    });
    const user = userEvent.setup();
    renderCard();
    await screen.findByDisplayValue("Abridged");
    await user.type(screen.getByLabelText("Indicator"), "x");
    await user.type(screen.getByLabelText("Indicator"), "{Backspace}");

    expect(await screen.findByText(/source that no longer exists/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save indicators/i })).toBeDisabled();
  });
});
