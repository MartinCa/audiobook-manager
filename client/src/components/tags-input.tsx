import { useMemo, useState, type KeyboardEvent } from "react";
import { GripVertical, X } from "lucide-react";
import {
  DndContext,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  horizontalListSortingStrategy,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { badgeVariants } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { narrowByQuery, normalizeForMatch } from "@/helpers/similarValueMatcher";

export interface TagsInputProps {
  value: string[];
  onValueChange: (value: string[]) => void;
  placeholder?: string;
  className?: string;
  "aria-invalid"?: boolean;
  disabled?: boolean;
  /**
   * Existing values (e.g. every author already in the library) to narrow while typing a new
   * entry, the same way TypeaheadInput's `candidates` narrowing works. Omit for fields (like
   * Genres) that have no such candidate list.
   */
  suggestions?: string[];
  /**
   * Enables drag-and-drop reordering of committed chips. Off by default: array position only
   * matters for fields like Authors/Narrators (folder naming, credits order), not Genres.
   */
  reorderable?: boolean;
  /** Fires after a new chip is successfully committed (not on a rejected duplicate). */
  onEntryCommitted?: (value: string) => void;
}

// A chip-based control for fields that are really a small set of discrete values (genres,
// authors, narrators) rather than a single string. Committing a value as its own chip - on
// Enter, Tab or blur - removes the ambiguity a single "a / b / c" text field has: there is no
// separator character for the user to type correctly, and nothing to accidentally split on if a
// value itself contains a "/".
//
// Order-preserving by design: every existing chip can only ever be replaced in place (edit) or
// removed at its own index (delete) - nothing here ever removes-then-re-appends an entry, which
// would silently move it. Reordering is only ever an explicit drag gesture (`reorderable`),
// never a side effect of edit/remove.
export function TagsInput({
  value,
  onValueChange,
  placeholder,
  className,
  disabled,
  suggestions = [],
  reorderable = false,
  onEntryCommitted,
  ...props
}: TagsInputProps) {
  const [draft, setDraft] = useState("");
  const [isDraftOpen, setIsDraftOpen] = useState(false);
  const [highlightedIndex, setHighlightedIndex] = useState(-1);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState("");
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [editHighlightedIndex, setEditHighlightedIndex] = useState(-1);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 4 } }));

  const isDuplicate = (candidate: string, excludeIndex?: number) =>
    value.some((v, i) => i !== excludeIndex && v.toLowerCase() === candidate.toLowerCase());

  const draftSuggestions = useMemo(() => {
    if (suggestions.length === 0) return [];
    const trimmed = draft.trim();
    if (!trimmed) return [];
    const matches = narrowByQuery(suggestions, trimmed, 6).filter((s) => !isDuplicate(s));
    if (matches.length === 1 && normalizeForMatch(matches[0]) === normalizeForMatch(trimmed)) {
      return [];
    }
    return matches;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [draft, suggestions, value]);

  // Same narrowing as draftSuggestions, but excludes the entry currently being edited from the
  // duplicate check (it's fine to retype a value back toward itself) rather than every entry.
  const editSuggestions = useMemo(() => {
    if (suggestions.length === 0 || editingIndex === null) return [];
    const trimmed = editDraft.trim();
    if (!trimmed) return [];
    const matches = narrowByQuery(suggestions, trimmed, 6).filter(
      (s) => !isDuplicate(s, editingIndex),
    );
    if (matches.length === 1 && normalizeForMatch(matches[0]) === normalizeForMatch(trimmed)) {
      return [];
    }
    return matches;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editDraft, suggestions, value, editingIndex]);

  const commitValue = (raw: string) => {
    const trimmed = raw.trim();
    if (trimmed.length === 0) return;
    if (isDuplicate(trimmed)) return;
    onValueChange([...value, trimmed]);
    onEntryCommitted?.(trimmed);
  };

  const commitDraft = () => {
    const trimmed = draft.trim();
    setDraft("");
    setIsDraftOpen(false);
    setHighlightedIndex(-1);
    commitValue(trimmed);
  };

  const applySuggestion = (suggestion: string) => {
    setDraft("");
    setIsDraftOpen(false);
    setHighlightedIndex(-1);
    commitValue(suggestion);
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
    setIsEditOpen(false);
    setEditHighlightedIndex(-1);
  };

  // Always a same-length, same-position replace (or, for an emptied value, a removal at that
  // one index) - never a remove-and-re-append, which is what would silently reorder entries.
  const commitEdit = () => {
    if (editingIndex === null) return;
    const index = editingIndex;
    const trimmed = editDraft.trim();
    setEditingIndex(null);
    setIsEditOpen(false);
    setEditHighlightedIndex(-1);

    if (trimmed.length === 0) {
      removeAt(index);
      return;
    }
    if (trimmed === value[index]) return;
    if (isDuplicate(trimmed, index)) return;

    onValueChange(value.map((v, i) => (i === index ? trimmed : v)));
  };

  const applyEditSuggestion = (suggestion: string) => {
    if (editingIndex === null) return;
    const index = editingIndex;
    setEditingIndex(null);
    setIsEditOpen(false);
    setEditHighlightedIndex(-1);

    if (suggestion === value[index]) return;
    if (isDuplicate(suggestion, index)) return;

    onValueChange(value.map((v, i) => (i === index ? suggestion : v)));
  };

  const cancelEdit = () => {
    setEditingIndex(null);
    setIsEditOpen(false);
    setEditHighlightedIndex(-1);
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (isDraftOpen && draftSuggestions.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault();
        setHighlightedIndex((prev) => (prev + 1) % draftSuggestions.length);
        return;
      }
      if (e.key === "ArrowUp") {
        e.preventDefault();
        setHighlightedIndex((prev) => (prev <= 0 ? draftSuggestions.length - 1 : prev - 1));
        return;
      }
      if (e.key === "Escape") {
        e.preventDefault();
        setIsDraftOpen(false);
        setHighlightedIndex(-1);
        return;
      }
      if (e.key === "Enter" || e.key === "Tab") {
        const selected = draftSuggestions[highlightedIndex];
        if (selected) {
          e.preventDefault();
          applySuggestion(selected);
          return;
        }
      }
    }

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
    if (isEditOpen && editSuggestions.length > 0) {
      if (e.key === "ArrowDown") {
        e.preventDefault();
        setEditHighlightedIndex((prev) => (prev + 1) % editSuggestions.length);
        return;
      }
      if (e.key === "ArrowUp") {
        e.preventDefault();
        setEditHighlightedIndex((prev) => (prev <= 0 ? editSuggestions.length - 1 : prev - 1));
        return;
      }
      if (e.key === "Enter") {
        const selected = editSuggestions[editHighlightedIndex];
        if (selected) {
          e.preventDefault();
          applyEditSuggestion(selected);
          return;
        }
      }
    }

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

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = value.indexOf(String(active.id));
    const newIndex = value.indexOf(String(over.id));
    if (oldIndex === -1 || newIndex === -1) return;
    onValueChange(arrayMove(value, oldIndex, newIndex));
  };

  const chips = value.map((tag, index) =>
    editingIndex === index ? (
      <div key={`${tag}-${index}`} className="relative">
        <input
          type="text"
          autoFocus
          value={editDraft}
          onChange={(e) => {
            setEditDraft(e.target.value);
            setIsEditOpen(true);
            setEditHighlightedIndex(-1);
          }}
          onFocus={() => setIsEditOpen(true)}
          onKeyDown={handleEditKeyDown}
          onBlur={handleEditBlur}
          className="border-input bg-background min-w-24 rounded-md border px-2 py-1 text-base outline-none md:text-sm"
        />
        {isEditOpen && editSuggestions.length > 0 && (
          <SuggestionListbox
            suggestions={editSuggestions}
            highlightedIndex={editHighlightedIndex}
            onHighlight={setEditHighlightedIndex}
            onSelect={applyEditSuggestion}
          />
        )}
      </div>
    ) : (
      <TagChip
        key={tag}
        id={tag}
        tag={tag}
        disabled={disabled}
        reorderable={reorderable}
        onEdit={() => startEditing(index)}
        onRemove={() => removeAt(index)}
      />
    ),
  );

  return (
    <div
      className={cn(
        "border-input bg-background ring-offset-background focus-within:ring-ring flex min-h-10 w-full flex-wrap items-center gap-1.5 rounded-md border px-2 py-1.5 focus-within:ring-2 focus-within:ring-offset-2",
        disabled && "cursor-not-allowed opacity-50",
        className,
      )}
      aria-invalid={props["aria-invalid"]}
    >
      {reorderable ? (
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={value} strategy={horizontalListSortingStrategy}>
            {chips}
          </SortableContext>
        </DndContext>
      ) : (
        chips
      )}
      <div className="relative min-w-24 flex-1">
        <input
          type="text"
          value={draft}
          disabled={disabled}
          aria-label={placeholder}
          onChange={(e) => {
            setDraft(e.target.value);
            setIsDraftOpen(true);
            setHighlightedIndex(-1);
          }}
          onFocus={() => setIsDraftOpen(true)}
          onKeyDown={handleKeyDown}
          onBlur={() => {
            setIsDraftOpen(false);
            setHighlightedIndex(-1);
            commitDraft();
          }}
          placeholder={value.length === 0 ? placeholder : undefined}
          className="placeholder:text-muted-foreground w-full bg-transparent text-base outline-none disabled:cursor-not-allowed md:text-sm"
        />

        {isDraftOpen && draftSuggestions.length > 0 && (
          <SuggestionListbox
            suggestions={draftSuggestions}
            highlightedIndex={highlightedIndex}
            onHighlight={setHighlightedIndex}
            onSelect={applySuggestion}
          />
        )}
      </div>
    </div>
  );
}

