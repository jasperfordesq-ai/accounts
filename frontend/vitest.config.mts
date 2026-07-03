import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Render/component harness for the money-entry surfaces (frontend-render-harness).
// jsdom + @testing-library/react so a rendered form's POST (path / method / payload / CSRF) can be
// asserted. Kept separate from `next build`/`tsc` — it only runs under `npm run test:render`.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/render/setup.ts"],
    include: ["tests/render/**/*.test.{ts,tsx}"],
    css: false,
    restoreMocks: true,
    testTimeout: 10000,
  },
});
