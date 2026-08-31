import { describe, it, expect, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useTargetCollision } from "./useTargetCollision";
import { audiobookApi } from "@/services/api";
import type { Audiobook } from "@/types/Audiobook";

vi.mock("@/services/api", () => ({
  audiobookApi: {
    checkTargetPath: vi.fn(),
  },
}));

describe("useTargetCollision", () => {
  const dummyBook: Audiobook = {
    bookName: "Test Book",
    authors: [{ name: "Author" }],
    narrators: [],
    genres: [],
    fileInfo: {
      fullPath: "/incoming/book.m4b",
      fileName: "book.m4b",
      sizeInBytes: 1000,
    },
  };

  it("proceeds directly when target does not exist", async () => {
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: false,
      targetPath: "/library/Author/Test Book.m4b",
    });

    const onProceed = vi.fn();
    const onReplaceExisting = vi.fn();

    const { result } = renderHook(() => useTargetCollision({ onReplaceExisting }));

    await act(async () => {
      await result.current.checkCollisionAndProceed(dummyBook, onProceed);
    });

    expect(onProceed).toHaveBeenCalledWith(dummyBook);
    expect(result.current.duplicateDialogOpen).toBe(false);
    expect(result.current.dialogProps).toBeNull();
  });

  it("proceeds directly when target path matches current file path (not a collision)", async () => {
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: true,
      targetPath: "/incoming/book.m4b",
    });

    const onProceed = vi.fn();
    const onReplaceExisting = vi.fn();

    const { result } = renderHook(() => useTargetCollision({ onReplaceExisting }));

    await act(async () => {
      await result.current.checkCollisionAndProceed(dummyBook, onProceed);
    });

    expect(onProceed).toHaveBeenCalledWith(dummyBook);
    expect(result.current.duplicateDialogOpen).toBe(false);
    expect(result.current.dialogProps).toBeNull();
  });

  it("opens duplicate dialog when target exists at a different path", async () => {
    vi.mocked(audiobookApi.checkTargetPath).mockResolvedValue({
      exists: true,
      targetPath: "/library/Author/Test Book.m4b",
      existing: {
        sizeInBytes: 2000,
        durationInSeconds: 300,
      },
    });

    const onProceed = vi.fn();
    const onReplaceExisting = vi.fn();
    const onDeleteNew = vi.fn();

    const { result } = renderHook(() => useTargetCollision({ onReplaceExisting, onDeleteNew }));

    await act(async () => {
      await result.current.checkCollisionAndProceed(dummyBook, onProceed);
    });

    expect(onProceed).not.toHaveBeenCalled();
    expect(result.current.duplicateDialogOpen).toBe(true);
    expect(result.current.dialogProps).not.toBeNull();
    expect(result.current.dialogProps?.targetPath).toBe("/library/Author/Test Book.m4b");

    // Test onReplaceExisting callback from dialogProps
    act(() => {
      result.current.dialogProps?.onReplaceExisting();
    });

    expect(onReplaceExisting).toHaveBeenCalledWith({
      ...dummyBook,
      replaceExisting: true,
    });
    expect(result.current.duplicateDialogOpen).toBe(false);
  });
});
