import {
  useState,
  useMemo,
  forwardRef,
  type ComponentProps,
  type FocusEvent,
  type KeyboardEvent,
} from "react";
import { Input } from "@/components/ui/input";
import { narrowByQuery, normalizeForMatch } from "@/helpers/similarValueMatcher";
import { cn } from "@/lib/utils";

export interface TypeaheadInputProps extends Omit<ComponentProps<"input">, "onChange" | "value"> {
  value: string;
  onValueChange: (value: string) => void;
  candidates: string[];
  multiValue?: boolean;
  onSelectSuggestion?: (suggestion: string) => void;
}

export const TypeaheadInput = forwardRef<HTMLInputElement, TypeaheadInputProps>(
  (
    {
      value,
      onValueChange,
      candidates,
      multiValue = false,
      onSelectSuggestion,
      className,
      onBlur,
      onFocus,
      onKeyDown,
      ...props
    },
    ref,
  ) => {
    const [isOpen, setIsOpen] = useState(false);
    const [highlightedIndex, setHighlightedIndex] = useState(-1);

    const activeQuery = useMemo(() => {
      if (!multiValue) return value.trim();
      const parts = value.split(",");
      return (parts[parts.length - 1] ?? "").trim();
    }, [value, multiValue]);

    const suggestions = useMemo(() => {
      if (!activeQuery) return [];
      const matches = narrowByQuery(candidates, activeQuery, 6);
      if (
        matches.length === 1 &&
        normalizeForMatch(matches[0]) === normalizeForMatch(activeQuery)
      ) {
        return [];
      }
      return matches;
    }, [candidates, activeQuery]);

    const applySuggestion = (suggestion: string) => {
      let nextValue: string;
      if (multiValue) {
        const parts = value.split(",");
        parts[parts.length - 1] = parts.length > 1 ? ` ${suggestion}` : suggestion;
        nextValue = parts.join(",");
      } else {
        nextValue = suggestion;
      }
      onValueChange(nextValue);
      onSelectSuggestion?.(suggestion);
      setIsOpen(false);
      setHighlightedIndex(-1);
    };

    const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
      if (!isOpen || suggestions.length === 0) {
        onKeyDown?.(e);
        return;
      }

      if (e.key === "ArrowDown") {
        e.preventDefault();
        setHighlightedIndex((prev) => (prev + 1) % suggestions.length);
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setHighlightedIndex((prev) => (prev <= 0 ? suggestions.length - 1 : prev - 1));
      } else if (e.key === "Enter") {
        const selected = suggestions[highlightedIndex];
        if (selected) {
          e.preventDefault();
          e.stopPropagation();
          applySuggestion(selected);
        } else {
          onKeyDown?.(e);
        }
      } else if (e.key === "Escape") {
        e.preventDefault();
        setIsOpen(false);
        setHighlightedIndex(-1);
      } else {
        onKeyDown?.(e);
      }
    };

    const handleFocus = (e: FocusEvent<HTMLInputElement>) => {
      setIsOpen(true);
      onFocus?.(e);
    };

    const handleBlur = (e: FocusEvent<HTMLInputElement>) => {
      setIsOpen(false);
      setHighlightedIndex(-1);
      onBlur?.(e);
    };

    return (
      <div className="relative w-full">
        <Input
          ref={ref}
          value={value}
          onChange={(e) => {
            onValueChange(e.target.value);
            setIsOpen(true);
            setHighlightedIndex(-1);
          }}
          onFocus={handleFocus}
          onBlur={handleBlur}
          onKeyDown={handleKeyDown}
          className={className}
          autoComplete="off"
          {...props}
        />

        {isOpen && suggestions.length > 0 && (
          <ul
            role="listbox"
            className="border-border bg-popover text-popover-foreground absolute top-full left-0 z-50 mt-1 max-h-48 w-full overflow-y-auto overscroll-contain rounded-md border shadow-md sm:max-h-56"
          >
            {suggestions.map((suggestion, index) => (
              <li
                key={suggestion}
                role="option"
                aria-selected={index === highlightedIndex}
                className={cn(
                  "cursor-pointer px-3.5 py-2.5 text-sm transition-colors select-none",
                  index === highlightedIndex
                    ? "bg-accent text-accent-foreground font-medium"
                    : "hover:bg-accent/80 hover:text-accent-foreground text-popover-foreground",
                )}
                onPointerDown={(e) => {
                  e.preventDefault();
                  applySuggestion(suggestion);
                }}
                onMouseEnter={() => setHighlightedIndex(index)}
              >
                {suggestion}
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  },
);

TypeaheadInput.displayName = "TypeaheadInput";
