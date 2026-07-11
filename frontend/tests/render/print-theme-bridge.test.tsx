import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PrintThemeBridge } from "@/components/PrintThemeBridge";

describe("PrintThemeBridge", () => {
  afterEach(() => {
    cleanup();
    document.documentElement.classList.remove("dark");
    vi.unstubAllGlobals();
  });

  it("prints with the light palette and restores dark mode after repeated print events", () => {
    const mediaListeners = new Set<(event: MediaQueryListEvent) => void>();
    vi.stubGlobal("matchMedia", vi.fn(() => ({
      matches: false,
      media: "print",
      onchange: null,
      addEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => mediaListeners.add(listener),
      removeEventListener: (_type: string, listener: (event: MediaQueryListEvent) => void) => mediaListeners.delete(listener),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })));
    document.documentElement.classList.add("dark");

    const view = render(<PrintThemeBridge />);
    window.dispatchEvent(new Event("beforeprint"));
    window.dispatchEvent(new Event("beforeprint"));
    expect(document.documentElement).not.toHaveClass("dark");

    window.dispatchEvent(new Event("afterprint"));
    expect(document.documentElement).toHaveClass("dark");

    mediaListeners.forEach((listener) => listener({ matches: true } as MediaQueryListEvent));
    expect(document.documentElement).not.toHaveClass("dark");
    mediaListeners.forEach((listener) => listener({ matches: false } as MediaQueryListEvent));
    expect(document.documentElement).toHaveClass("dark");

    window.dispatchEvent(new Event("beforeprint"));
    view.unmount();
    expect(document.documentElement).toHaveClass("dark");
  });
});
