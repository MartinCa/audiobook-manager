import type { ReactNode } from "react";
import { ExternalLink } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { metadataSearchApi } from "@/services/api";
import type { MetadataSearchResult } from "@/types/MetadataSearchResult";

interface MetadataSearchResultCardProps {
  result: MetadataSearchResult;
  /** The caller's own action button(s) (e.g. "Apply", "Select this match") - kept a slot rather
   * than a fixed prop so each flow's specific action stays with the flow, not this shared card. */
  actions?: ReactNode;
}

/**
 * One scraped metadata result's presentation, shared by every flow that shows candidate search
 * results: BookSearchDialog's interactive search and the bulk online-match pending list's
 * per-book expand panel. Extracted so both render a result identically - no drift.
 */
export function MetadataSearchResultCard({ result, actions }: MetadataSearchResultCardProps) {
  return (
    <div className="border-border bg-card hover:bg-muted/50 flex flex-col justify-between gap-3 rounded-lg border p-3 transition-colors sm:flex-row sm:items-start sm:gap-4">
      <div className="flex min-w-0 flex-1 gap-3">
        {result.imageUrl && (
          <img
            src={metadataSearchApi.getProxyImageUrl(result.imageUrl)}
            alt={result.bookName}
            className="h-16 w-16 shrink-0 rounded object-contain shadow-sm"
            onError={(e) => {
              (e.currentTarget as HTMLElement).style.display = "none";
            }}
          />
        )}
        <div className="min-w-0 flex-1 space-y-1">
          <div className="flex flex-wrap items-center gap-1.5 sm:gap-2">
            <span className="text-foreground font-semibold break-words">{result.bookName}</span>
            <Badge variant="secondary" className="text-[10px]">
              {result.source}
            </Badge>
            {result.year && <span className="text-muted-foreground text-xs">({result.year})</span>}
            {result.duration && (
              <span className="text-muted-foreground text-xs">· {result.duration}</span>
            )}
            {result.language && (
              <span className="text-muted-foreground text-xs capitalize">{result.language}</span>
            )}
          </div>

          <div className="text-muted-foreground space-y-0.5 text-xs">
            {result.authors && result.authors.length > 0 && (
              <div className="break-words">
                By:{" "}
                <span className="text-foreground font-medium">
                  {result.authors.map((a) => a.name).join(", ")}
                </span>
              </div>
            )}
            {result.narrators && result.narrators.length > 0 && (
              <div className="break-words">
                Narrated by: {result.narrators.map((n) => n.name).join(", ")}
              </div>
            )}
            {result.series?.[0] && (
              <div className="break-words">
                Series: {result.series[0].seriesName}{" "}
                {result.series[0].seriesPart && `#${result.series[0].seriesPart}`}
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="border-border/50 flex items-center justify-between gap-2 border-t pt-2 sm:flex-col sm:items-end sm:justify-start sm:border-t-0 sm:pt-0">
        {result.url && (
          <a
            href={result.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-muted-foreground hover:text-foreground flex items-center text-[11px]"
          >
            <ExternalLink className="mr-1 h-3 w-3" />
            View Source
          </a>
        )}
        {actions}
      </div>
    </div>
  );
}

export default MetadataSearchResultCard;
