import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Folder, Loader2 } from "lucide-react";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { filesApi } from "@/services/api";
import { formatFileSize } from "@/helpers/formatHelpers";
import { getTotalSizeInBytes } from "@/helpers/folderHelpers";
import { FolderDeleteContents } from "./FolderDeleteContents";

export interface DirectoryItem {
  id?: number | string;
  directoryPath: string;
}

export interface BulkDeleteDirectoriesDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  directories: DirectoryItem[];
  onConfirmDelete: () => void | Promise<void>;
  title?: string;
  description?: string | React.ReactNode;
  confirmButtonText?: string;
  deletingText?: string;
}

function BulkDirectoryItem({ directoryPath }: { directoryPath: string }) {
  const { data: files = [], isLoading } = useQuery({
    queryKey: ["directoryContents", directoryPath],
    queryFn: () => filesApi.getDirectoryContents(directoryPath),
  });

  const totalSize = getTotalSizeInBytes(files);

  return (
    <AccordionItem
      value={directoryPath}
      className="border-border bg-muted/20 rounded-md border px-3"
    >
      <AccordionTrigger className="w-full min-w-0 py-2.5 text-left hover:no-underline">
        <div className="flex min-w-0 flex-1 items-start justify-between gap-3 pr-2">
          <div className="flex min-w-0 items-start gap-2">
            <Folder className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
            <span
              className="text-foreground text-left font-mono text-xs break-all"
              title={directoryPath}
            >
              {directoryPath}
            </span>
          </div>
          <div className="text-muted-foreground shrink-0 pt-0.5 font-mono text-[11px] whitespace-nowrap">
            {isLoading ? (
              <Loader2 className="mr-1 inline h-3 w-3 animate-spin" />
            ) : (
              `${files.length} ${files.length === 1 ? "file" : "files"} (${formatFileSize(totalSize)})`
            )}
          </div>
        </div>
      </AccordionTrigger>
      <AccordionContent className="border-border border-t pt-3 pb-3">
        <FolderDeleteContents
          targetPath={directoryPath}
          files={files}
          isLoading={isLoading}
          showFolderPath={false}
        />
      </AccordionContent>
    </AccordionItem>
  );
}

export function BulkDeleteDirectoriesDialog({
  open,
  onOpenChange,
  directories,
  onConfirmDelete,
  title = "Delete Orphaned Directories",
  description,
  confirmButtonText = "Delete All",
  deletingText = "Deleting...",
}: BulkDeleteDirectoriesDialogProps) {
  const [deleting, setDeleting] = useState(false);

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
      <DialogContent className="flex max-h-[85vh] w-[calc(100vw-2rem)] flex-col overflow-hidden p-4 sm:max-w-2xl sm:p-6">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <div className="flex-1 space-y-4 overflow-x-hidden overflow-y-auto py-2 pr-1">
          <p className="text-muted-foreground text-sm">
            {description ??
              `This will permanently delete all ${directories.length} directories and their contained files from disk.`}
          </p>

          <Accordion type="multiple" className="space-y-2">
            {directories.map((dir) => (
              <BulkDirectoryItem
                key={dir.id ? String(dir.id) : dir.directoryPath}
                directoryPath={dir.directoryPath}
              />
            ))}
          </Accordion>
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

export default BulkDeleteDirectoriesDialog;
