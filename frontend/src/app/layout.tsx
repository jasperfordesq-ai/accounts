import type { Metadata } from "next";
import { Geist } from "next/font/google";
import { Providers } from "./providers";
import { AppNavbar } from "@/components/AppNavbar";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "Irish Accounts — Statutory Accounts Platform",
  description: "Multi-company statutory accounts production for Irish private companies",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className={`${geistSans.variable} h-full antialiased`}>
      <body className="min-h-full flex flex-col bg-[#fafafa]">
        <Providers>
          <AppNavbar />
          <main className="max-w-7xl mx-auto w-full px-6 py-8 flex-1">
            {children}
          </main>
        </Providers>
      </body>
    </html>
  );
}
