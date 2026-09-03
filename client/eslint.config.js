import config from "@martinrun/frontend-config/eslint";

// scripts/ holds standalone Node build scripts (e.g. generate-api-types.mjs) that aren't part
// of the app's tsconfig project graph, so typed-linting can't run against them.
export default [
  ...config({ ignores: ["scripts"] }),

  {
    // The backend refuses state-changing /api requests that arrive without the
    // `X-Requested-With` header (CrossSiteRequestGuardMiddleware), and `src/lib/api.ts` is the
    // single place that sets it. A component reaching for `fetch` directly therefore doesn't
    // just bypass error handling and the base URL — its writes are rejected outright, and the
    // failure looks like a server bug rather than a missing header. Nothing else enforces that,
    // so the rule does.
    files: ["src/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-globals": [
        "error",
        {
          name: "fetch",
          message:
            "Call the API through src/lib/api.ts instead. It sets the X-Requested-With header the backend requires on writes, resolves the /api base URL, and turns problem+json into ApiError. If you genuinely need a non-JSON response, disable this rule on the line with a reason.",
        },
      ],
    },
  },

  {
    // The module that owns the one real `fetch` call.
    files: ["src/lib/api.ts"],
    rules: {
      "no-restricted-globals": "off",
    },
  },
];
