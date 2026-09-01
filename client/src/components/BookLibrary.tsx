import React, { useState, useEffect } from "react";
import { BookOpen, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api, handleApiError } from "@/lib/api";
import { type Audiobook } from "@/types/domain";
import BookList from "./BookList";
import LibrarySearch from "./LibrarySearch";
import TagPreviewDialog from "./TagPreviewDialog";
import OperationProgressBar from "./OperationProgressBar";
import { toast } from "sonner";

export const BookLibrary: React.FC = () => {
  const [books, setBooks] = useState<Audiobook[]>([]);
  const [loading, setLoading] = useState(true);
  const [selectedBook, setSelectedBook] = useState<Audiobook | null>(null);
  const [scanning, setScanning] = useState(false);
  const [scanProgress] = useState<{ processed: number; total: number }>({
    processed: 0,
    total: 0,
  });

  const fetchLibrary = async (query?: string) => {
    setLoading(true);
    try {
      const endpoint = query ? "/library/search" : "/library/audiobooks";
      const params = query ? { query } : undefined;
      const res = await api.get<Audiobook[]>(endpoint, { params });
      setBooks(res.data || []);
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLibrary();
  }, []);

  const handleScanLibrary = async () => {
    setScanning(true);
    try {
      await api.post("/library/scan");
      toast.info("Library scan started");
    } catch (err) {
      toast.error(handleApiError(err).message);
      setScanning(false);
    }
  };

  const handleDeleteBook = async (book: Audiobook) => {
    try {
      await api.delete(`/library/audiobook/${book.id}`);
      toast.success("Audiobook deleted from library");
      setSelectedBook(null);
      setBooks((prev) => prev.filter((b) => b.id !== book.id));
    } catch (err) {
      toast.error(handleApiError(err).message);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center flex-wrap gap-4">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <BookOpen className="h-6 w-6 text-primary" />
            Audiobook Library
          </h1>
          <p className="text-sm text-muted-foreground">
            Browse, search, and manage your organized audiobook library.
          </p>
        </div>
        <Button
          onClick={handleScanLibrary}
          disabled={scanning}
        >
          <RefreshCw
            className={`h-4 w-4 mr-2 ${scanning ? "animate-spin" : ""}`}
          />
          {scanning ? "Scanning Library..." : "Scan Library"}
        </Button>
      </div>

      {scanning && (
        <OperationProgressBar
          processed={scanProgress.processed}
          total={scanProgress.total}
          label="Scanning Library..."
        />
      )}

      <LibrarySearch onSearch={(q) => fetchLibrary(q)} />

      {loading ? (
        <div className="text-center py-12 text-muted-foreground text-sm">
          Loading library audiobooks...
        </div>
      ) : (
        <BookList
          books={books}
          onSelectBook={(b) => setSelectedBook(b)}
        />
      )}

      <TagPreviewDialog
        book={selectedBook}
        open={!!selectedBook}
        onOpenChange={(open) => !open && setSelectedBook(null)}
        onDelete={handleDeleteBook}
      />
    </div>
  );
};
export default BookLibrary;
