import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LibrarySettingsPage } from "./LibrarySettingsPage";
import { notifications } from "@/lib/notifications";
import type { LibrarySettings } from "@/types/LibrarySettings";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children, ...props }: React.ComponentProps<"a">) => <a {...props}>{children}</a>,
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

vi.mock("@/services/api", () => ({
  settingsApi: {
    getLibrarySettings: vi.fn(),
    updateLibrarySettings: vi.fn(),
  },
}));

import { settingsApi } from "@/services/api";

function renderPage(
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  return {
    queryClient,
    ...render(
      <QueryClientProvider client={queryClient}>
        <LibrarySettingsPage />
      </QueryClientProvider>,
    ),
  };
}

function makeSettings(overrides: Partial<LibrarySettings> = {}): LibrarySettings {
  return {
    initialsSpacing: "Unspaced",
    initialsPunctuation: "Dotted",
    metadataRefreshDelayMs: 1000,
    upcomingReleasesEnabled: true,
    upcomingReleasesCronSchedule: "0 3 * * *",
    ...overrides,
  };
}

// The spacing select is rendered before the punctuation select, so the first combobox is always
// spacing and the second is always punctuation.
async function findSpacingCombo() {
  const [spacing] = await screen.findAllByRole("combobox");
  if (!spacing) throw new Error("Expected the spacing combobox to be rendered");
  return spacing;
}

async function findPunctuationCombo() {
  const [, punctuation] = await screen.findAllByRole("combobox");
  if (!punctuation) throw new Error("Expected the punctuation combobox to be rendered");
  return punctuation;
}

describe("LibrarySettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads and displays the current initials spacing and punctuation", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(makeSettings());

    renderPage();

    expect(await screen.findByText("Library Settings")).toBeInTheDocument();
    expect(await findSpacingCombo()).toHaveTextContent("Unspaced (J.K. Rowling)");
    expect(await findPunctuationCombo()).toHaveTextContent("Dotted (J. R. R. Tolkien)");
  });

  it("loads and displays the current upcoming-releases schedule", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(
      makeSettings({ upcomingReleasesEnabled: true, upcomingReleasesCronSchedule: "0 4 * * *" }),
    );

    renderPage();

    const cronInput = await screen.findByPlaceholderText("0 3 * * *");
    expect(cronInput).toHaveValue("0 4 * * *");
    expect(screen.getByRole("checkbox")).toBeChecked();
  });

  it("sends an update when a different spacing is chosen and shows a success toast", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(makeSettings());
    vi.mocked(settingsApi.updateLibrarySettings).mockResolvedValue(
      makeSettings({ initialsSpacing: "Spaced" }),
    );

    renderPage();

    const comboBox = await findSpacingCombo();
    await user.click(comboBox);
    const spacedOption = (await screen.findAllByRole("option")).find(
      (el) => el.textContent === "Spaced (J. K. Rowling)",
    );
    expect(spacedOption).toBeDefined();
    await user.click(spacedOption!);

    await user.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(settingsApi.updateLibrarySettings).toHaveBeenCalledWith({
        initialsSpacing: "Spaced",
        initialsPunctuation: "Dotted",
        upcomingReleasesEnabled: true,
        upcomingReleasesCronSchedule: "0 3 * * *",
      });
    });
    expect(notifications.success).toHaveBeenCalledWith("Library settings saved");
  });

  it("sends an update when a different punctuation is chosen", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(makeSettings());
    vi.mocked(settingsApi.updateLibrarySettings).mockResolvedValue(
      makeSettings({ initialsPunctuation: "Undotted" }),
    );

    renderPage();

    const comboBox = await findPunctuationCombo();
    await user.click(comboBox);
    const undottedOption = (await screen.findAllByRole("option")).find(
      (el) => el.textContent === "Undotted (J R R Tolkien)",
    );
    expect(undottedOption).toBeDefined();
    await user.click(undottedOption!);

    await user.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(settingsApi.updateLibrarySettings).toHaveBeenCalledWith({
        initialsSpacing: "Unspaced",
        initialsPunctuation: "Undotted",
        upcomingReleasesEnabled: true,
        upcomingReleasesCronSchedule: "0 3 * * *",
      });
    });
    expect(notifications.success).toHaveBeenCalledWith("Library settings saved");
  });

  it("sends the edited upcoming-releases schedule along with the initials spacing", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(makeSettings());
    vi.mocked(settingsApi.updateLibrarySettings).mockResolvedValue(makeSettings());

    renderPage();

    const cronInput = await screen.findByPlaceholderText("0 3 * * *");
    await user.clear(cronInput);
    await user.type(cronInput, "0 5 * * *");
    await user.click(screen.getByRole("checkbox"));

    await user.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(settingsApi.updateLibrarySettings).toHaveBeenCalledWith({
        initialsSpacing: "Unspaced",
        initialsPunctuation: "Dotted",
        upcomingReleasesEnabled: false,
        upcomingReleasesCronSchedule: "0 5 * * *",
      });
    });
  });

  // Regression guard: the save mutation only invalidated its own librarySettings query, so the
  // Settings > Tasks page (which reads the same upcoming-releases enabled/cron values via
  // queryKeys.scheduledTasks()) kept showing the pre-save schedule until its own 30s refetch.
  it("also invalidates the scheduled-tasks query on a successful save", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(makeSettings());
    vi.mocked(settingsApi.updateLibrarySettings).mockResolvedValue(makeSettings());

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");
    renderPage(queryClient);

    await user.click(await screen.findByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(settingsApi.updateLibrarySettings).toHaveBeenCalled();
    });

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["librarySettings"] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ["scheduledTasks"] });
  });

  it("surfaces save errors via toast", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue(
      makeSettings({ initialsSpacing: "Spaced" }),
    );
    vi.mocked(settingsApi.updateLibrarySettings).mockRejectedValue(new Error("nope"));

    renderPage();

    const comboBox = await findSpacingCombo();
    await user.click(comboBox);
    const spacedOption = (await screen.findAllByRole("option")).find(
      (el) => el.textContent === "Spaced (J. K. Rowling)",
    );
    expect(spacedOption).toBeDefined();
    await user.click(spacedOption!);
    await user.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(notifications.error).toHaveBeenCalled();
    });
  });
});
