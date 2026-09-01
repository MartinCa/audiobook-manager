import React, { useState, useEffect } from "react";
import { FolderInput, RefreshCw, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { api, handleApiError } from "@/lib/api";
import { type DiscoveredAudiobook } from "@/types/domain";
import BookEditForm from "./BookEditForm";
import { toast } from "sonner";

export const BookOrganize: React.FC = () => {
  const [books, setBooks] = useState<DiscoveredAudiobook[]>([]);
  const [languages, setLanguages] = useState<{ code: string; name: string }[]>(
    [],
  );
  const [loading, setLoading] = useState(true);
  const [expandedPath, setExpandedPath] = useState<string | undefined>(
    undefined,
  );

  const fetchUnorganized = async () => {
    setLoading(true);
    try {
      const [queueRes, langRes] = await Promise.all([
        api.get<DiscoveredAudiobook[]>("/queue/audiobooks"),
        api.get<{ code: string; name: string }[]>("/settings/languages"),
      ]);
      setBooks(queueRes.data || []);
      setLanguages(langRes.data || []);
      if (queueRes.data && queueRes.data.length > 0) {
        setExpandedPath(queueRes.data[0].fullPath);
      }
    } catch (err) {
      toast.error(handleApiError(err).message);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchUnorganized();
  }, []);

  const handleSaveBook = async (payload: any) => {
    try {
      await api.post("/organize/organize-audiobook", payload);
      toast.success("Audiobook organized successfully!");
      setBooks((prev) => prev.filter((b) => b.fullPath !== payload.fullPath));
    } catch (err) {
      toast.error(handleApiError(err).message);
    }
  };

  const handleDeleteBook = async (fullPath: string) => {
    try {
      await api.delete("/files/audiobook", { params: { fullPath } });
      toast.success("File deleted successfully");
      setBooks((prev) => prev.filter((b) => b.fullPath !== fullPath));
    } catch (err) {
      toast.error(handleApiError(err).message);
    }
  };

  if (loading) {
    return (
      <div className="text-center py-12 text-muted-foreground">
        Loading import queue...
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold flex items-center gap-2">
            <FolderInput className="h-6 w-6 text-primary" />
            Organize Audiobooks
          </h1>
          <p className="text-sm text-muted-foreground">
            Review and organize imported m4b audiobook files into your library
            structure.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={fetchUnorganized}
        >
          <RefreshCw className="h-4 w-4 mr-2" />
          Refresh Queue
        </Button>
      </div>

      {books.length === 0 ? (
        <Card>
          <CardContent className="py-12 text-center space-y-3">
            <CheckCircle2 className="h-12 w-12 text-emerald-500 mx-auto" />
            <h3 className="font-semibold text-lg">No Unorganized Books</h3>
            <p className="text-sm text-muted-foreground max-w-sm mx-auto">
              Your import folder is currently empty. Place m4b files in your
              import folder to process them here.
            </p>
          </CardContent>
        </Card>
      ) : (
        <Accordion
          type="single"
          collapsible
          value={expandedPath}
          onValueChange={setExpandedPath}
          className="space-y-4"
        >
          {books.map((book) => (
            <AccordionItem
              key={book.fullPath}
              value={book.fullPath}
              className="border border-border rounded-lg bg-card px-4"
            >
              <AccordionTrigger className="hover:no-underline">
                <div className="flex items-center gap-3 text-left">
                  <span className="font-semibold text-sm">
                    {book.bookName || book.filename}
                  </span>
                  {book.authors && book.authors.length > 0 && (
                    <span className="text-xs text-muted-foreground">
                      by {book.authors.join(", ")}
                    </span>
                  )}
                </div>
              </AccordionTrigger>
              <AccordionContent className="pt-4 border-t border-border">
                <BookEditForm
                  initialBook={book}
                  languages={languages}
                  onSave={handleSaveBook}
                  onDelete={handleDeleteBook}
                />
              </AccordionContent>
            </AccordionItem>
          ))}
        </Accordion>
      )}
    </div>
  );
};
export default BookOrganize;
