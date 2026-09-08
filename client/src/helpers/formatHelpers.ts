import { format } from "date-fns";

export function formatDuration(seconds: number | null | undefined): string {
  if (!seconds || seconds <= 0) return "0s";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);

  const parts: string[] = [];
  if (h > 0) parts.push(`${h}h`);
  if (m > 0 || h > 0) parts.push(`${m}m`);
  parts.push(`${s}s`);
  return parts.join(" ");
}

export function formatFileSize(bytes: number | null | undefined): string {
  if (!bytes || bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return `${(bytes / Math.pow(1024, i)).toFixed(2)} ${units[i]}`;
}

/**
 * Formats a wire timestamp (ISO 8601 with an explicit offset, UTC on the wire — DESIGN.md
 * section 7) for display the same way everywhere the UI shows one. Displays the full local
 * date-time as `yyyy-MM-dd HH:mm:ss` (24-hour time), matching this project's display
 * convention — see client/src/AGENTS.md. `format` from date-fns deliberately runs in the
 * user's own timezone; the ISO parse is the load-bearing part (new Date("...Z") handles UTC
 * conversion correctly, a naive string would be read as local time).
 */
export function formatDateTime(value: string | null | undefined): string {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return format(date, "yyyy-MM-dd HH:mm:ss");
}

/**
 * Formats a date for display as `yyyy-MM-dd` (see client/src/AGENTS.md). A date-only ISO
 * value is a calendar date, so it is read as local midnight — `new Date("yyyy-MM-dd")`
 * would parse it as UTC midnight and shift the day for timezones off UTC. Values with an
 * explicit offset or a time part keep the timestamp semantics: the instant is converted to
 * the user's local timezone and its calendar date is shown.
 */
export function formatDate(value: string | null | undefined): string {
  if (!value) return "";
  if (/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value)) {
    const year = Number(value.slice(0, 4));
    const month = Number(value.slice(5, 7));
    const day = Number(value.slice(8, 10));
    const local = new Date(year, month - 1, day);
    if (local.getFullYear() !== year || local.getMonth() !== month - 1 || local.getDate() !== day) {
      return "";
    }
    return format(local, "yyyy-MM-dd");
  }
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  return format(date, "yyyy-MM-dd");
}
