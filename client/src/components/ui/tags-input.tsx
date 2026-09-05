import { useState, type KeyboardEvent } from "react";
import { X } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

export interface TagsInputProps {
  value: string[];
  onValueChange: (value: string[]) => void;
  placeholder?: string;
  className?: string;
  "aria-invalid"?: boolean;
  disabled?: boolean;
}

// A chip-based control for fields that are really a small set of discrete values (genres,
// authors, narrators) rather than a single string. Committing a value as its own chip - on
// Enter, Tab or blur - removes the ambiguity a single "a / b / c" text field has: there is no
// separator character for the user to type correctly, and nothing to accidentally split on if a
// value itself contains a "/".
export function TagsInput({
  value,
  onValueChange,
  placeholder,
  className,
  disabled,
  ...props
}: TagsInputProps) {
  const [draft, setDraft] = useState("");

  const commitDraft = () => {
    const trimmed = draft.trim();
    setDraft("");
    if (trimmed.length === 0) return;
    if (value.some((v) => v.toLowerCase() === trimmed.toLowerCase())) return;
    onValueChange([...value, trimmed]);
  };

  const removeAt = (index: number) => {
    onValueChange(value.filter((_, i) => i !== index));
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter" || e.key === "Tab") {
      if (draft.trim().length > 0) {
        e.preventDefault();
        commitDraft();
      }
      return;
    }

    if (e.key === "Backspace" && draft.length === 0 && value.length > 0) {
      e.preventDefault();
      removeAt(value.length - 1);
    }
  };

  return (
    <div
      className={cn(
        "border-input bg-background ring-offset-background focus-within:ring-ring flex min-h-10 w-full flex-wrap items-center gap-1.5 rounded-md border px-2 py-1.5 focus-within:ring-2 focus-within:ring-offset-2",
        disabled && "cursor-not-allowed opacity-50",
        className,
      )}
      aria-invalid={props["aria-invalid"]}
    >
      {value.map((tag, index) => (
        <Badge key={`${tag}-${index}`} variant="secondary" className="gap-1 py-1 pr-1 font-normal">
          <span>{tag}</span>
          {!disabled && (
            <button
              type="button"
              onClick={() => removeAt(index)}
              className="hover:bg-secondary-foreground/20 rounded-full p-0.5"
              aria-label={`Remove ${tag}`}
            >
              <X className="h-3 w-3" />
            </button>
          )}
        </Badge>
      ))}
      <input
        type="text"
        value={draft}
        disabled={disabled}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={handleKeyDown}
        onBlur={commitDraft}
        placeholder={value.length === 0 ? placeholder : undefined}
        className="placeholder:text-muted-foreground min-w-24 flex-1 bg-transparent text-base outline-none disabled:cursor-not-allowed md:text-sm"
      />
    </div>
  );
}
