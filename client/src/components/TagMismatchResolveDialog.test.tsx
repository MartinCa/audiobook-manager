import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TagMismatchResolveDialog } from "./TagMismatchResolveDialog";
import { consistencyApi } from "@/services/api";
import type { ConsistencyIssue } from "@/types/ConsistencyIssue";

const issue: ConsistencyIssue = {
  id: 42,
  audiobookId: 7,
  bookName: "The Test Book",
  authors: ["Some Author"],
  issueType: "TagMismatch",
  description: 'Tag mismatch: "bookName" differs',
  detectedAt: "2026-09-01T10:00:00Z",
  expectedValue: null,
  actualValue: null,
};

let queryClient: QueryClient;

function renderWithQuery(ui: React.ReactElement) {
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe("TagMismatchResolveDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
  });

  it("shows loading placeholder, then field rows with library/file values", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "bookName", libraryValue: "The Test Book", fileValue: "The Test Book 2" },
      { field: "year", libraryValue: "2020", fileValue: "2021" },
    ]);

    renderWithQuery(
      <TagMismatchResolveDialog
        open
        onOpenChange={() => {}}
        issue={issue}
        onResolve={() => Promise.resolve()}
      />,
    );

    expect(await screen.findByText("Resolve Tag Mismatch")).toBeInTheDocument();
    expect(await screen.findByText("The Test Book 2")).toBeInTheDocument();
    expect(screen.getByText("2020")).toBeInTheDocument();
    expect(screen.getByText("2021")).toBeInTheDocument();
  });

  it("submits library values by default when Apply Choices is clicked", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "bookName", libraryValue: "The Test Book", fileValue: "The Test Book 2" },
    ]);
    const onResolve = vi.fn().mockResolvedValue(undefined);

    renderWithQuery(
      <TagMismatchResolveDialog open onOpenChange={() => {}} issue={issue} onResolve={onResolve} />,
    );

    // Wait for fields to load, then click Apply (all rows default to Library).
    await screen.findByText("The Test Book 2");
    fireEvent.click(screen.getByRole("button", { name: "Apply Choices" }));

    await waitFor(() => {
      expect(onResolve).toHaveBeenCalledWith(42, { bookName: "The Test Book" });
    });
  });

  it("submits file value when the File radio is selected", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "bookName", libraryValue: "The Test Book", fileValue: "The Test Book 2" },
    ]);
    const onResolve = vi.fn().mockResolvedValue(undefined);

    renderWithQuery(
      <TagMismatchResolveDialog open onOpenChange={() => {}} issue={issue} onResolve={onResolve} />,
    );

    const fileRadio = await screen.findByRole("radio", { name: "The Test Book 2" });
    fireEvent.click(fileRadio);

    fireEvent.click(screen.getByRole("button", { name: "Apply Choices" }));

    await waitFor(() => {
      expect(onResolve).toHaveBeenCalledWith(42, { bookName: "The Test Book 2" });
    });
  });

  it("submits null when Keep Neither is selected", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "description", libraryValue: "Library description", fileValue: "File description" },
    ]);
    const onResolve = vi.fn().mockResolvedValue(undefined);

    renderWithQuery(
      <TagMismatchResolveDialog open onOpenChange={() => {}} issue={issue} onResolve={onResolve} />,
    );

    const emptyRadio = await screen.findByRole("radio", { name: "Clear description" });
    fireEvent.click(emptyRadio);

    fireEvent.click(screen.getByRole("button", { name: "Apply Choices" }));

    await waitFor(() => {
      expect(onResolve).toHaveBeenCalledWith(42, { description: null });
    });
  });

  it("does not offer Keep Neither for structural fields", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "Book Name", libraryValue: "Library Title", fileValue: "File Title" },
      { field: "Year", libraryValue: "2010", fileValue: "2020" },
      { field: "Description", libraryValue: "desc", fileValue: "other" },
    ]);

    renderWithQuery(
      <TagMismatchResolveDialog
        open
        onOpenChange={() => {}}
        issue={issue}
        onResolve={() => Promise.resolve()}
      />,
    );

    await screen.findByText("File Title");
    // Structural fields (Book Name, Year) expose no "Clear" radio; Description does.
    expect(screen.queryByRole("radio", { name: "Clear Book Name" })).not.toBeInTheDocument();
    expect(screen.queryByRole("radio", { name: "Clear Year" })).not.toBeInTheDocument();
    expect(screen.getByRole("radio", { name: "Clear Description" })).toBeInTheDocument();
  });

  it("stays open and keeps selections when resolve fails", async () => {
    vi.spyOn(consistencyApi, "getTagMismatch").mockResolvedValue([
      { field: "bookName", libraryValue: "Library Title", fileValue: "File Title" },
    ]);
    const onOpenChange = vi.fn();
    const onResolve = vi.fn().mockRejectedValue(new Error("boom"));

    renderWithQuery(
      <TagMismatchResolveDialog
        open
        onOpenChange={onOpenChange}
        issue={issue}
        onResolve={onResolve}
      />,
    );

    await screen.findByText("File Title");
    const fileRadio = screen.getByRole("radio", { name: "File Title" });
    fireEvent.click(fileRadio);
    fireEvent.click(screen.getByRole("button", { name: "Apply Choices" }));

    await waitFor(() => expect(onResolve).toHaveBeenCalled());
    expect(onOpenChange).not.toHaveBeenCalled();
    // The dialog content is still visible.
    expect(screen.getByText("Resolve Tag Mismatch")).toBeInTheDocument();
  });
});
