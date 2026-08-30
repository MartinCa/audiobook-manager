import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Tag, BookOpen, ChevronRight, Globe, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card } from "@/components/ui/card";
import { OperationProgressBar } from "./OperationProgressBar";
import { missingTagsApi, operationsApi } from "@/services/api";
import { handleApiError } from "@/lib/api";
import { toast } from "sonner";

export function MissingTags() {
  const queryClient = useQueryClient();
  const [selectedFields, setSelectedFields] = useState<string[]>([]);
  const [backfilling, setBackfilling] = useState(false);

  const { data: fields = [], isLoading: loadingFields } = useQuery({
    queryKey: ["missingTagFields"],
    queryFn: () => missingTagsApi.getFields(),
  });

  // Default to first field if none selected
  const activeSelected =
    selectedFields.length > 0 ? selectedFields : fields[0]?.key ? [fields[0].key] : [];

  const { data: audiobooks = [], isLoading: loadingBooks } = useQuery({
    queryKey: ["missingTagsAudiobooks", activeSelected],
    queryFn: () => missingTagsApi.getAudiobooksMissingTags(activeSelected),
    enabled: activeSelected.length > 0,
  });

  const { data: backfillStatus } = useQuery({
    queryKey: ["languageBackfillStatus"],
    queryFn: () => operationsApi.getStatus("language-backfill"),
    enabled: backfilling,
    refetchInterval: backfilling ? 1500 : false,
  });

  useEffect(() => {
    if (backfilling && backfillStatus && !backfillStatus.isRunning) {
      toast.success("Language backfill operation completed");
      void queryClient.invalidateQueries({
        queryKey: ["missingTagsAudiobooks"],
      });
    }
  }, [backfilling, backfillStatus, queryClient]);

  const toggleField = (key: string) => {
    setSelectedFields((prev) => {
      const current = prev.length > 0 ? prev : fields[0]?.key ? [fields[0].key] : [];
      return current.includes(key) ? current.filter((k) => k !== key) : [...current, key];
    });
  };

  const handleStartLanguageBackfill = async () => {
    setBackfilling(true);
    try {
      await missingTagsApi.startLanguageBackfill();
      toast.success("Language backfill started in background");
    } catch (err: unknown) {
      toast.error(handleApiError(err).message);
      setBackfilling(false);
    }
  };

  const isBackfillRunning = backfilling && backfillStatus?.isRunning;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Button variant="ghost" size="sm" asChild>
          <Link to="/library">
            <ArrowLeft className="mr-2 h-4 w-4" />
            Back to Library
          </Link>
        </Button>

        <Button
          variant="outline"
          onClick={() => {
            void handleStartLanguageBackfill();
          }}
          disabled={isBackfillRunning}
        >
          <Globe className={`mr-2 h-4 w-4 ${isBackfillRunning ? "animate-spin" : ""}`} />
          {isBackfillRunning ? "Backfilling Languages..." : "Backfill Missing Languages"}
        </Button>
      </div>

      <div>
        <h1 className="text-foreground flex items-center gap-2 text-2xl font-bold">
          <Tag className="text-primary h-6 w-6" />
          Missing Tags Inspection
        </h1>
        <p className="text-muted-foreground text-sm">
          Find audiobooks in your library missing critical tags such as author, year, narrator, or
          language.
        </p>
      </div>

      {isBackfillRunning && backfillStatus && (
        <OperationProgressBar
          processed={backfillStatus.processed}
          total={backfillStatus.total}
          label="Backfilling missing languages..."
        />
      )}

      <div className="space-y-2">
        <label className="text-muted-foreground text-xs font-semibold uppercase">
          Select Fields to Inspect
        </label>
        <div className="flex flex-wrap gap-2">
          {fields.map((f) => {
            const isSelected = activeSelected.includes(f.key);
            return (
              <Badge
                key={f.key}
                variant={isSelected ? "default" : "outline"}
                className="hover:bg-primary/90 cursor-pointer px-3 py-1.5 select-none"
                onClick={() => toggleField(f.key)}
              >
                {f.label}
              </Badge>
            );
          })}
        </div>
      </div>

      <div className="space-y-3">
        <div className="flex items-center justify-between">
          <h2 className="text-foreground text-lg font-bold">
            Audiobooks with Missing Tags ({audiobooks.length})
          </h2>
        </div>

        {loadingFields || loadingBooks ? (
          <div className="text-muted-foreground flex flex-col items-center justify-center py-16">
            <Loader2 className="text-primary mb-3 h-8 w-8 animate-spin" />
            <p className="text-sm">Scanning tags...</p>
          </div>
        ) : audiobooks.length === 0 ? (
          <Card className="p-12 text-center">
            <BookOpen className="text-muted-foreground/40 mx-auto mb-3 h-12 w-12" />
            <h3 className="text-foreground text-lg font-medium">
              No audiobooks missing selected tags
            </h3>
            <p className="text-muted-foreground mt-1 text-sm">
              Every book in your library contains the selected tag fields.
            </p>
          </Card>
        ) : (
          <div className="space-y-2">
            {audiobooks.map((b) => (
              <Link
                key={b.audiobookId}
                to={`/library/book/${b.audiobookId}`}
                className="group border-border bg-card hover:bg-muted/50 flex items-center justify-between rounded-lg border p-3 transition-colors"
              >
                <div className="min-w-0">
                  <div className="text-foreground font-semibold">
                    {b.authors.join(", ")} &mdash; {b.bookName}
                  </div>
                  <div className="flex flex-wrap gap-1.5 pt-1">
                    {b.missingFields.map((f) => (
                      <Badge
                        key={f}
                        variant="secondary"
                        className="bg-amber-500/15 text-[10px] text-amber-600 dark:text-amber-400"
                      >
                        Missing {f}
                      </Badge>
                    ))}
                  </div>
                </div>

                <ChevronRight className="text-muted-foreground group-hover:text-foreground h-4 w-4" />
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default MissingTags;
