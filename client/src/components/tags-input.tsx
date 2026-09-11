import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { GripVertical, X } from "lucide-react";
import {
  dragAndDrop,
  isDragState,
  parents,
  remapNodes,
  state,
  type ParentData,
  type SortEventData,
} from "@formkit/drag-and-drop";
import { badgeVariants } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { TYPEAHEAD_SUGGESTION_COUNT } from "@/constants/paging";
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
}

// FormKit drag-and-drop is keyed by *value*, not by DOM node: performSort filters the current
// values by `eq()` against the dragged value, and two chips sharing a string would be treated
// as one item (both filtered out, one spliced back). So the controlled `value: string[]` is
// never handed to formkit directly - each entry is wrapped in a `ReorderEntry` whose `key` is
// minted once per entry and never reused. Two chips with the same string still compare
// distinct, and only the actual dragged entry is removed and re-inserted during a sort.
const DRAG_HANDLE_SELECTOR = "[data-drag-handle]";

interface ReorderEntry {
  /** Identity for formkit's deep `eq()` comparison - unique per entry instance, never reused. */
  key: number;
  value: string;
}

let nextEntryKey = 0;

function mintEntry(value: string): ReorderEntry {
  return { key: nextEntryKey++, value };
}

// True while this component's parent element is the origin of an in-flight formkit drag.
// FormKit is a module-level state machine: `state` is its exported current drag record, and
// when a drag is active it points at the initial parent the drag started from. Used to decide
// whether an external `value` change landing mid-drag belongs to a live drag (see the sync
// effect below).
function isDragInFlightFor(parentEl: HTMLElement | null): boolean {
  if (!parentEl) return false;
  return isDragState(state) && state.initialParent.el === parentEl;
}

