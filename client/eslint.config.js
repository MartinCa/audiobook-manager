import config from "@martinrun/frontend-config/eslint";

// scripts/ holds standalone Node build scripts (e.g. generate-api-types.mjs) that aren't part
// of the app's tsconfig project graph, so typed-linting can't run against them.
export default config({ ignores: ["scripts"] });
