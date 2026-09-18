import { useQuery } from "@tanstack/react-query";
import { ConfirmDialog } from "@/components/ConfirmDialog";
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
  const { data: directoryContents = [], isLoading: loadingContents } = useQuery({
    queryKey: ["directoryContents", targetPath],
    queryFn: () => filesApi.getDirectoryContents(targetPath),
    enabled: open && Boolean(targetPath),
  });

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      onConfirm={onConfirmDelete}
      confirmVariant="destructive"
      confirmText={confirmButtonText}
      pendingText={deletingText}
      contentClassName="max-h-[85vh] sm:max-w-lg"
    >
      <div className="space-y-4 overflow-x-hidden pr-1">
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
    </ConfirmDialog>
  );
}

export default DeleteFileDialog;
