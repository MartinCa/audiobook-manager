import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { filesApi } from "@/services/api";
import { FolderDeleteContents } from "./FolderDeleteContents";

export interface DeleteFileDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  targetPath: string;
  onConfirmDelete: () => void | Promise<void>;
  title?: string;
  description?: string;
  confirmButtonText?: string;
  deletingText?: string;
}

export function DeleteFileDialog({
  open,
  onOpenChange,
  targetPath,
  onConfirmDelete,
  title = "Delete File",
  description,
  confirmButtonText = "Delete Permanently",
  deletingText = "Deleting...",
}: DeleteFileDialogProps) {
  const [deleting, setDeleting] = useState(false);

  const { data: directoryContents = [], isLoading: loadingContents } = useQuery({
    queryKey: ["directoryContents", targetPath],
    queryFn: () => filesApi.getDirectoryContents(targetPath),
    enabled: open && Boolean(targetPath),
  });

  const handleConfirm = async () => {
    setDeleting(true);
    try {
      await onConfirmDelete();
      onOpenChange(false);
    } finally {
      setDeleting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-lg sm:p-6">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-y-auto py-2 pr-1">
          <p className="text-muted-foreground text-sm">
            {description ??
              (directoryContents.length > 1
                ? "Are you sure you want to delete this file and its folder contents? This will permanently remove them from disk."
                : "Are you sure you want to delete this file? This will permanently remove it from disk.")}
          </p>

          <FolderDeleteContents
            targetPath={targetPath}
            files={directoryContents}
            isLoading={loadingContents}
          />
        </div>

        <div className="border-border flex flex-col-reverse justify-end gap-2 border-t pt-4 sm:flex-row">
          <Button
            variant="outline"
            className="w-full sm:w-auto"
            onClick={() => onOpenChange(false)}
            disabled={deleting}
          >
            Cancel
          </Button>
          <Button
            variant="destructive"
            className="w-full sm:w-auto"
            onClick={() => void handleConfirm()}
            disabled={deleting}
          >
            {deleting ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                {deletingText}
              </>
            ) : (
              confirmButtonText
            )}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export default DeleteFileDialog;
