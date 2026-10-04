import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MetadataApplyRulesCard } from "./MetadataApplyRulesCard";
import type { MetadataApplyRules } from "@/types/MetadataApplyRules";

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getMetadataApplyRules: vi.fn(),
    updateMetadataApplyRules: vi.fn(),
  },
}));

import { settingsApi } from "@/services/api";

const rules: MetadataApplyRules = {
  fields: [
    {
      field: "Authors",
      label: "Authors",
      interactive: "AlwaysSelect",
      automated: "AskMe",
      alwaysOverwriteAllowed: false,
    },
    {
      field: "Series",
      label: "Series",
      interactive: "AlwaysSelect",
      automated: "AskMe",
      alwaysOverwriteAllowed: true,
      alwaysOverwriteWarning: "An empty source would remove the book's series.",
    },
    {
      field: "Description",
      label: "Description",
      interactive: "AlwaysSelect",
      automated: "AskMe",
      alwaysOverwriteAllowed: true,
    },
  ],
  interactiveOptions: [
    { key: "AlwaysSelect", label: "Always select", description: "Ticked whenever it differs." },
    { key: "NeverSelect", label: "Never select", description: "Starts unticked." },
  ],
  automatedOptions: [
    { key: "AskMe", label: "Ask me", description: "Send the changeset to review." },
    { key: "AlwaysOverwrite", label: "Always overwrite", description: "Apply no matter what." },
    { key: "KeepCurrent", label: "Keep current", description: "Never apply." },
  ],
};

function renderCard() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <MetadataApplyRulesCard />
    </QueryClientProvider>,
  );
}

describe("MetadataApplyRulesCard", () => {
  beforeEach(() => {
    vi.mocked(settingsApi.getMetadataApplyRules).mockReset().mockResolvedValue(rules);
    vi.mocked(settingsApi.updateMetadataApplyRules).mockReset();
  });

  it("lists the fields the server serves, with each option explained", async () => {
    renderCard();

    expect(await screen.findByText("Authors")).toBeInTheDocument();
    expect(screen.getByText("Series")).toBeInTheDocument();
    expect(screen.getByText("Description")).toBeInTheDocument();
    // The legend carries the server's explanation of every option.
    expect(screen.getByText("Ticked whenever it differs.")).toBeInTheDocument();
    expect(screen.getByText("Send the changeset to review.")).toBeInTheDocument();
  });

  it("titles both selects of every field with visible text, not just an aria-label", async () => {
    renderCard();
    await screen.findByText("Authors");

    // Per field: one title above each select (shown on narrow screens), plus the card description,
    // the wide-screen column
    // header and the legend heading. A bare aria-label would leave a phone with two unlabelled
    // dropdowns per field.
    const perFieldAndShared = rules.fields.length + 3;
    expect(screen.getAllByText("When reviewing")).toHaveLength(perFieldAndShared);
    expect(screen.getAllByText("When automated")).toHaveLength(perFieldAndShared);
  });

  it("keeps Save disabled until a rule is changed", async () => {
    renderCard();

    expect(await screen.findByRole("button", { name: /save rules/i })).toBeDisabled();
  });

  it("saves only the rows that changed", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.updateMetadataApplyRules).mockResolvedValue(rules);
    renderCard();

    await user.click(await screen.findByRole("combobox", { name: "Description: when automated" }));
    await user.click(await screen.findByRole("option", { name: "Keep current" }));
    await user.click(screen.getByRole("button", { name: /save rules/i }));

    await waitFor(() => expect(settingsApi.updateMetadataApplyRules).toHaveBeenCalled());
    expect(settingsApi.updateMetadataApplyRules).toHaveBeenCalledWith({
      rules: [{ field: "Description", interactive: "AlwaysSelect", automated: "KeepCurrent" }],
    });
  });

  it("does not offer Always overwrite on a required field", async () => {
    const user = userEvent.setup();
    renderCard();

    await user.click(await screen.findByRole("combobox", { name: "Authors: when automated" }));

    const option = await screen.findByRole("option", { name: /Always overwrite/ });
    expect(option).toHaveAttribute("aria-disabled", "true");
  });

  it("warns when Always overwrite is chosen on Series", async () => {
    const user = userEvent.setup();
    renderCard();

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    await user.click(await screen.findByRole("combobox", { name: "Series: when automated" }));
    await user.click(await screen.findByRole("option", { name: "Always overwrite" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "An empty source would remove the book's series.",
    );
  });
});
