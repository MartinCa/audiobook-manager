import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BookOrganize } from "./BookOrganize";

vi.mock("@/services/api", () => ({
  audiobookApi: {
    parseBookDetails: vi.fn(),
    organizeBook: vi.fn(),
    checkTargetPath: vi.fn(),
    generateNewPath: vi.fn().mockResolvedValue("Author/Book/Book.m4b"),
  },
  filesApi: {
    getCoverUrl: vi.fn((path: string) => `/api/files/cover?path=${encodeURIComponent(path)}`),
    getDirectoryContents: vi.fn().mockResolvedValue([]),
    deleteBook: vi.fn(),
  },
  settingsApi: {
    getLanguages: vi.fn().mockResolvedValue({ languages: [] }),
  },
  similarValuesApi: {
    getAutocomplete: vi.fn().mockResolvedValue([]),
  },
}));

import { audiobookApi, filesApi } from "@/services/api";

describe("BookOrganize", () => {
  let queryClient: QueryClient;

  const sampleBookDetails = {
    authors: [{ name: "Isaac Asimov" }],
    narrators: [],
    bookName: "Foundation",
    subtitle: undefined,
    series: "Foundation",
    seriesPart: "1",
    year: 1951,
    genres: ["Sci-Fi"],
    description: "Galactic Empire story",
    copyright: undefined,
    publisher: "Gnome Press",
    language: "eng",
    rating: undefined,
    asin: undefined,
    www: undefined,
    fileInfo: {
      fullPath: "/import/Foundation.m4b",
      fileName: "Foundation.m4b",
      sizeInBytes: 500000000,
    },
    durationInSeconds: 36000,
  };

  beforeEach(() => {
    vi.clearAllMocks();
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });

    vi.mocked(audiobookApi.parseBookDetails).mockResolvedValue(sampleBookDetails);
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: false,
      targetPath: "/library/Isaac Asimov/Foundation.m4b",
    });
    vi.mocked(audiobookApi.organizeBook).mockResolvedValue("queue-123");
    vi.mocked(filesApi.deleteBook).mockResolvedValue();
  });

  const renderComponent = (props = {}) =>
    render(
      <QueryClientProvider client={queryClient}>
        <BookOrganize bookPath="/import/Foundation.m4b" {...props} />
      </QueryClientProvider>,
    );

  it("loads and displays parsed audiobook details in form", async () => {
    renderComponent();

    const titleInput = await screen.findByPlaceholderText("Book title");
    expect(titleInput).toHaveValue("Foundation");
    expect(screen.getByText("Isaac Asimov")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("YYYY")).toHaveValue(1951);
  });

  it("submits organize request and triggers onSuccess callback", async () => {
    const onSuccess = vi.fn();
    renderComponent({ onSuccess });

    const submitBtn = await screen.findByRole("button", { name: /organize into library/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(audiobookApi.checkTargetPath).toHaveBeenCalled();
      expect(audiobookApi.organizeBook).toHaveBeenCalled();
      expect(onSuccess).toHaveBeenCalled();
    });
  });

  it("handles duplicate target collision and deletes new file without second confirmation dialog", async () => {
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: true,
      targetPath: "/library/Isaac Asimov/Foundation.m4b",
      existing: {
        audiobookId: 10,
        sizeInBytes: 500000000,
        durationInSeconds: 36000,
      },
    });

    renderComponent();

    const submitBtn = await screen.findByRole("button", { name: /organize into library/i });
    fireEvent.click(submitBtn);

    // Duplicate target dialog opens
    expect(await screen.findByText("Duplicate file at target location")).toBeInTheDocument();

    // Click "Delete new file" inside DuplicateTargetDialog
    const deleteNewBtn = screen.getByRole("button", { name: /delete new file/i });
    fireEvent.click(deleteNewBtn);

    // Confirmation view in DuplicateTargetDialog
    expect(screen.getByText("Confirm Deletion of New File")).toBeInTheDocument();

    const confirmDeleteBtn = screen.getByRole("button", { name: "Confirm Delete" });
    fireEvent.click(confirmDeleteBtn);

    await waitFor(() => {
      expect(filesApi.deleteBook).toHaveBeenCalledWith("/import/Foundation.m4b");
    });
  });

  // Regression: the organize form used to pass the on-disk cover URL unconditionally, so every
  // file without embedded cover art issued a guaranteed-404 request to /api/files/cover after the
  // parse response already showed there was no cover to fetch. The parsed response IS the source
  // of truth - no separate request is needed.
  it("does not request the on-disk cover URL when the parsed file has no cover", async () => {
    renderComponent();

    await screen.findByPlaceholderText("Book title");

    expect(filesApi.getCoverUrl).not.toHaveBeenCalled();
    // The cover editor shows the no-cover placeholder instead of a pending image request.
    expect(await screen.findByText("Click to set cover")).toBeInTheDocument();
  });

  it("wires the on-disk cover URL only when the parsed file carries cover data", async () => {
    vi.mocked(audiobookApi.parseBookDetails).mockResolvedValue({
      ...sampleBookDetails,
      cover: { base64Data: "aGVsbG8=", mimeType: "image/jpeg" },
    });

    renderComponent();

    await screen.findByRole("button", { name: /change cover/i });
    expect(filesApi.getCoverUrl).toHaveBeenCalledWith("/import/Foundation.m4b");
  });
});
