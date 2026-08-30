import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, FolderPlus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { BookEditForm } from "./BookEditForm";
import { DuplicateTargetDialog } from "./DuplicateTargetDialog";
import { audiobookApi, filesApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";
import type { Audiobook } from "@/types/Audiobook";
import type { BookFileInfo } from "@/types/BookFileInfo";
import type { TargetPathCheckResult } from "@/types/TargetPathCheck";

interface BookOrganizeProps {
  file?: BookFileInfo;
  bookPath?: string;
  onSuccess?: () => void;
  onBookQueued?: (queueId: string) => void;
  onBookDeleted?: () => void;
}

export function BookOrganize({
  file,
  bookPath,
  onSuccess,
  onBookQueued,
  onBookDeleted,
}: BookOrganizeProps) {
  const targetPath = file?.fullPath ?? bookPath ?? "";
  const [organizing, setOrganizing] = useState(false);

  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const [duplicateCheck, setDuplicateCheck] = useState<TargetPathCheckResult | null>(null);
  const [duplicateDialogOpen, setDuplicateDialogOpen] = useState(false);
  const [pendingBook, setPendingBook] = useState<Audiobook | null>(null);

  const {
    data: bookDetails = null,
    isLoading: loading,
    error: parseError,
  } = useQuery({
    queryKey: ["bookDetails", targetPath],
    queryFn: () => audiobookApi.parseBookDetails(targetPath),
    enabled: Boolean(targetPath),
  });

  const error = parseError ? handleApiError(parseError).message : null;

  const proceedOrganize = async (book: Audiobook) => {
    setOrganizing(true);
    try {
      const queueId = await audiobookApi.organizeBook(book);
      toast.success("Book added to organization queue");
      onBookQueued?.(queueId || targetPath);
      onSuccess?.();
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setOrganizing(false);
    }
  };

  const handleOrganizeClick = async (book: Audiobook) => {
    setOrganizing(true);
    try {
      const check = await audiobookApi.checkTargetPath(book);
      if (check.exists) {
        setDuplicateCheck(check);
        setPendingBook(book);
        setDuplicateDialogOpen(true);
        return;
      }
      await proceedOrganize(book);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setOrganizing(false);
    }
  };

  const handleDeleteBook = async () => {
    setDeleting(true);
    try {
      await filesApi.deleteBook(targetPath);
      toast.success("File deleted successfully");
      setDeleteConfirmOpen(false);
      onBookDeleted?.();
      onSuccess?.();
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setDeleting(false);
    }
  };

  if (loading) {
    return (
      <div className="text-muted-foreground flex items-center justify-center py-8 text-sm">
        <Loader2 className="text-primary mr-2 h-4 w-4 animate-spin" />
        Reading metadata and audio tags...
      </div>
    );
  }

  if (error && !bookDetails) {
    return (
      <div className="space-y-3 py-4 text-center">
        <div className="text-destructive text-xs">Failed to parse file: {error}</div>
        <Button variant="outline" size="sm" onClick={() => setDeleteConfirmOpen(true)}>
          <Trash2 className="text-destructive mr-1.5 h-3.5 w-3.5" />
          Delete Corrupted File
        </Button>
      </div>
    );
  }

  if (!bookDetails) {
    return null;
  }

  return (
    <div className="space-y-4">
      <BookEditForm
        initialBook={bookDetails}
        currentPath={targetPath}
        onSave={handleOrganizeClick}
        formActions={
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setDeleteConfirmOpen(true)}
              className="text-destructive hover:bg-destructive/10"
            >
              <Trash2 className="mr-1.5 h-3.5 w-3.5" />
              Delete File
            </Button>

            <Button type="submit" size="sm" disabled={organizing}>
              {organizing ? (
                <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />
              ) : (
                <FolderPlus className="mr-1.5 h-3.5 w-3.5" />
              )}
              Organize into Library
            </Button>
          </div>
        }
      />

      {duplicateCheck && pendingBook && (
        <DuplicateTargetDialog
          open={duplicateDialogOpen}
          onOpenChange={setDuplicateDialogOpen}
          newPath={pendingBook.fileInfo?.fullPath ?? targetPath}
          newSizeInBytes={pendingBook.fileInfo?.sizeInBytes ?? 0}
          newDurationInSeconds={pendingBook.durationInSeconds}
          targetPath={duplicateCheck.targetPath}
          existingSizeInBytes={duplicateCheck.existing?.sizeInBytes}
          existingDurationInSeconds={duplicateCheck.existing?.durationInSeconds}
          onReplaceExisting={() => {
            void (async () => {
              await proceedOrganize(pendingBook);
              setDuplicateDialogOpen(false);
            })();
          }}
          onDeleteNew={() => {
            void (async () => {
              await handleDeleteBook();
              setDuplicateDialogOpen(false);
            })();
          }}
        />
      )}

      <Dialog open={deleteConfirmOpen} onOpenChange={setDeleteConfirmOpen}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Delete Audiobook File</DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-xs">
              Are you sure you want to permanently delete this unorganized file?
            </p>
            <div className="bg-muted text-muted-foreground rounded p-2 font-mono text-xs break-all">
              {targetPath}
            </div>
            <div className="border-border flex justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setDeleteConfirmOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="destructive"
                disabled={deleting}
                onClick={() => {
                  void handleDeleteBook();
                }}
              >
                {deleting ? "Deleting..." : "Delete File"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default BookOrganize;
