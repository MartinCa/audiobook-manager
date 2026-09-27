import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PAGE_SIZE_OPTIONS, type PageSizeOption } from "@/constants/paging";

interface PageSizeSelectProps {
  value: number;
  onChange: (value: PageSizeOption) => void;
  disabled?: boolean;
}

const ITEMS = PAGE_SIZE_OPTIONS.map((size) => ({ value: size, label: `${size} / page` }));

/**
 * The rows-per-page dropdown shared by every paged list (see SectionPager) and by the handful of
 * lists that render their own pager markup instead of SectionPager. Offers exactly the values
 * `SettingsController.AllowedPageSizes` accepts for the default-page-size library setting, so a
 * value picked here is always one that setting could also hold.
 */
export function PageSizeSelect({ value, onChange, disabled = false }: PageSizeSelectProps) {
  return (
    <Select
      value={value}
      onValueChange={(v) => {
        if (v != null) onChange(v as PageSizeOption);
      }}
      items={ITEMS}
      disabled={disabled}
    >
      <SelectTrigger size="sm" aria-label="Rows per page">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {ITEMS.map((item) => (
          <SelectItem key={item.value} value={item.value}>
            {item.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

export default PageSizeSelect;
