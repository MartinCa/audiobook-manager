import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/lib/api";
import { filterPresetsApi } from "@/services/api";
import type { FilterPreset } from "@/types/FilterPreset";
import type { FilterFieldDef, FilterValueMap } from "./filterUtils";
import { FilterPresets } from "./FilterPresets";

vi.mock("@/services/api", () => ({
  filterPresetsApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
}));

vi.mock("@/lib/notifications", () => ({
  notifications: { success: vi.fn(), error: vi.fn(), info: vi.fn(), warning: vi.fn() },
}));

import { notifications } from "@/lib/notifications";

const FIELDS: FilterFieldDef[] = [
  {
    type: "multiselect",
    key: "sources",
    label: "Metadata source",
    options: ["Audible", "Unsupported"],
    optionLabels: { Unsupported: "Unsupported/None" },
  },
  {
    type: "multiselect",
    key: "queueStates",
    label: "Queue status",
    options: ["NotQueued", "MatchRejected"],
    optionLabels: { NotQueued: "Not in any queue", MatchRejected: "Online match rejected" },
  },
  {
    type: "tristate",
    key: "followed",
    label: "Followed",
    trueLabel: "Followed",
    falseLabel: "Not followed",
  },
];

function preset(id: number, name: string, filters: Record<string, unknown>): FilterPreset {
  return { id, scope: "books", name, filters, updatedAt: "2026-10-09T00:00:00Z" };
}

const BACKLOG = preset(1, "Unsupported backlog", {
  sources: ["Unsupported"],
  queueStates: ["NotQueued"],
});
const REJECTED = preset(2, "Retry rejected", {
  sources: ["Unsupported"],
  queueStates: ["MatchRejected"],
});

function renderPresets(
  props: Partial<React.ComponentProps<typeof FilterPresets>> = {},
  onApply = vi.fn(),
) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <FilterPresets scope="books" filters={{}} fields={FIELDS} onApply={onApply} {...props} />
    </QueryClientProvider>,
  );
  return onApply;
}

async function openMenu() {
  const trigger = await screen.findByRole("button", {
    name: /Apply a preset|Unsupported backlog|Retry rejected/,
  });
  await waitFor(() => expect(trigger).toBeEnabled());
  fireEvent.click(trigger);
}