interface SuggestionListboxProps {
  suggestions: string[];
  highlightedIndex: number;
  onHighlight: (index: number) => void;
  onSelect: (suggestion: string) => void;
}

// Shared dropdown for both the trailing "add a new entry" draft input and the in-place edit
// input, so typeahead narrowing behaves identically no matter which one is being typed into.
function SuggestionListbox({
  suggestions,
  highlightedIndex,
  onHighlight,
  onSelect,
}: SuggestionListboxProps) {
  return (
    <ul
      role="listbox"
      className="border-border bg-popover text-popover-foreground absolute top-full left-0 z-50 mt-1 max-h-48 w-max min-w-full overflow-y-auto overscroll-contain rounded-md border shadow-md sm:max-h-56"
    >
      {suggestions.map((suggestion, index) => (
        <li
          key={suggestion}
          role="option"
          aria-selected={index === highlightedIndex}
          className={cn(
            "cursor-pointer px-3.5 py-2.5 text-sm whitespace-nowrap transition-colors select-none",
            index === highlightedIndex
              ? "bg-accent text-accent-foreground font-medium"
              : "hover:bg-accent/80 hover:text-accent-foreground text-popover-foreground",
          )}
          onPointerDown={(e) => {
            e.preventDefault();
            onSelect(suggestion);
          }}
          onMouseEnter={() => onHighlight(index)}
        >
          {suggestion}
        </li>
      ))}
    </ul>
  );
}

