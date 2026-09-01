import path from "path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { tanstackRouter } from "@tanstack/router-plugin/vite";

export default defineConfig({
  define: {
    __APP_VERSION__: JSON.stringify(
      process.env.VITE_APP_VERSION || process.env.npm_package_version || "dev",
    ),
    __COMMIT_HASH__: JSON.stringify(process.env.VITE_COMMIT_HASH || ""),
  },
  plugins: [
    tanstackRouter({ target: "react", autoCodeSplitting: true, routesDirectory: "./src/routes" }),
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
    },
  },
  server: {
    port: 3000,
    proxy: {
      "/api": {
        target: "http://localhost:5271",
        changeOrigin: true,
      },
      "/hubs": {
        target: "http://localhost:5271",
        ws: true,
      },
    },
  },
});
