import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MainAuthorField } from "./MainAuthorField";
import { settingsApi } from "@/services/api";

vi.mock("@/services/api", () => ({
  settingsApi: { getLibrarySettings: vi.fn() },
}));

function renderField(props: Partial<React.ComponentProps<typeof MainAuthorField>> = {}) {
  const onChange = vi.fn();
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <MainAuthorField authors={["A One", "B Two"]} value="" onChange={onChange} {...props} />
    </QueryClientProvider>,
  );
  return { onChange };
}

describe("MainAuthorField", () => {
  it("shows 'No main author' when none is chosen", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    renderField();

    expect(await screen.findByRole("combobox", { name: "Main author" })).toHaveTextContent(
      "No main author",
    );
  });

  it("offers every author plus 'No main author', and reports the chosen name", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    const user = userEvent.setup();
    const { onChange } = renderField();

    await user.click(await screen.findByRole("combobox", { name: "Main author" }));
    const options = (await screen.findAllByRole("option")).map((o) => o.textContent);
    expect(options).toEqual(["No main author", "A One", "B Two"]);

    await user.click(screen.getByRole("option", { name: "B Two" }));
    expect(onChange).toHaveBeenCalledWith("B Two");
  });

  it("reports an empty string when 'No main author' is chosen, so the form clears it", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    const user = userEvent.setup();
    const { onChange } = renderField({ value: "A One" });

    await user.click(await screen.findByRole("combobox", { name: "Main author" }));
    await user.click(screen.getByRole("option", { name: "No main author" }));

    expect(onChange).toHaveBeenCalledWith("");
  });

  it("shows the chosen author", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    renderField({ value: "B Two" });

    expect(await screen.findByRole("combobox", { name: "Main author" })).toHaveTextContent("B Two");
  });

  it("shows no author as chosen when the stored value is not among the authors", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    renderField({ value: "Gone Author" });

    expect(await screen.findByRole("combobox", { name: "Main author" })).toHaveTextContent(
      "No main author",
    );
  });

  it("says the main author is also the only author in the folder name when the library setting is on", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: true,
    } as never);
    renderField();

    expect(await screen.findByText(/the only author in the folder name/i)).toBeInTheDocument();
  });

  it("does not mention the folder name when the library setting is off", async () => {
    vi.mocked(settingsApi.getLibrarySettings).mockResolvedValue({
      mainAuthorOnlyInPath: false,
    } as never);
    renderField();

    await screen.findByRole("combobox", { name: "Main author" });
    expect(screen.queryByText(/folder name/i)).not.toBeInTheDocument();
    expect(screen.getByText(/used when searching online sources/i)).toBeInTheDocument();
  });
});
