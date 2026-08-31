import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { filesApi } from "@/services/api";
import { formatFileSize } from "@/helpers/formatHelpers";

export interface DeleteFileDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  targetPath: string;
  onConfirmDelete: () => void | Promise<void>;
  title?: string;
  description?: string;
}

export function DeleteFileDialog({
  open,
  onOpenChange,
  targetPath,
  onConfirmDelete,
  title = "Delete File",
  description,
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
      <DialogContent className="w-[calc(100vw-2rem)] p-4 sm:max-w-md sm:p-6">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          <p className="text-muted-foreground text-sm">
            {description ??
              (directoryContents.length > 1
                ? "Are you sure you want to delete this file and its folder contents? This will permanently remove them from disk."
                : "Are you sure you want to delete this file? This will permanently remove it from disk.")}
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
              {targetPath}
            </div>
          )}

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
                  Deleting...
                </>
              ) : (
                "Delete Permanently"
              )}
            </Button>
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
