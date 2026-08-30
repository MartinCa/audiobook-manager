import React from "react";
import { BookOpen, Folder, Trash2 } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Audiobook } from "@/types/domain";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";

interface TagPreviewDialogProps {
  book: Audiobook | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onDelete?: (book: Audiobook) => void;
}

export const TagPreviewDialog: React.FC<TagPreviewDialogProps> = ({
  book,
  open,
  onOpenChange,
  onDelete,
}) => {
  if (!book) return null;

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
    >
      <DialogContent className="max-w-2xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle className="text-xl font-bold flex items-center gap-2">
            <BookOpen className="h-5 w-5 text-primary" />
            {book.bookName}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-6">
          <div className="flex gap-6">
            {book.coverPath ? (
              <img
                src={`/api/image/cover?path=${encodeURIComponent(book.coverPath)}`}
                alt={book.bookName}
                className="w-32 h-44 object-cover rounded shadow"
              />
            ) : (
              <div className="w-32 h-44 bg-muted rounded flex items-center justify-center">
                <BookOpen className="h-10 w-10 text-muted-foreground" />
              </div>
            )}

            <div className="space-y-2 text-sm flex-1">
              <div>
                <span className="text-xs text-muted-foreground font-semibold uppercase block">
                  Authors
                </span>
                <span>{book.authors?.join(", ") || "Unknown"}</span>
              </div>

              {book.narrators && book.narrators.length > 0 && (
                <div>
                  <span className="text-xs text-muted-foreground font-semibold uppercase block">
                    Narrators
                  </span>
                  <span>{book.narrators.join(", ")}</span>
                </div>
              )}

              {book.series && (
                <div>
                  <span className="text-xs text-muted-foreground font-semibold uppercase block">
                    Series
                  </span>
                  <span>
                    {book.series} {book.seriesPart && `#${book.seriesPart}`}
                  </span>
                </div>
              )}

              <div className="flex gap-4">
                {book.year && (
                  <div>
                    <span className="text-xs text-muted-foreground font-semibold uppercase block">
                      Year
                    </span>
                    <span>{book.year}</span>
                  </div>
                )}
                {book.language && (
                  <div>
                    <span className="text-xs text-muted-foreground font-semibold uppercase block">
                      Language
                    </span>
                    <span>{book.language}</span>
                  </div>
                )}
              </div>
            </div>
          </div>

          {book.description && (
            <div>
              <span className="text-xs text-muted-foreground font-semibold uppercase block mb-1">
                Description
              </span>
              <p className="text-sm bg-muted/40 p-3 rounded-md whitespace-pre-wrap leading-relaxed">
                {book.description}
              </p>
            </div>
          )}

          {book.genres && book.genres.length > 0 && (
            <div>
              <span className="text-xs text-muted-foreground font-semibold uppercase block mb-2">
                Genres
              </span>
              <div className="flex flex-wrap gap-1.5">
                {book.genres.map((g) => (
                  <Badge
                    key={g}
                    variant="secondary"
                  >
                    {g}
                  </Badge>
                ))}
              </div>
            </div>
          )}

          <div className="text-xs text-muted-foreground space-y-1 pt-4 border-t border-border">
            <div className="flex items-center gap-2">
              <Folder className="h-3.5 w-3.5" />
              <span className="truncate">{book.fullPath}</span>
            </div>
            <div className="flex gap-4 pt-1">
              <span>Duration: {formatDuration(book.durationInSeconds)}</span>
              <span>Size: {formatFileSize(book.fileSizeInBytes)}</span>
            </div>
          </div>

          {onDelete && (
            <div className="flex justify-end pt-2">
              <Button
                variant="destructive"
                size="sm"
                onClick={() => onDelete(book)}
              >
                <Trash2 className="h-4 w-4 mr-2" />
                Delete Audiobook
              </Button>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
};
export default TagPreviewDialog;
