import type { components } from "@/lib/api-types";
import type { Require } from "@/lib/dto";
import type BookFileInfo from "./BookFileInfo";

// AudiobookManager.Domain/AudiobookImage.cs: both fields are non-nullable.
export type AudiobookImage = Require<
  components["schemas"]["AudiobookImage"],
  "base64Data" | "mimeType"
>;

// Not generated: this is the frontend's own editing-form model, not a wire DTO. The backend
// saves/loads via flat "/"-joined author/genre strings (see
// helpers/organizeAudiobookInput.ts's splitList/joinList and services/api.ts's toAudiobookDto),
// while this shape is the richer, array-based representation BookEditForm and friends work
// with — services/api.ts converts between the two. Generating it from api-types.ts would just
// reproduce the DTO and lose that transform.
export interface Audiobook {
  authors: AudiobookPerson[];
  narrators: AudiobookPerson[];
  bookName?: string;
  subtitle?: string;
  series?: string;
  seriesPart?: string;
  year?: number;
  genres: string[];
  description?: string;
  copyright?: string;
  publisher?: string;
  language?: string;
  rating?: string;
  asin?: string;
  www?: string;

  cover?: AudiobookImage;

  durationInSeconds?: number;

  fileInfo?: BookFileInfo;
}

// Not generated (see Audiobook above) — also used to construct locally-built author/narrator
// entries (e.g. `{ name }`) before they're flattened into the wire's joined-string form, so it
// deliberately omits the backend Person schema's `id` field, which the frontend never needs.
export interface AudiobookPerson {
  name: string;
  role?: string;
}

export type { Audiobook as default };