// Reflect the external `value` onto formkit's internal entries while preserving entry objects
// (by value, in order) so a drag in flight never sees its identities replaced. Edits that
// change/remove/add a string only mint new entries for those positions - the rest keep their
// object identity, which is what formkit's drag tracking relies on.
function remapEntries(existing: ReorderEntry[], values: string[]): ReorderEntry[] {
  const result: ReorderEntry[] = [];
  const used = new Array(existing.length).fill(false);
  for (const value of values) {
    const match = existing.findIndex((entry, index) => !used[index] && entry.value === value);
    if (match === -1) {
      result.push(mintEntry(value));
    } else {
      used[match] = true;
      result.push(existing[match]!);
    }
  }
  return result;
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
  ...props
}: TagsInputProps) {
  const [draft, setDraft] = useState("");
  const [isDraftOpen, setIsDraftOpen] = useState(false);
  const [highlightedIndex, setHighlightedIndex] = useState(-1);
  const [editingIndex, setEditingIndex] = useState<number | null>(null);
  const [editDraft, setEditDraft] = useState("");
  const [isEditOpen, setIsEditOpen] = useState(false);
  const [editHighlightedIndex, setEditHighlightedIndex] = useState(-1);

  // FormKit keeps its own mutable list of entries; this component is controlled (`value` +
  // `onValueChange`), so the entries are held in a ref that both our sync effect and the
  // library's getValues/setValues read/write directly. Using `useDragAndDrop`'s internal state
  // here would fight the external `value`: its setState is only updated when a component
  // re-renders, so an edit/remove that originates *outside* a drag would briefly leave formkit
  // with stale entries - exactly the failure mode where dragging a just-edited chip computes on
  // a value that no longer exists. The ref closes over that window, and `remapNodes` re-derives
  // each node's entry binding immediately after a sync.
  const dndRef = useRef<HTMLDivElement | null>(null);
  const entriesRef = useRef<ReorderEntry[] | null>(null);
  const onValueChangeRef = useRef(onValueChange);
  const valueRef = useRef(value);
  const dragCancelledRef = useRef(false);
  const dndInitRef = useRef(false);

  useEffect(() => {
    valueRef.current = value;
  }, [value]);

  useEffect(() => {
    onValueChangeRef.current = onValueChange;
  }, [onValueChange]);

  const isReorderEnabled = reorderable && !disabled;

  // Cleanup caveat (formkit 0.6.1): there is no way to fully release a parent. `dragAndDrop`
  // attaches a MutationObserver to the parent and registers `document`-level listeners, and
  // neither `tearDown()` nor anything else ever disconnects them - the observer is a local
  // variable inside the library (not stored anywhere public) and the document controller is a
  // module-wide singleton. To keep the observer count bounded, this component calls
  // `dragAndDrop` AT MOST ONCE per mounted element (the dndInitRef guard below) and never
  // re-initializes or tears down afterwards - toggling `reorderable`/`disabled` mutates the
  // parent's live config object instead. A tearDown-on-unmount was deliberately dropped:
  // React 19 StrictMode replays mount effects on the same element, so tearing down would abort
  // the parent's listeners that the replay expects to still be there. One observer per mounted
  // element, attached to a node that becomes unreachable (and therefore garbage) together with
  // it when the component unmounts, is the tightest this library allows.
  //
  // No `useDragAndDrop`/`tearDown`: besides being unreleasable, formkit's `useDragAndDrop` hook
  // holds its own state which a controlled component (`value` + `onValueChange`) would have to
  // mirror back on every render - the refs below keep formkit's entries and the external value
  // in the same synchronous structure instead.
  useEffect(() => {
    const el = dndRef.current;
    if (!el) return;
    if (!dndInitRef.current) {
      // Lazy init on first enable: a TagsInput that is never reorderable (e.g. Genres) carries
      // zero formkit wiring - no parent listeners, no observer - matching the pre-formkit
      // behavior where nothing drag-related was set up. If `disabled` was true at mount,
      // enabling it later initializes here too.
      if (!isReorderEnabled) return;
      dndInitRef.current = true;
      entriesRef.current = remapEntries([], valueRef.current);
      dragAndDrop<ReorderEntry>({
        parent: el,
        getValues: () => entriesRef.current ?? [],
        setValues: (entries) => {
          if (dragCancelledRef.current) return;
          entriesRef.current = entries;
        },
        config: {
          dragHandle: DRAG_HANDLE_SELECTOR,
          disabled: false,
          draggingClass: "opacity-70",
          dragPlaceholderClass: "opacity-70",
          // formkit sorts live on drag-hover and gives us the fully reordered entries, so no
          // local arrayMove step is needed - just hand the new order back up.
          onSort: (data: SortEventData<ReorderEntry>) => {
            if (dragCancelledRef.current) return;
            onValueChangeRef.current(data.values.map((entry) => entry.value));
          },
          // A cancelled drag ends through the normal drop/dragend/pointercancel path; clear the
          // suppression flag there so the next drag starts clean.
          onDragend: () => {
            dragCancelledRef.current = false;
          },
        },
      });
      return;
    }
    // Already initialized once: toggle enabled/disabled without re-initializing formkit.
    // Re-invoking `dragAndDrop()` (which is what the exported `updateConfig` helper would do)
    // tears the parent down and attaches a fresh MutationObserver on every call - the very leak
    // this component must avoid. Updating `parentData.config` in the `parents` WeakMap and
    // remapping nodes is the no-reinit path; `remapNodes` reads the live config immediately, so
    // the new `disabled` value is honored on the very next drag attempt.
    const parentData = parents.get(el) as ParentData<ReorderEntry> | undefined;
    if (!parentData) return;
    parentData.config.disabled = !isReorderEnabled;
    if (!isReorderEnabled && isDragInFlightFor(el)) {
      dragCancelledRef.current = true;
    }
    remapNodes<ReorderEntry>(el);
  }, [isReorderEnabled]);

  // External `value` changes (commit/edit/remove, or a caller setting the prop directly) must
  // reach formkit's entry list. remapEntries preserves object identity where possible, so a
  // drag already in progress keeps its identity; remapNodes then re-binds every node's entry so
  // a subsequent drag cannot compute on a stale index/value pairing.
  //
  // If the value changed while a drag is in flight, that drag's intent is stale: formkit's
  // performSort splices the dragged entry back in BY VALUE, which would resurrect a value that
  // was just removed (or emit an order mixing pre- and post-change entries - e.g. when a new
  // chip was inserted ahead of the one being dragged). The whole in-flight drag is therefore
  // suppressed: setValues refuses the order formkit writes and onSort stays silent, so
  // onValueChange never emits a removed value and the external `value` (already updated by the
  // commit/edit/remove that triggered this effect) is untouched. The suppression is lifted when
  // the drag ends (onDragend) or on the next run of this effect with no drag in flight.
  useEffect(() => {
    const current = entriesRef.current;
    if (current === null) return; // formkit never initialized (reorder never enabled)
    const next = remapEntries(current, value);
    const dragActive = isDragInFlightFor(dndRef.current);
    const changed =
      next.length !== current.length || next.some((entry, index) => current[index] !== entry);
    if (dragActive) {
      if (changed) dragCancelledRef.current = true;
    } else {
      dragCancelledRef.current = false;
    }
    if (!changed) return;
    entriesRef.current = next;
    if (dndRef.current) remapNodes<ReorderEntry>(dndRef.current);
  }, [value]);

  const isDuplicate = (candidate: string, excludeIndex?: number) =>
    value.some((v, i) => i !== excludeIndex && v.toLowerCase() === candidate.toLowerCase());

  const draftSuggestions = useMemo(() => {
    if (suggestions.length === 0) return [];
    const trimmed = draft.trim();
    if (!trimmed) return [];
    const matches = narrowByQuery(suggestions, trimmed, TYPEAHEAD_SUGGESTION_COUNT).filter(
      (s) => !isDuplicate(s),
    );
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
    const matches = narrowByQuery(suggestions, trimmed, TYPEAHEAD_SUGGESTION_COUNT).filter(
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

  // React keys (and formkit's entry identity) are the entry's *position*, never its value.
  // Values are supposed to be unique - isDuplicate rejects a new/edited entry that collides
  // with another - but that guarantee only holds for edits made through this component. A
  // caller that sets `value` directly (BookEditForm's "similar existing value" hint used to do
  // this) could still produce two equal strings; keying chips by value would then give React
  // two elements with the same key and break drag-and-drop. Position is always unique, so it
  // can't have that failure mode.
  const chips = value.map((tag, index) =>
    editingIndex === index ? (
      <div key={index} className="relative">
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
        key={index}
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
      {/* FormKit's parent must contain exactly one draggable element per entry (it needs N nodes
          for N values), so the committed chips get their own flex row instead of sharing the
          outer container with the trailing draft input. The inner row keeps the same wrap, gap
          and alignment the chips had as direct children of the outer container. */}
      <div ref={dndRef} className="flex flex-wrap items-center gap-1.5">
        {chips}
      </div>
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
  tag: string;
  disabled?: boolean;
  reorderable: boolean;
  onEdit: () => void;
  onRemove: () => void;
}

// A single committed chip. The chip element itself is formkit's draggable node (it receives the
// node listeners); only the grip button carries `data-drag-handle` so it is the sole drag
// origin - the handle-selector approach the library documents, not the whole chip. When the
// field doesn't support reordering (Genres) or is disabled, the grip is not rendered and the
// parent config is disabled, so no drag can start. The handle is a native button (focusable,
// implicit role="button") but deliberately claims no keyboard reorder semantics: formkit 0.6.1
// has no keyboard drag plugin, so the description says plainly that this is a pointer
// interaction. The grip is omitted entirely when the field is disabled - there is no
// always-dead aria-disabled control in the tab order.
function TagChip({ tag, disabled, reorderable, onEdit, onRemove }: TagChipProps) {
  return (
    <div
      className={cn(badgeVariants({ variant: "secondary" }), "gap-1 py-0 pr-1 pl-1 font-normal")}
    >
      {reorderable && !disabled && (
        <button
          type="button"
          data-drag-handle
          aria-label={`Reorder: ${tag}`}
          aria-description="Drag this handle with the pointer to change order"
          className="hover:bg-secondary-foreground/20 cursor-grab touch-none rounded-full p-1.5 active:cursor-grabbing"
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
