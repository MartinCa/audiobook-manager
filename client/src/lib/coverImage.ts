/**
 * Client-side cover preparation.
 *
 * The server validates and normalizes every cover it is given (CoverImageProcessor), so this is
 * not a correctness measure — it is about not sending a 12 MP phone photo through a JSON request
 * body as base64 in the first place. Shrinking here keeps the upload small; the server still has
 * the final say on what is stored.
 *
 * Every failure path falls back to the original bytes rather than blocking the user: a browser
 * that cannot decode the image in a canvas is not a reason to refuse a cover the server would
 * have accepted.
 */

/** Longest edge to send. Matches the server's cap, so a shrunk cover arrives already conforming. */
export const COVER_MAX_DIMENSION = 1500;

/** Below this, sending the original is cheaper than re-encoding it. */
export const COVER_MAX_BYTES = 2 * 1024 * 1024;

const SHRUNK_MIME_TYPE = "image/jpeg";
const SHRUNK_QUALITY = 0.85;

/**
 * How long to wait for the browser to decode the image before giving up and sending the original.
 *
 * An <img> load is not guaranteed to settle: it can neither fire load nor error, and without a
 * bound the user's upload would sit there with no cover set and nothing to explain why.
 */
const DEFAULT_DECODE_TIMEOUT_MS = 5000;

export interface PreparedCover {
  base64Data: string;
  mimeType: string;
}

/**
 * Whether an image is worth re-encoding before upload: too many pixels, or too many bytes.
 */
export function shouldShrink(width: number, height: number, byteLength: number): boolean {
  return (
    width > COVER_MAX_DIMENSION || height > COVER_MAX_DIMENSION || byteLength > COVER_MAX_BYTES
  );
}

/**
 * The size to draw at, preserving aspect ratio. Never enlarges — an image already inside the cap
 * is drawn at its own size.
 */
export function fitWithinCap(width: number, height: number): { width: number; height: number } {
  const scale = Math.min(COVER_MAX_DIMENSION / width, COVER_MAX_DIMENSION / height, 1);
  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

function toBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onloadend = () => {
      if (typeof reader.result !== "string") {
        reject(new Error("Failed to read image as a data URL"));
        return;
      }
      const base64Index = reader.result.indexOf(";base64,");
      resolve(base64Index !== -1 ? reader.result.substring(base64Index + 8) : reader.result);
    };
    reader.onerror = () => reject(reader.error ?? new Error("FileReader error"));
    reader.readAsDataURL(blob);
  });
}

function loadImage(objectUrl: string, timeoutMs: number): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    const timer = setTimeout(() => {
      reject(new Error("Timed out decoding the image"));
    }, timeoutMs);

    image.onload = () => {
      clearTimeout(timer);
      resolve(image);
    };
    image.onerror = () => {
      clearTimeout(timer);
      reject(new Error("The image could not be decoded"));
    };
    image.src = objectUrl;
  });
}

async function shrink(blob: Blob, decodeTimeoutMs: number): Promise<Blob | null> {
  const objectUrl = URL.createObjectURL(blob);
  try {
    const image = await loadImage(objectUrl, decodeTimeoutMs);
    if (!shouldShrink(image.naturalWidth, image.naturalHeight, blob.size)) return null;

    const { width, height } = fitWithinCap(image.naturalWidth, image.naturalHeight);
    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;

    const context = canvas.getContext("2d");
    if (!context) return null;
    context.drawImage(image, 0, 0, width, height);

    const shrunk = await new Promise<Blob | null>((resolve) => {
      canvas.toBlob(resolve, SHRUNK_MIME_TYPE, SHRUNK_QUALITY);
    });

    // Only worth sending if it actually came out smaller. Re-encoding a well-compressed JPEG at
    // its own size can produce a larger file, and sending that would be strictly worse.
    return shrunk && shrunk.size < blob.size ? shrunk : null;
  } finally {
    URL.revokeObjectURL(objectUrl);
  }
}

/**
 * Turns a file or fetched blob into the base64 payload to send, shrinking it first when that
 * helps. Returns the original bytes unchanged whenever shrinking is unnecessary or unavailable.
 */
export async function prepareCover(
  blob: Blob,
  { decodeTimeoutMs = DEFAULT_DECODE_TIMEOUT_MS }: { decodeTimeoutMs?: number } = {},
): Promise<PreparedCover> {
  const original: PreparedCover = {
    base64Data: await toBase64(blob),
    mimeType: blob.type || "image/jpeg",
  };

  try {
    const shrunk = await shrink(blob, decodeTimeoutMs);
    if (!shrunk) return original;

    return { base64Data: await toBase64(shrunk), mimeType: SHRUNK_MIME_TYPE };
  } catch {
    // The server normalizes anyway; a browser that cannot decode this is not a reason to stop.
    return original;
  }
}
