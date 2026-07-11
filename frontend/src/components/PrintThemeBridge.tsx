"use client";

import { useEffect } from "react";

export function PrintThemeBridge() {
  useEffect(() => {
    const printMedia = window.matchMedia("print");
    let restoreDarkTheme = false;

    const applyLightPrintTheme = () => {
      if (document.documentElement.classList.contains("dark")) {
        restoreDarkTheme = true;
        document.documentElement.classList.remove("dark");
      }
    };
    const restoreTheme = () => {
      if (restoreDarkTheme) {
        document.documentElement.classList.add("dark");
        restoreDarkTheme = false;
      }
    };
    const handlePrintMediaChange = (event: MediaQueryListEvent) => {
      if (event.matches) applyLightPrintTheme();
      else restoreTheme();
    };

    window.addEventListener("beforeprint", applyLightPrintTheme);
    window.addEventListener("afterprint", restoreTheme);
    printMedia.addEventListener("change", handlePrintMediaChange);
    return () => {
      window.removeEventListener("beforeprint", applyLightPrintTheme);
      window.removeEventListener("afterprint", restoreTheme);
      printMedia.removeEventListener("change", handlePrintMediaChange);
      restoreTheme();
    };
  }, []);

  return null;
}
