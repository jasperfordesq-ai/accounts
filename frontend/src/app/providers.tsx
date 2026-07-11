"use client";

import { RouterProvider } from "@heroui/react";
import { Toaster } from "sonner";
import { ErrorBoundary } from "@/components/ErrorBoundary";
import { AuthProvider } from "@/components/AuthProvider";
import { UnsavedChangesProvider } from "@/components/UnsavedChangesProvider";
import { useGuardedRouter } from "@/lib/useUnsavedChanges";
import { ClientMonitoringBridge } from "@/components/ClientMonitoringBridge";
import { PrintThemeBridge } from "@/components/PrintThemeBridge";

function ApplicationProviders({ children }: { children: React.ReactNode }) {
  const router = useGuardedRouter();

  return (
    <RouterProvider navigate={(path) => router.push(path)}>
      <ClientMonitoringBridge />
      <PrintThemeBridge />
      <ErrorBoundary>
        <AuthProvider>{children}</AuthProvider>
      </ErrorBoundary>
      <Toaster
        position="bottom-right"
        richColors
        closeButton
        toastOptions={{
          duration: 4000,
          className: "text-sm",
        }}
      />
    </RouterProvider>
  );
}

export function Providers({ children }: { children: React.ReactNode }) {
  return (
    <UnsavedChangesProvider>
      <ApplicationProviders>{children}</ApplicationProviders>
    </UnsavedChangesProvider>
  );
}
