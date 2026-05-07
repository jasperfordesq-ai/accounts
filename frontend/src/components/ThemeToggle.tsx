"use client";

import { useSyncExternalStore } from "react";
import { Button } from "@heroui/react";
import { Moon, Sun } from "lucide-react";

function subscribe(callback: () => void) {
  window.addEventListener("storage", callback);
  window.addEventListener("accounts-theme-change", callback);
  return () => {
    window.removeEventListener("storage", callback);
    window.removeEventListener("accounts-theme-change", callback);
  };
}

function getSnapshot() {
  return document.documentElement.classList.contains("dark") ? "dark" : "light";
}

export function ThemeToggle() {
  const theme = useSyncExternalStore(subscribe, getSnapshot, () => "light");
  const dark = theme === "dark";

  function toggle() {
    const next = !dark;
    document.documentElement.classList.toggle("dark", next);
    localStorage.setItem("theme", next ? "dark" : "light");
    window.dispatchEvent(new Event("accounts-theme-change"));
  }

  return (
    <Button
      variant="ghost"
      size="sm"
      isIconOnly
      onPress={toggle}
      aria-label={dark ? "Switch to light mode" : "Switch to dark mode"}
    >
      {dark ? <Sun className="w-4 h-4" /> : <Moon className="w-4 h-4" />}
    </Button>
  );
}
