import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LibrarySettingsPage } from "./LibrarySettingsPage";
import type * as SonnerModule from "sonner";
import { toast } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  return {
    ...actual,
    toast: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
    },
  };
});

vi.mock("@/services/api", () => ({
  settingsApi: {
    getLibrarySettings: vi.fn(),
    updateLibrarySettings: vi.fn(),
  },
}));

import { settingsApi } from "@/services/api";

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <LibrarySettingsPage />
    </QueryClientProvider>,
  );
}

describe("LibrarySettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads and displays the current initials spacing", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      initialsSpacing: "Unspaced",
    });

    renderPage();

    expect(await screen.findByText("Library Settings")).toBeInTheDocument();
    expect(await screen.findByRole("combobox")).toHaveTextContent("Unspaced (J.K. Rowling)");
  });

  it("sends an update when a different spacing is chosen and shows a success toast", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      initialsSpacing: "Unspaced",
    });
    vi.mocked(settingsApi.updateLibrarySettings).mockResolvedValue({
      initialsSpacing: "Spaced",
    });

    renderPage();

    const comboBox = await screen.findByRole("combobox");
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
      });
    });
    expect(toast.success).toHaveBeenCalledWith("Library settings saved");
  });

  it("surfaces save errors via toast", async () => {
    const user = userEvent.setup();
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      initialsSpacing: "Spaced",
    });
    vi.mocked(settingsApi.updateLibrarySettings).mockRejectedValue(new Error("nope"));

    renderPage();

    const comboBox = await screen.findByRole("combobox");
    await user.click(comboBox);
    const spacedOption = (await screen.findAllByRole("option")).find(
      (el) => el.textContent === "Spaced (J. K. Rowling)",
    );
    expect(spacedOption).toBeDefined();
    await user.click(spacedOption!);
    await user.click(screen.getByRole("button", { name: /^Save$/ }));

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalled();
    });
  });
});
