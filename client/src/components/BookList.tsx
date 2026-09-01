import React from "react";
import { BookOpen, Clock, HardDrive } from "lucide-react";
import { Card, CardContent } from "@/components/ui/card";
import { type Audiobook } from "@/types/domain";
import { formatDuration, formatFileSize } from "@/helpers/formatHelpers";

interface BookListProps {
  books: Audiobook[];
  onSelectBook: (book: Audiobook) => void;
}

export const BookList: React.FC<BookListProps> = ({ books, onSelectBook }) => {
  if (books.length === 0) {
    return (
      <div className="text-center py-12 text-muted-foreground text-sm">
        No audiobooks found in your library.
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {books.map((book) => (
        <Card
          key={book.id || book.fullPath}
          className="hover:border-primary transition-colors cursor-pointer flex flex-col justify-between"
          onClick={() => onSelectBook(book)}
        >
          <CardContent className="p-4 flex gap-4">
            {book.coverPath ? (
              <img
                src={`/api/image/cover?path=${encodeURIComponent(book.coverPath)}`}
                alt={book.bookName}
                className="w-20 h-28 object-cover rounded shrink-0"
              />
            ) : (
              <div className="w-20 h-28 bg-muted rounded shrink-0 flex items-center justify-center">
                <BookOpen className="h-8 w-8 text-muted-foreground" />
              </div>
            )}
            <div className="flex-1 min-w-0 flex flex-col justify-between">
              <div>
                <h3 className="font-semibold text-sm line-clamp-2">
                  {book.bookName}
                </h3>
                {book.authors && book.authors.length > 0 && (
                  <p className="text-xs text-muted-foreground mt-1 truncate">
                    {book.authors.join(", ")}
                  </p>
                )}
                {book.series && (
                  <p className="text-xs text-muted-foreground mt-0.5 truncate">
                    {book.series} {book.seriesPart && `#${book.seriesPart}`}
                  </p>
                )}
              </div>
              <div className="flex flex-wrap gap-2 text-xs text-muted-foreground mt-2">
                {book.durationInSeconds && (
                  <span className="flex items-center gap-1">
                    <Clock className="h-3 w-3" />
                    {formatDuration(book.durationInSeconds)}
                  </span>
                )}
                {book.fileSizeInBytes && (
                  <span className="flex items-center gap-1">
                    <HardDrive className="h-3 w-3" />
                    {formatFileSize(book.fileSizeInBytes)}
                  </span>
                )}
              </div>
            </div>
          </CardContent>
        </Card>
      ))}
    </div>
  );
};
export default BookList;
