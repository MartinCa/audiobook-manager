import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";

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
  onDeleteNew: () => void;
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
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Duplicate file at target location</DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          <p className="text-muted-foreground text-sm">
            A file already exists where this book would be organized to. Choose which copy to keep:
          </p>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="border-border bg-muted/40 rounded-lg border p-4">
              <div className="text-foreground text-sm font-semibold">New file (Source)</div>
              <div className="text-muted-foreground mt-1 text-xs break-all" title={newPath}>
                {newPath}
              </div>
              <div className="mt-2 text-xs font-medium">Size: {formatFileSize(newSizeInBytes)}</div>
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
            <Button
              variant="destructive"
              onClick={() => {
                onDeleteNew();
                onOpenChange(false);
              }}
            >
              Delete new file
            </Button>
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
      </DialogContent>
    </Dialog>
  );
}

export default DuplicateTargetDialog;
