import { Badge } from "@/components/ui/badge";
import type { MetadataSearchServiceInfo } from "@/types/MetadataSearchServiceInfo";

interface MetadataSourceSelectorProps {
  services: MetadataSearchServiceInfo[];
  activeSources: string[];
  onToggleSource: (sourceName: string) => void;
  label?: string;
}

/**
 * The clickable-badge metadata-source picker, shared by every flow that lets a user choose which
 * scraper sources to search: BookSearchDialog's interactive dialog and the bulk online-match
 * search dialog. Extracted so the two never drift - a source's enabled/disabled presentation is
 * defined once.
 */
export function MetadataSourceSelector({
  services,
  activeSources,
  onToggleSource,
  label = "Metadata Sources",
}: MetadataSourceSelectorProps) {
  return (
    <div>
      <div className="text-muted-foreground mb-1.5 text-xs font-semibold uppercase">{label}</div>
      <div className="flex flex-wrap gap-1.5 sm:gap-2">
        {services.map((service) => {
          const isConfigured = service.enabled;
          const isSelected = activeSources.includes(service.name);

          return (
            <Badge
              key={service.name}
              variant={isSelected ? "default" : isConfigured ? "outline" : "secondary"}
              className={`cursor-pointer text-[11px] select-none ${
                !isConfigured ? "cursor-not-allowed opacity-50" : "hover:bg-primary/90"
              }`}
              onClick={() => {
                if (isConfigured) onToggleSource(service.name);
              }}
            >
              {service.name}
              {!isConfigured && ` (${service.disabledReason || "Unavailable"})`}
            </Badge>
          );
        })}
      </div>
    </div>
  );
}

export default MetadataSourceSelector;
