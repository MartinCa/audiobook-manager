import { defineConfig } from "vitest/config";
import { fileURLToPath, URL } from "node:url";
import vue from "@vitejs/plugin-vue";

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    css: true,
    setupFiles: ["./vitest.setup.ts"],
    server: {
      deps: {
        inline: ["vuetify"],
      },
    },
  },
});
