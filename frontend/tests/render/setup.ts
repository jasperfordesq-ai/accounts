// Global setup for the render harness. Imported by every render test via vitest.config `setupFiles`.
//
// HeroUI v3 is built on React Aria, which (with framer-motion) reaches for a handful of browser APIs
// that jsdom does not implement. Polyfill them here so mounting a real HeroUI Button/Card does not
// throw, and so a React-Aria `usePress` button fires `onPress` under `@testing-library/user-event`.
import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { cleanup } from "@testing-library/react";

afterEach(() => {
  cleanup();
});

// matchMedia — React Aria / framer-motion read it on mount.
if (typeof window.matchMedia !== "function") {
  window.matchMedia = (query: string): MediaQueryList =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList;
}

// ResizeObserver / IntersectionObserver — used by overlay/positioning code paths.
class ObserverStub {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
  takeRecords(): [] {
    return [];
  }
}

if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = ObserverStub as unknown as typeof ResizeObserver;
}
if (typeof globalThis.IntersectionObserver === "undefined") {
  globalThis.IntersectionObserver = ObserverStub as unknown as typeof IntersectionObserver;
}

// Pointer-capture APIs — React Aria `usePress` calls these during a press; jsdom omits them.
if (typeof Element.prototype.hasPointerCapture !== "function") {
  Element.prototype.hasPointerCapture = () => false;
}
if (typeof Element.prototype.setPointerCapture !== "function") {
  Element.prototype.setPointerCapture = () => {};
}
if (typeof Element.prototype.releasePointerCapture !== "function") {
  Element.prototype.releasePointerCapture = () => {};
}
if (typeof Element.prototype.scrollIntoView !== "function") {
  Element.prototype.scrollIntoView = () => {};
}
