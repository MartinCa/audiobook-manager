import { useState } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";
import { filesApi } from "@/services/api";
import { Loader2 } from "lucide-react";
import type BookFileInfo from "@/types/BookFileInfo";

interface DuplicateTargetDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  newPath: string;
  newSizeInBytes: number;
  newDurationInSeconds?: number;
  targetPath: string;
  existingSizeInBytes?: number;
  existingDurationInSeconds?: number;
  onReplaceExisting: () => void;
  onDeleteNew?: () => void;
}

export function DuplicateTargetDialog({
  open,
  onOpenChange,
  newPath,
  newSizeInBytes,
  newDurationInSeconds,
  targetPath,
  existingSizeInBytes,
  existingDurationInSeconds,
  onReplaceExisting,
  onDeleteNew,
}: DuplicateTargetDialogProps) {
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [directoryContents, setDirectoryContents] = useState<BookFileInfo[]>([]);
  const [loadingContents, setLoadingContents] = useState(false);

  const handleOpenChange = (nextOpen: boolean) => {
    if (!nextOpen) {
      setConfirmDelete(false);
      setDirectoryContents([]);
      setLoadingContents(false);
    }
    onOpenChange(nextOpen);
  };

  const handleStartDelete = async () => {
    setConfirmDelete(true);
    setLoadingContents(true);
    try {
      const contents = await filesApi.getDirectoryContents(newPath);
      setDirectoryContents(contents);
    } catch {
      setDirectoryContents([]);
    } finally {
      setLoadingContents(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>
            {confirmDelete ? "Confirm Deletion of New File" : "Duplicate file at target location"}
          </DialogTitle>
        </DialogHeader>

        {confirmDelete ? (
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-sm">
              Are you sure you want to permanently delete the new file
              {directoryContents.length > 1 ? " and its folder contents" : ""}? This cannot be
              undone.
            </p>

            {loadingContents ? (
              <div className="text-muted-foreground flex items-center gap-2 text-xs">
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
                Listing folder contents...
              </div>
            ) : directoryContents.length > 0 ? (
              <ul className="max-h-48 space-y-1 overflow-y-auto">
                {directoryContents.map((f) => (
                  <li
                    key={f.fullPath}
                    className="bg-muted flex items-center justify-between gap-2 rounded p-2 text-xs"
                  >
                    <span className="text-foreground truncate font-mono">{f.fileName}</span>
                    <span className="text-muted-foreground shrink-0">
                      {formatFileSize(f.sizeInBytes)}
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <div className="bg-muted text-muted-foreground rounded p-2 font-mono text-xs break-all">
                {newPath}
              </div>
            )}

            <div className="border-border flex flex-wrap items-center justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => setConfirmDelete(false)}>
                Back
              </Button>
              <Button
                variant="destructive"
                onClick={() => {
                  onDeleteNew?.();
                  onOpenChange(false);
                }}
              >
                Confirm Delete
              </Button>
            </div>
          </div>
        ) : (
          <div className="space-y-4 py-2">
            <p className="text-muted-foreground text-sm">
              A file already exists where this book would be organized to. Choose which copy to
              keep:
            </p>

            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div className="border-border bg-muted/40 rounded-lg border p-4">
                <div className="text-foreground text-sm font-semibold">New file (Source)</div>
                <div className="text-muted-foreground mt-1 text-xs break-all" title={newPath}>
                  {newPath}
                </div>
                <div className="mt-2 text-xs font-medium">
                  Size: {formatFileSize(newSizeInBytes)}
                </div>
                {newDurationInSeconds != null && (
                  <div className="text-muted-foreground text-xs">
                    Duration: {formatDuration(newDurationInSeconds)}
                  </div>
                )}
              </div>

              <div className="border-border bg-muted/40 rounded-lg border p-4">
                <div className="text-foreground text-sm font-semibold">Existing file (Target)</div>
                <div className="text-muted-foreground mt-1 text-xs break-all" title={targetPath}>
                  {targetPath}
                </div>
                {existingSizeInBytes != null && (
                  <div className="mt-2 text-xs font-medium">
                    Size: {formatFileSize(existingSizeInBytes)}
                  </div>
                )}
                {existingDurationInSeconds != null && (
                  <div className="text-muted-foreground text-xs">
                    Duration: {formatDuration(existingDurationInSeconds)}
                  </div>
                )}
              </div>
            </div>

            <div className="border-border flex flex-wrap items-center justify-end gap-2 border-t pt-4">
              <Button variant="outline" onClick={() => onOpenChange(false)}>
                Cancel
              </Button>
              {onDeleteNew && (
                <Button
                  variant="destructive"
                  onClick={() => {
                    void handleStartDelete();
                  }}
                >
                  Delete new file
                </Button>
              )}
              <Button
                variant="default"
                onClick={() => {
                  onReplaceExisting();
                  onOpenChange(false);
                }}
              >
                Replace existing
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

export default DuplicateTargetDialog;
