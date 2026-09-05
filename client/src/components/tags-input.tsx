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
//
// Order-preserving by design: every existing chip can only ever be replaced in place (edit) or
// removed at its own index (delete) - nothing here ever removes-then-re-appends an entry, which
// would silently move it. That matters beyond Genres (order-insensitive): the same component is
// meant to grow into Authors/Narrators, where array position is meaningful (folder naming,
// credits order - see AudiobookFileHandler.cs). Drag-to-reorder is intentionally not here yet
// (tracked separately) - it needs the same order-preserving guarantee.
export function TagsInput({
  value,
  onValueChange,
  placeholder,
  className,
  disabled,
  ...props
}: TagsInputProps) {
  const [draft, setDraft] = useState("");
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState("");

  const isDuplicate = (candidate: string, excludeIndex?: number) =>
    value.some((v, i) => i !== excludeIndex && v.toLowerCase() === candidate.toLowerCase());

  const commitDraft = () => {
    const trimmed = draft.trim();
    setDraft("");
    if (trimmed.length === 0) return;
    if (isDuplicate(trimmed)) return;
    onValueChange([...value, trimmed]);
  };

  const removeAt = (index: number) => {
    onValueChange(value.filter((_, i) => i !== index));
  };

  const startEditing = (index: number) => {
    if (disabled) return;
    const current = value[index];
    if (current === undefined) return;
    setEditingIndex(index);
    setEditDraft(current);
  };

  // Always a same-length, same-position replace (or, for an emptied value, a removal at that
  // one index) - never a remove-and-re-append, which is what would silently reorder entries.
  const commitEdit = () => {
    if (editingIndex === null) return;
    const index = editingIndex;
    const trimmed = editDraft.trim();
    setEditingIndex(null);

    if (trimmed.length === 0) {
      removeAt(index);
      return;
    }
    if (trimmed === value[index]) return;
    if (isDuplicate(trimmed, index)) return;

    onValueChange(value.map((v, i) => (i === index ? trimmed : v)));
  };

  const cancelEdit = () => {
    setEditingIndex(null);
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

  const handleEditKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter") {
      e.preventDefault();
      commitEdit();
    } else if (e.key === "Escape") {
      e.preventDefault();
      cancelEdit();
    }
  };

  // A blur caused by Escape (which already cancelled the edit) must not then commit it again -
  // cancelEdit already cleared editingIndex, so this is a no-op in that case.
  const handleEditBlur = () => {
    commitEdit();
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
      {value.map((tag, index) =>
        editingIndex === index ? (
          <input
            key={`${tag}-${index}`}
            type="text"
            autoFocus
            value={editDraft}
            onChange={(e) => setEditDraft(e.target.value)}
            onKeyDown={handleEditKeyDown}
            onBlur={handleEditBlur}
            className="border-input bg-background min-w-24 rounded-md border px-2 py-1 text-base outline-none md:text-sm"
          />
        ) : (
          <Badge
            key={`${tag}-${index}`}
            variant="secondary"
            className="gap-1 py-0 pr-1 pl-1 font-normal"
          >
            <button
              type="button"
              onClick={() => startEditing(index)}
              disabled={disabled}
              className="hover:bg-secondary-foreground/20 rounded-full px-1.5 py-1.5 disabled:cursor-not-allowed"
              aria-label={`Edit ${tag}`}
            >
              {tag}
            </button>
            {!disabled && (
              <button
                type="button"
                onClick={() => removeAt(index)}
                className="hover:bg-secondary-foreground/20 rounded-full p-1.5"
                aria-label={`Remove ${tag}`}
              >
                <X className="h-3 w-3" />
              </button>
            )}
          </Badge>
        ),
      )}
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