describe("FilterPresets", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(filterPresetsApi.list).mockResolvedValue([BACKLOG, REJECTED]);
  });

  it("loads the presets of its own list", async () => {
    renderPresets({ scope: "authors" });

    await screen.findByRole("button", { name: "Apply a preset" });
    expect(filterPresetsApi.list).toHaveBeenCalledWith("authors");
  });

  it("applies a chosen preset, replacing the filters that are set", async () => {
    const onApply = renderPresets({ filters: { followed: true, sources: ["Audible"] } });

    await openMenu();
    fireEvent.click(await screen.findByRole("menuitemradio", { name: "Retry rejected" }));

    // Choosing a preset closes the menu - the result is on the page behind it.
    await waitFor(() => expect(screen.queryByRole("menu")).not.toBeInTheDocument());
    expect(onApply).toHaveBeenCalledTimes(1);
    const next = onApply.mock.calls[0]![0] as Record<string, unknown>;
    expect(next).toMatchObject({ sources: ["Unsupported"], queueStates: ["MatchRejected"] });
    // toHaveBeenCalledWith cannot tell `{ followed: undefined }` from `{}`, and the cleared key is
    // exactly what makes the list drop the old filter from its search params.
    expect(Object.keys(next).sort()).toEqual(["followed", "queueStates", "sources"]);
    expect(next.followed).toBeUndefined();
  });

  it("shows the name of the preset the current filters are", async () => {
    renderPresets({ filters: { queueStates: ["NotQueued"], sources: ["Unsupported"] } });

    expect(await screen.findByRole("button", { name: /Unsupported backlog/ })).toBeInTheDocument();
  });

  it("falls back to 'Apply a preset' once the filters are tweaked away from a preset", async () => {
    renderPresets({
      filters: { queueStates: ["NotQueued"], sources: ["Unsupported", "Audible"] },
    });

    expect(await screen.findByRole("button", { name: "Apply a preset" })).toBeInTheDocument();
  });

  it("explains an empty list and offers nothing to manage", async () => {
    vi.mocked(filterPresetsApi.list).mockResolvedValue([]);
    renderPresets({ filters: { followed: true } });

    await openMenu();

    expect(
      await screen.findByRole("menuitem", { name: "No presets saved yet" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("menuitem", { name: "Manage presets..." })).toHaveAttribute(
      "aria-disabled",
      "true",
    );
  });

  it("will not save when no filter is set", async () => {
    renderPresets({ filters: {} });

    await openMenu();

    expect(
      await screen.findByRole("menuitem", { name: "Save current filters..." }),
    ).toHaveAttribute("aria-disabled", "true");
  });

  it("saves the current filters under the typed name - and not the search text", async () => {
    vi.mocked(filterPresetsApi.create).mockResolvedValue(preset(3, "My preset", {}));
    renderPresets({ filters: { sources: ["Unsupported"], followed: undefined, genres: [] } });

    await openMenu();
    fireEvent.click(await screen.findByRole("menuitem", { name: "Save current filters..." }));
    fireEvent.change(await screen.findByLabelText("Preset name"), {
      target: { value: "  My preset  " },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save preset" }));

    await waitFor(() =>
      expect(filterPresetsApi.create).toHaveBeenCalledWith("books", "My preset", {
        sources: ["Unsupported"],
      }),
    );
    await waitFor(() =>
      expect(notifications.success).toHaveBeenCalledWith('Saved preset "My preset"'),
    );
    // The list is refetched after a write.
    await waitFor(() => expect(filterPresetsApi.list).toHaveBeenCalledTimes(2));
  });

  it("keeps the dialog open and shows the server's message when the name is taken", async () => {
    vi.mocked(filterPresetsApi.create).mockRejectedValue(
      new ApiError(409, {
        status: 409,
        detail: "A preset named 'Unsupported backlog' already exists for this list.",
      }),
    );
    renderPresets({ filters: { sources: ["Unsupported"] } });

    await openMenu();
    fireEvent.click(await screen.findByRole("menuitem", { name: "Save current filters..." }));
    fireEvent.change(await screen.findByLabelText("Preset name"), {
      target: { value: "Unsupported backlog" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save preset" }));

    expect(
      await screen.findByText("A preset named 'Unsupported backlog' already exists for this list."),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Preset name")).toBeInTheDocument();
    expect(notifications.success).not.toHaveBeenCalled();
  });

  it("stops typing at the longest name the backend accepts, so there is no dead Save click", async () => {
    renderPresets({ filters: { sources: ["Unsupported"] } });

    await openMenu();
    fireEvent.click(await screen.findByRole("menuitem", { name: "Save current filters..." }));

    expect(await screen.findByLabelText("Preset name")).toHaveAttribute("maxlength", "80");
  });

  it("does not offer to save a blank name", async () => {
    renderPresets({ filters: { sources: ["Unsupported"] } });

    await openMenu();
    fireEvent.click(await screen.findByRole("menuitem", { name: "Save current filters..." }));
    fireEvent.change(await screen.findByLabelText("Preset name"), { target: { value: "   " } });

    expect(screen.getByRole("button", { name: "Save preset" })).toBeDisabled();
  });

  it("shows what went wrong and offers a retry when the presets cannot be loaded", async () => {
    vi.mocked(filterPresetsApi.list).mockRejectedValueOnce(
      new ApiError(500, { status: 500, detail: "The presets are unavailable." }),
    );
    renderPresets();

    expect(
      await screen.findByText(/Filter presets could not be loaded: The presets are unavailable\./),
    ).toBeInTheDocument();

    vi.mocked(filterPresetsApi.list).mockResolvedValue([BACKLOG]);
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));

    expect(await screen.findByRole("button", { name: "Apply a preset" })).toBeInTheDocument();
  });

  describe("managing presets", () => {
    async function openManage(filters: FilterValueMap = { followed: true }) {
      renderPresets({ filters });
      await openMenu();
      fireEvent.click(await screen.findByRole("menuitem", { name: "Manage presets..." }));
      return await screen.findByRole("dialog", { name: "Manage filter presets" });
    }

    it("describes each preset by the filters it holds", async () => {
      const dialog = await openManage();

      expect(
        within(dialog).getByText(
          "Metadata source: Unsupported/None · Queue status: Not in any queue",
        ),
      ).toBeInTheDocument();
      expect(
        within(dialog).getByText(
          "Metadata source: Unsupported/None · Queue status: Online match rejected",
        ),
      ).toBeInTheDocument();
    });

    it("renames a preset, keeping its filters", async () => {
      vi.mocked(filterPresetsApi.update).mockResolvedValue(preset(1, "Backlog", {}));
      const dialog = await openManage();

      fireEvent.click(
        within(dialog).getByRole("button", { name: "Rename preset Unsupported backlog" }),
      );
      fireEvent.change(within(dialog).getByLabelText("New name for preset Unsupported backlog"), {
        target: { value: "Backlog" },
      });
      fireEvent.click(within(dialog).getByRole("button", { name: "Save new name" }));

      await waitFor(() =>
        expect(filterPresetsApi.update).toHaveBeenCalledWith(1, "Backlog", {
          sources: ["Unsupported"],
          queueStates: ["NotQueued"],
        }),
      );
    });

    it("limits a new name to the longest the backend accepts", async () => {
      const dialog = await openManage();

      fireEvent.click(
        within(dialog).getByRole("button", { name: "Rename preset Unsupported backlog" }),
      );

      expect(
        within(dialog).getByLabelText("New name for preset Unsupported backlog"),
      ).toHaveAttribute("maxlength", "80");
    });

    it("replaces a preset's filters with the current ones, keeping its name", async () => {
      vi.mocked(filterPresetsApi.update).mockResolvedValue(preset(2, "Retry rejected", {}));
      const dialog = await openManage({ genres: ["Fantasy"] });

      fireEvent.click(
        within(dialog).getByRole("button", {
          name: "Replace the filters of preset Retry rejected with the current filters",
        }),
      );

      await waitFor(() =>
        expect(filterPresetsApi.update).toHaveBeenCalledWith(2, "Retry rejected", {
          genres: ["Fantasy"],
        }),
      );
    });

    it("cannot replace a preset's filters when none are set, or when they already match", async () => {
      const dialog = await openManage({});
      expect(
        within(dialog).getByRole("button", {
          name: "Replace the filters of preset Retry rejected with the current filters",
        }),
      ).toBeDisabled();
    });

    it("disables the replace button for the preset the current filters already are", async () => {
      const dialog = await openManage({ sources: ["Unsupported"], queueStates: ["NotQueued"] });

      expect(
        within(dialog).getByRole("button", {
          name: "Replace the filters of preset Unsupported backlog with the current filters",
        }),
      ).toBeDisabled();
      expect(
        within(dialog).getByRole("button", {
          name: "Replace the filters of preset Retry rejected with the current filters",
        }),
      ).toBeEnabled();
    });

    it("deletes a preset only after a second, explicit click", async () => {
      vi.mocked(filterPresetsApi.remove).mockResolvedValue(undefined);
      const dialog = await openManage();

      fireEvent.click(within(dialog).getByRole("button", { name: "Delete preset Retry rejected" }));
      expect(filterPresetsApi.remove).not.toHaveBeenCalled();
      expect(within(dialog).getByText("Delete this preset?")).toBeInTheDocument();

      fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

      await waitFor(() => expect(filterPresetsApi.remove).toHaveBeenCalledWith(2));
    });

    it("backs out of a delete without calling the server", async () => {
      const dialog = await openManage();

      fireEvent.click(within(dialog).getByRole("button", { name: "Delete preset Retry rejected" }));
      fireEvent.click(within(dialog).getByRole("button", { name: "Keep" }));

      expect(filterPresetsApi.remove).not.toHaveBeenCalled();
      expect(within(dialog).queryByText("Delete this preset?")).not.toBeInTheDocument();
    });

    it("reports a failed rename and leaves the rename field open to correct", async () => {
      vi.mocked(filterPresetsApi.update).mockRejectedValue(
        new ApiError(409, {
          status: 409,
          detail: "A preset named 'Retry rejected' already exists.",
        }),
      );
      const dialog = await openManage();

      fireEvent.click(
        within(dialog).getByRole("button", { name: "Rename preset Unsupported backlog" }),
      );
      fireEvent.change(within(dialog).getByLabelText("New name for preset Unsupported backlog"), {
        target: { value: "Retry rejected" },
      });
      fireEvent.click(within(dialog).getByRole("button", { name: "Save new name" }));

      await waitFor(() =>
        expect(notifications.error).toHaveBeenCalledWith(
          "A preset named 'Retry rejected' already exists.",
        ),
      );
      expect(
        within(dialog).getByLabelText("New name for preset Unsupported backlog"),
      ).toBeInTheDocument();
    });
  });
});
