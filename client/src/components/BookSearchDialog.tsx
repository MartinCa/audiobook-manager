import React, { useState, useEffect } from "react";
import { Search, Globe, AlertCircle } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { api, handleApiError } from "@/lib/api";
import { type MetadataSearchResult } from "@/types/domain";

interface BookSearchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSelectResult: (result: MetadataSearchResult) => void;
}

interface SearchServiceInfo {
  sourceName: string;
  isApiKeyConfigured: boolean;
  requiresApiKey: boolean;
}

export const BookSearchDialog: React.FC<BookSearchDialogProps> = ({
  open,
  onOpenChange,
  onSelectResult,
}) => {
  const [query, setQuery] = useState("");
  const [services, setServices] = useState<SearchServiceInfo[]>([]);
  const [selectedSources, setSelectedSources] = useState<string[]>([]);
  const [results, setResults] = useState<MetadataSearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      api
        .get<SearchServiceInfo[]>("/metadata-search/services")
        .then((res) => {
          setServices(res.data);
          const active = res.data
            .filter((s) => !s.requiresApiKey || s.isApiKeyConfigured)
            .map((s) => s.sourceName);
          setSelectedSources(active);
        })
        .catch(() => {});
    }
  }, [open]);

  const toggleSource = (sourceName: string) => {
    setSelectedSources((prev) =>
      prev.includes(sourceName)
        ? prev.filter((s) => s !== sourceName)
        : [...prev, sourceName],
    );
  };

  const handleSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    setError(null);
    setResults([]);

    try {
      if (query.startsWith("http://") || query.startsWith("https://")) {
        const res = await api.get<MetadataSearchResult>(
          "/metadata-search/by-url",
          {
            params: { url: query },
          },
        );
        setResults(res.data ? [res.data] : []);
      } else {
        const res = await api.get<MetadataSearchResult[]>(
          "/metadata-search/search",
          {
            params: { query, sources: selectedSources.join(",") },
          },
        );
        setResults(res.data || []);
      }
    } catch (err) {
      const apiErr = handleApiError(err);
      setError(apiErr.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
    >
      <DialogContent className="max-w-3xl max-h-[85vh] flex flex-col">
        <DialogHeader>
          <DialogTitle>Search Metadata</DialogTitle>
        </DialogHeader>

        <form
          onSubmit={handleSearch}
          className="space-y-4"
        >
          <div className="flex gap-2">
            <Input
              placeholder="Search by title, author, or paste URL..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="flex-1"
            />
            <Button
              type="submit"
              disabled={loading}
            >
              <Search className="h-4 w-4 mr-2" />
              Search
            </Button>
          </div>

          <div className="flex flex-wrap gap-2 items-center">
            <span className="text-xs text-muted-foreground font-medium">
              Sources:
            </span>
            {services.map((svc) => {
              const isDisabled = svc.requiresApiKey && !svc.isApiKeyConfigured;
              const isSelected = selectedSources.includes(svc.sourceName);
              return (
                <Badge
                  key={svc.sourceName}
                  variant={isSelected ? "default" : "outline"}
                  className={`cursor-pointer ${
                    isDisabled ? "opacity-50 cursor-not-allowed" : ""
                  }`}
                  onClick={() => !isDisabled && toggleSource(svc.sourceName)}
                >
                  {svc.sourceName}
                  {isDisabled && " (No Key)"}
                </Badge>
              );
            })}
          </div>
        </form>

        {error && (
          <div className="flex items-center gap-2 p-3 text-sm text-destructive border border-destructive/20 rounded-md bg-destructive/10">
            <AlertCircle className="h-4 w-4" />
            <span>{error}</span>
          </div>
        )}

        <div className="flex-1 overflow-y-auto space-y-3 mt-4 pr-1">
          {loading ? (
            <div className="text-center py-8 text-muted-foreground text-sm">
              Searching online services...
            </div>
          ) : results.length === 0 ? (
            <div className="text-center py-8 text-muted-foreground text-sm">
              No metadata results found.
            </div>
          ) : (
            results.map((res, i) => (
              <Card
                key={i}
                className="hover:border-primary transition-colors cursor-pointer"
                onClick={() => onSelectResult(res)}
              >
                <CardContent className="p-4 flex gap-4">
                  {res.coverUrl ? (
                    <img
                      src={res.coverUrl}
                      alt={res.title}
                      className="w-16 h-20 object-cover rounded"
                    />
                  ) : (
                    <div className="w-16 h-20 bg-muted rounded flex items-center justify-center">
                      <Globe className="h-6 w-6 text-muted-foreground" />
                    </div>
                  )}
                  <div className="flex-1 min-w-0">
                    <div className="flex justify-between items-start gap-2">
                      <h4 className="font-semibold text-sm truncate">
                        {res.title}
                      </h4>
                      <Badge
                        variant="secondary"
                        className="text-xs"
                      >
                        {res.source}
                      </Badge>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      By: {res.authors.join(", ") || "Unknown"}
                    </p>
                    {res.narrators.length > 0 && (
                      <p className="text-xs text-muted-foreground">
                        Narrated by: {res.narrators.join(", ")}
                      </p>
                    )}
                    {res.series && (
                      <p className="text-xs text-muted-foreground">
                        Series: {res.series}{" "}
                        {res.seriesPart && `#${res.seriesPart}`}
                      </p>
                    )}
                  </div>
                </CardContent>
              </Card>
            ))
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
};
export default BookSearchDialog;
