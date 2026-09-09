import { useCallback, useMemo, useState } from "react";
import type { ManagedAudiobook } from "@/types/ManagedAudiobook";

export interface SelectedBookInfo {
  id: number;
  title: string;
  authors: string[];
}

export interface BookSelection {
  count: number;
  /** Stable selection order: the order books entered the selection. */
  selectedBooks: SelectedBookInfo[];
  isSelected: (id: number) => boolean;
  toggle: (book: ManagedAudiobook) => void;
  /** Adds every book on the page to the selection, keeping anything already picked. */
  selectPage: (books: ManagedAudiobook[]) => void;
  deselectPage: (books: ManagedAudiobook[]) => void;
  /** Every book on the page is selected (the select-all checkbox's checked state). */
  pageAllSelected: (books: ManagedAudiobook[]) => boolean;
  /** Some but not all books on the page are selected (the indeterminate state). */
  pageSomeSelected: (books: ManagedAudiobook[]) => boolean;
  clear: () => void;
}

function toSelectedInfo(book: ManagedAudiobook): SelectedBookInfo {
  return { id: book.id, title: book.bookName ?? "", authors: book.authors ?? [] };
}

/**
 * Multi-select state for the library book lists. Id-keyed and held in a Map so selection can
 * never be tied to a row's position (rows must never be index-keyed), and deliberately NOT
 * reset by paging or search changes within a view — the user's picks survive until the view
 * decides the entity changed (a different author/series) and calls clear.
 */
export function useBookSelection(): BookSelection {
  const [selected, setSelected] = useState<Map<number, SelectedBookInfo>>(() => new Map());

  const toggle = useCallback((book: ManagedAudiobook) => {
    setSelected((prev) => {
      const next = new Map(prev);
      if (next.has(book.id)) {
        next.delete(book.id);
      } else {
        next.set(book.id, toSelectedInfo(book));
      }
      return next;
    });
  }, []);

  const selectPage = useCallback((books: ManagedAudiobook[]) => {
    setSelected((prev) => {
      const next = new Map(prev);
      for (const book of books) {
        next.set(book.id, toSelectedInfo(book));
      }
      return next;
    });
  }, []);

  const deselectPage = useCallback((books: ManagedAudiobook[]) => {
    setSelected((prev) => {
      const next = new Map(prev);
      for (const book of books) {
        next.delete(book.id);
      }
      return next;
    });
  }, []);

  const clear = useCallback(() => {
    setSelected(new Map());
  }, []);

  return useMemo(
    () => ({
      count: selected.size,
      selectedBooks: Array.from(selected.values()),
      isSelected: (id: number) => selected.has(id),
      toggle,
      selectPage,
      deselectPage,
      pageAllSelected: (books: ManagedAudiobook[]) =>
        books.length > 0 && books.every((b) => selected.has(b.id)),
      pageSomeSelected: (books: ManagedAudiobook[]) =>
        books.some((b) => selected.has(b.id)) && !books.every((b) => selected.has(b.id)),
      clear,
    }),
    [selected, toggle, selectPage, deselectPage, clear],
  );
}
