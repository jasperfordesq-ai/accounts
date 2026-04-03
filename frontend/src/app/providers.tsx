"use client";

import { useRouter } from "next/navigation";
import { RouterProvider } from "@heroui/react";
import { Toaster } from "sonner";
import { ErrorBoundary } from "@/components/ErrorBoundary";

export function Providers({ children }: { children: React.ReactNode }) {
  const router = useRouter();

  return (
    <RouterProvider navigate={(path) => router.push(path)}>
      <ErrorBoundary>
        {children}
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
