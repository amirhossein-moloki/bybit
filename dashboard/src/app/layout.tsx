import type { Metadata } from "next";
import type { ReactNode } from "react";
import "./globals.css";
import { Providers } from "./providers";

export const metadata: Metadata = {
  title: {
    default: "Trading Bot Dashboard",
    template: "%s · Trading Bot Dashboard",
  },
  description:
    "Operational dashboard for the Telegram Signal Trading Bot — positions, orders, trades, analytics and system health.",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en" className="dark">
      <body className="min-h-screen bg-background font-sans text-foreground">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
