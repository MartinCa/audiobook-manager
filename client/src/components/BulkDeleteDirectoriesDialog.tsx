import { useQuery } from "@tanstack/react-query";
import { Folder, Loader2 } from "lucide-react";
import { ConfirmDialog } from "@/components/ConfirmDialog";
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
  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      onConfirm={onConfirmDelete}
      confirmVariant="destructive"
      confirmText={confirmButtonText}
      pendingText={deletingText}
      contentClassName="max-h-[85vh] sm:max-w-2xl"
    >
      <div className="space-y-4 overflow-x-hidden pr-1">
        <p className="text-muted-foreground text-sm">
          {description ??
            `This will permanently delete all ${directories.length} directories and their contained files from disk.`}
        </p>

        <Accordion multiple className="space-y-2">
          {directories.map((dir) => (
            <BulkDirectoryItem
              key={dir.id ? String(dir.id) : dir.directoryPath}
              directoryPath={dir.directoryPath}
            />
          ))}
        </Accordion>
      </div>
    </ConfirmDialog>
  );
}

export default BulkDeleteDirectoriesDialog;
