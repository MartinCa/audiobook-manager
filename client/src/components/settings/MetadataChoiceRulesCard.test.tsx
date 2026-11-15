import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MetadataChoiceRulesCard } from "./MetadataChoiceRulesCard";
import type { MetadataChoiceRules } from "@/types/MetadataChoiceRules";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getMetadataChoiceRules: vi.fn(),
    updateMetadataChoiceRules: vi.fn(),
  },
}));

import { settingsApi } from "@/services/api";

const namesMainOptions = [
  { key: "KeepCurrent", label: "Keep current", description: "Ignore the source's main one." },
  { key: "UseSources", label: "Use the source's", description: "Take the one the source names." },
  { key: "AskMe", label: "Ask me", description: "Hold the book for review." },
];

const rules: MetadataChoiceRules = {
  choices: [
    {
      choice: "MainAuthor",
      label: "Main author",
      description: "Which author stands for the book.",
      whenSourceNamesMain: "KeepCurrent",
      whenSourceSilent: "KeepCurrent",
      namesMainOptions,
      silentOptions: [
        { key: "KeepCurrent", label: "Keep current", description: "Leave it as it is." },
        { key: "FirstListed", label: "First listed", description: "Fill a gap with the first." },
        { key: "None", label: "No main author", description: "Clear it." },
        { key: "AskMe", label: "Ask me", description: "Hold the book for review." },
      ],
      warning: "A source's author order is not a designation.",
    },
    {
      choice: "PrimarySeries",
      label: "Primary series",
      description: "Which series files the book.",
      whenSourceNamesMain: "KeepCurrent",
      whenSourceSilent: "KeepCurrent",
      namesMainOptions,
      silentOptions: [
        { key: "KeepCurrent", label: "Keep current", description: "Leave it as it is." },
        { key: "FirstListed", label: "First listed", description: "Take the first." },
      ],
      warning: null,
    },
  ],
};

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MetadataChoiceRulesCard />
    </QueryClientProvider>,
  );
}

describe("MetadataChoiceRulesCard", () => {
  beforeEach(() => {
    vi.mocked(settingsApi.getMetadataChoiceRules).mockReset().mockResolvedValue(rules);
    vi.mocked(settingsApi.updateMetadataChoiceRules).mockReset();
  });

  it("lists the choices the server serves, each with its warning and explained options", async () => {
    renderCard();

    expect(await screen.findByText("Main author")).toBeInTheDocument();
    expect(screen.getByText("Primary series")).toBeInTheDocument();
    expect(screen.getByText("A source's author order is not a designation.")).toBeInTheDocument();
    expect(screen.getAllByText("Take the one the source names.")).toHaveLength(2);
    expect(screen.getByText("Clear it.")).toBeInTheDocument();
  });

  it("keeps Save disabled until a rule is changed", async () => {
    renderCard();

    expect(await screen.findByRole("button", { name: /save rules/i })).toBeDisabled();
  });

  it("saves only the choice that changed, with both of its settings", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.updateMetadataChoiceRules).mockResolvedValue(rules);
    renderCard();

    await user.click(
      await screen.findByRole("combobox", {
        name: "Main author: when the source names a main one",
      }),
    );
    await user.click(await screen.findByRole("option", { name: "Use the source's" }));
    await user.click(screen.getByRole("button", { name: /save rules/i }));

    await waitFor(() => expect(settingsApi.updateMetadataChoiceRules).toHaveBeenCalled());
    expect(settingsApi.updateMetadataChoiceRules).toHaveBeenCalledWith({
      rules: [
        {
          choice: "MainAuthor",
          whenSourceNamesMain: "UseSources",
          whenSourceSilent: "KeepCurrent",
        },
      ],
    });
  });

  it("offers No main author only where the server's silent options include it", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(
      await screen.findByRole("combobox", { name: "Primary series: when the source is silent" }),
    );
    expect(await screen.findByRole("option", { name: "First listed" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "No main author" })).not.toBeInTheDocument();
  });

  it("drops a change that is put back to the saved value, so nothing is sent", async () => {
    const user = userEvent.setup();
    renderCard();

    const select = await screen.findByRole("combobox", {
      name: "Main author: when the source is silent",
    });
    await user.click(select);
    await user.click(await screen.findByRole("option", { name: "First listed" }));
    expect(screen.getByRole("button", { name: /save rules/i })).toBeEnabled();

    await user.click(select);
    await user.click(await screen.findByRole("option", { name: "Keep current" }));
    expect(screen.getByRole("button", { name: /save rules/i })).toBeDisabled();
  });
});
