import type { Metadata } from "next";
import { headers } from "next/headers";
import Script from "next/script";
import { Providers } from "./providers";
import { AppNavbar } from "@/components/AppNavbar";
import { AppFooter } from "@/components/AppFooter";
import "./globals.css";

export const metadata: Metadata = {
  title: "Irish Accounts - Statutory Accounts Platform",
  description: "Multi-company statutory accounts production for Irish private companies",
  icons: { icon: "/favicon.ico" },
};

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const nonce = (await headers()).get("x-nonce") ?? undefined;

  return (
    <html lang="en" className="h-full antialiased" data-scroll-behavior="smooth" suppressHydrationWarning>
      <head>
        <Script src="/theme-init.js" strategy="beforeInteractive" nonce={nonce} suppressHydrationWarning />
      </head>
      <body className="min-h-full flex flex-col bg-[var(--background)] text-[var(--foreground)] transition-colors">
        <Providers>
          <a
            href="#main-content"
            className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded-lg focus:bg-[var(--accent)] focus:px-4 focus:py-2 focus:text-[var(--accent-foreground)]"
          >
            Skip to main content
          </a>
          <AppNavbar />
          <main id="main-content" className="max-w-7xl mx-auto w-full px-6 py-8 flex-1">
            {children}
          </main>
        </Providers>
        <AppFooter />
      </body>
    </html>
  );
}
