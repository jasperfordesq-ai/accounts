"use client";

import { useEffect } from "react";
import { reportClientMonitoringEvent } from "@/lib/clientMonitoring";

export function ClientMonitoringBridge() {
  useEffect(() => {
    const report = () => {
      void reportClientMonitoringEvent("unhandled-client-exception");
    };

    window.addEventListener("error", report);
    window.addEventListener("unhandledrejection", report);
    return () => {
      window.removeEventListener("error", report);
      window.removeEventListener("unhandledrejection", report);
    };
  }, []);

  return null;
}