interface TagChipProps {
  id: string;
  tag: string;
  disabled?: boolean;
  reorderable: boolean;
  onEdit: () => void;
  onRemove: () => void;
}

// A single committed chip. Always registered with useSortable so hook order never depends on
// the `reorderable` prop - dragging itself is disabled (and the grip handle hidden) when the
// field doesn't support reordering (Genres) or is disabled.
function TagChip({ id, tag, disabled, reorderable, onEdit, onRemove }: TagChipProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id,
    disabled: !reorderable || disabled,
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      // dnd-kit computes this per-frame during a drag (live translate offset); it cannot be a
      // static Tailwind class. See DESIGN.md section 5.
      // eslint-disable-next-line no-restricted-syntax
      style={style}
      className={cn(
        badgeVariants({ variant: "secondary" }),
        "gap-1 py-0 pr-1 pl-1 font-normal",
        isDragging && "z-10 opacity-70",
      )}
    >
      {reorderable && !disabled && (
        <button
          type="button"
          {...attributes}
          {...listeners}
          className="hover:bg-secondary-foreground/20 cursor-grab touch-none rounded-full p-1.5 active:cursor-grabbing"
          aria-label={`Reorder ${tag}`}
        >
          <GripVertical className="h-3 w-3" />
        </button>
      )}
      <button
        type="button"
        onClick={onEdit}
        disabled={disabled}
        className="hover:bg-secondary-foreground/20 rounded-full px-1.5 py-1.5 disabled:cursor-not-allowed"
        aria-label={`Edit ${tag}`}
      >
        {tag}
      </button>
      {!disabled && (
        <button
          type="button"
          onClick={onRemove}
          className="hover:bg-secondary-foreground/20 rounded-full p-1.5"
          aria-label={`Remove ${tag}`}
        >
          <X className="h-3 w-3" />
        </button>
      )}
    </div>
  );
}
