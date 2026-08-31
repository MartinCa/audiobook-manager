import { useState } from "react";
import { audiobookApi } from "@/services/api";
import { pathsEqual } from "@/helpers/pathHelpers";
import type { Audiobook } from "@/types/Audiobook";
import type { TargetPathCheckResult } from "@/types/TargetPathCheck";

export interface UseTargetCollisionOptions {
  onReplaceExisting: (book: Audiobook) => void | Promise<void>;
  onDeleteNew?: (book: Audiobook) => void | Promise<void>;
}

export function useTargetCollision({ onReplaceExisting, onDeleteNew }: UseTargetCollisionOptions) {
  const [duplicateDialogOpen, setDuplicateDialogOpen] = useState(false);
  const [duplicateCheck, setDuplicateCheck] = useState<TargetPathCheckResult | null>(null);
  const [pendingBook, setPendingBook] = useState<Audiobook | null>(null);

  const checkCollisionAndProceed = async (
    book: Audiobook,
    onProceed: (book: Audiobook) => void | Promise<void>,
  ) => {
    try {
      const check = await audiobookApi.checkTargetPath(book);
      // If target exists and is NOT the same as the book's current file path, it's a collision
      if (check.exists && !pathsEqual(check.targetPath, book.fileInfo?.fullPath)) {
        setDuplicateCheck(check);
        setPendingBook(book);
        setDuplicateDialogOpen(true);
        return;
      }
    } catch {
      // If check fails, fall through and let caller proceed
    }

    await onProceed(book);
  };

  const dialogProps =
    duplicateCheck && pendingBook
      ? {
          open: duplicateDialogOpen,
          onOpenChange: setDuplicateDialogOpen,
          newPath: pendingBook.fileInfo?.fullPath ?? "",
          newSizeInBytes: pendingBook.fileInfo?.sizeInBytes ?? 0,
          newDurationInSeconds: pendingBook.durationInSeconds,
          targetPath: duplicateCheck.targetPath,
          existingSizeInBytes: duplicateCheck.existing?.sizeInBytes,
          existingDurationInSeconds: duplicateCheck.existing?.durationInSeconds ?? undefined,
          onReplaceExisting: () => {
            setDuplicateDialogOpen(false);
            void onReplaceExisting({
              ...pendingBook,
              replaceExisting: true,
            });
          },
          onDeleteNew: onDeleteNew
            ? () => {
                setDuplicateDialogOpen(false);
                void onDeleteNew(pendingBook);
              }
            : undefined,
        }
      : null;

  return {
    duplicateDialogOpen,
    setDuplicateDialogOpen,
    duplicateCheck,
    pendingBook,
    checkCollisionAndProceed,
    dialogProps,
  };
}
