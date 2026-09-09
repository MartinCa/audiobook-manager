import { useQuery } from "@tanstack/react-query";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { settingsApi } from "@/services/api";
import { languageSelectItems } from "@/helpers/languages";
import type { LanguageOption } from "@/types/Language";

export interface LanguageFieldProps {
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  /** Empty-state placeholder; the bulk-edit dialog overrides it with "Different values" for mixed previews. */
  placeholder?: string;
  /**
   * Whether to render the field's own label. The bulk-edit dialog suppresses it because its
   * FieldRow already renders one; BookEditForm keeps the default (true) so its DOM is unchanged.
   */
  showLabel?: boolean;
}

export function LanguageField({
  value,
  onChange,
  disabled = false,
  placeholder = "Select language...",
  showLabel = true,
}: LanguageFieldProps) {
  const { data: languagesRes } = useQuery({
    queryKey: ["languages"],
    queryFn: () => settingsApi.getLanguages(),
  });
  const languages: LanguageOption[] = languagesRes?.languages ?? [];

  return (
    <div className="min-w-0 flex-1">
      {showLabel && <label className="mb-1 block text-xs font-medium">Language</label>}
      <Select
        value={value || ""}
        onValueChange={(val) => onChange(val ?? "")}
        disabled={disabled}
        items={languageSelectItems(value, languages).map((l) => ({
          value: l.code,
          label: l.displayName,
        }))}
      >
        <SelectTrigger>
          <SelectValue placeholder={placeholder} />
        </SelectTrigger>
        <SelectContent>
          {languageSelectItems(value, languages).map((l) => (
            <SelectItem key={l.code} value={l.code}>
              {l.displayName}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
