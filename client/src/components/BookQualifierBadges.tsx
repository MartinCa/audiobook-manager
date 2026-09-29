import { Badge } from "@/components/ui/badge";
import { useBookQualifiers } from "@/hooks/useBookQualifiers";
import { normalizeQualifiers, qualifierLabel } from "@/helpers/bookQualifiers";
import { cn } from "cn";

export interface BookQualifierBadgesProps {
  /** Keys of the book's qualifiers (abridged, dramatized, ...). Nothing renders when empty. */
  qualifiers: readonly string[] | null | undefined;
  className?: string;
}

/**
 * One badge per qualifier, alphabetical, wherever a book is presented. The labels come from the
 * backend's qualifier list; a key it no longer knows renders as itself rather than vanishing.
 */
export function BookQualifierBadges({ qualifiers, className }: BookQualifierBadgesProps) {
  // Most books carry none, and this renders once per row of long lists: only a book that has a
  // qualifier pays for the label lookup (and needs the query cache at all).
  if (!qualifiers || qualifiers.length === 0) {
    return null;
  }

  return <QualifierBadgeList qualifiers={qualifiers} className={className} />;
}

function QualifierBadgeList({
  qualifiers,
  className,
}: {
  qualifiers: readonly string[];
  className?: string;
}) {
  const options = useBookQualifiers();
  const keys = normalizeQualifiers(qualifiers, options);

  if (keys.length === 0) {
    return null;
  }

  return (
    <span
      className={cn("inline-flex flex-wrap items-center gap-1 align-middle", className)}
      data-testid="book-qualifier-badges"
    >
      {keys.map((key) => (
        <Badge key={key} variant="secondary">
          {qualifierLabel(key, options)}
        </Badge>
      ))}
    </span>
  );
}
