/**
 * The only module that knows how to talk to the backend.
 *
 * Rules (DESIGN.md section 7):
 *  - Components and query hooks call through here, never `fetch` directly.
 *  - Requests go to a same-origin `/api` path; the dev server proxies it, and
 *    in production the reverse proxy serves both on one origin. No CORS, no
 *    per-environment base URL in the client bundle.
 *  - Errors arrive as RFC 9457 problem+json and leave here as ApiError.
 */

const BASE_URL = "/api";

/** RFC 9457 problem details. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  /** ASP.NET Core model validation puts field errors here. */
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  readonly status: number;
  readonly problem: ProblemDetails;

  constructor(status: number, problem: ProblemDetails) {
    super(problem.detail ?? problem.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.problem = problem;
  }

  /** Field-level validation messages, flattened for react-hook-form. */
  get fieldErrors(): Record<string, string> {
    const out: Record<string, string> = {};
    for (const [field, messages] of Object.entries(this.problem.errors ?? {})) {
      const first = messages[0];
      if (first !== undefined) out[field] = first;
    }
    return out;
  }

  /** Retrying a 4xx will fail the same way. Used by the shared QueryClient. */
  get isRetryable(): boolean {
    return this.status >= 500 || this.status === 408 || this.status === 429;
  }
}

export interface RequestOptions extends Omit<RequestInit, "body"> {
  /** Serialized as JSON. Omit for GET and DELETE. */
  body?: unknown;
  /** Appended as a query string; undefined and null values are dropped. */
  query?: Record<
    string,
    string | number | boolean | readonly string[] | string[] | undefined | null
  >;
}

function buildUrl(path: string, query?: RequestOptions["query"]): string {
  const url = `${BASE_URL}${path.startsWith("/") ? path : `/${path}`}`;
  if (!query) return url;

  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null) {
      if (Array.isArray(value)) {
        for (const item of value) {
          if (item !== undefined && item !== null) {
            params.append(key, String(item));
          }
        }
      } else {
        params.set(key, String(value));
      }
    }
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

function buildHeaders(headers: HeadersInit | undefined, hasBody: boolean): Headers {
  const merged = new Headers({ Accept: "application/json" });
  if (hasBody) merged.set("Content-Type", "application/json");
  if (headers) {
    new Headers(headers).forEach((value, key) => merged.set(key, value));
  }
  return merged;
}

async function parseBody<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.status === 205) return undefined as unknown as T;
  const text = await response.text();
  if (text === "") return undefined as unknown as T;
  return JSON.parse(text) as T;
}

async function toApiError(response: Response): Promise<ApiError> {
  let problem: ProblemDetails = {};
  try {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      problem = (await response.json()) as ProblemDetails;
    }
  } catch {
    // A body that will not parse is not worth a second failure mode.
  }
  return new ApiError(response.status, {
    title: response.statusText,
    status: response.status,
    ...problem,
  });
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { body, query, headers, ...init } = options;

  const response = await fetch(buildUrl(path, query), {
    ...init,
    headers: buildHeaders(headers, body !== undefined),
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });

  if (!response.ok) throw await toApiError(response);

  return parseBody<T>(response);
}

export const api = {
  get: <T>(path: string, options?: Omit<RequestOptions, "body" | "method">) =>
    request<T>(path, { ...options, method: "GET" }),

  post: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, "body" | "method">) =>
    request<T>(path, { ...options, method: "POST", body }),

  put: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, "body" | "method">) =>
    request<T>(path, { ...options, method: "PUT", body }),

  patch: <T>(path: string, body?: unknown, options?: Omit<RequestOptions, "body" | "method">) =>
    request<T>(path, { ...options, method: "PATCH", body }),

  delete: <T>(path: string, options?: Omit<RequestOptions, "body" | "method">) =>
    request<T>(path, { ...options, method: "DELETE" }),
};

export function handleApiError(error: unknown): {
  message: string;
  status?: number;
  details?: unknown;
} {
  if (error instanceof ApiError) {
    return {
      message: error.message,
      status: error.status,
      details: error.problem,
    };
  }
  if (error instanceof Error) {
    return { message: error.message };
  }
  return { message: "An unexpected error occurred" };
}
