import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2, FolderPlus, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { BookEditForm } from "./BookEditForm";
import { DuplicateTargetDialog } from "./DuplicateTargetDialog";
import { DeleteFileDialog } from "./DeleteFileDialog";
import { AudiobookFileDetails } from "./AudiobookFileDetails";
import { audiobookApi, filesApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { useTargetCollision } from "@/hooks/useTargetCollision";
import { toast } from "sonner";
import type { Audiobook } from "@/types/Audiobook";
import type { BookFileInfo } from "@/types/BookFileInfo";

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

  const { dialogProps, checkCollisionAndProceed } = useTargetCollision({
    onReplaceExisting: (book) => proceedOrganize(book),
    onDeleteNew: () => {
      void handleDeleteBook();
    },
  });

  const handleOrganizeClick = async (book: Audiobook) => {
    setOrganizing(true);
    try {
      await checkCollisionAndProceed(book, proceedOrganize);
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
    } finally {
      setOrganizing(false);
    }
  };

  const handleDeleteBook = async () => {
    try {
      await filesApi.deleteBook(targetPath);
      toast.success("File deleted successfully");
      onBookDeleted?.();
      onSuccess?.();
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
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

        <DeleteFileDialog
          open={deleteConfirmOpen}
          onOpenChange={setDeleteConfirmOpen}
          targetPath={targetPath}
          onConfirmDelete={handleDeleteBook}
          title="Delete Audiobook File"
          description="Are you sure you want to permanently delete this file and its folder contents? This will permanently remove them from disk."
        />
      </div>
    );
  }

  if (!bookDetails) {
    return null;
  }

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-4">
        <div className="space-y-6 lg:col-span-3">
          <BookEditForm
            initialBook={bookDetails}
            currentPath={targetPath}
            coverUrl={filesApi.getCoverUrl(targetPath)}
            onSave={handleOrganizeClick}
            defaultEmptyLanguage
            onDelete={() => setDeleteConfirmOpen(true)}
            deleteLabel="Delete File"
            submitLabel="Organize into Library"
            submitIcon={<FolderPlus className="mr-2 h-4 w-4" />}
            isSaving={organizing}
          />
        </div>

        <div className="space-y-6 lg:col-span-1">
          <AudiobookFileDetails
            filePath={targetPath}
            sizeInBytes={bookDetails.fileInfo?.sizeInBytes ?? file?.sizeInBytes}
            durationInSeconds={bookDetails.durationInSeconds}
          />
        </div>
      </div>

      {dialogProps && <DuplicateTargetDialog {...dialogProps} />}

      <DeleteFileDialog
        open={deleteConfirmOpen}
        onOpenChange={setDeleteConfirmOpen}
        targetPath={targetPath}
        onConfirmDelete={handleDeleteBook}
        title="Delete Audiobook File"
        description="Are you sure you want to permanently delete this file and its folder contents? This will permanently remove them from disk."
      />
    </div>
  );
}

export default BookOrganize;
