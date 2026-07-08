import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import AboutPage from "@/app/about/page";
import { AppFooter } from "@/components/AppFooter";

describe("public attribution surfaces", () => {
  it("keeps AGPL attribution and the repository link visible in the global footer", () => {
    render(<AppFooter />);

    expect(screen.getByRole("contentinfo")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Built on Irish Accounts by Jasper Ford" })).toHaveAttribute(
      "href",
      "https://github.com/jasperfordesq-ai/accounts",
    );
    expect(screen.getByText(/Licensed under AGPL v3-or-later/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "GitHub repository" })).toHaveAttribute(
      "href",
      "https://github.com/jasperfordesq-ai/accounts",
    );
    expect(screen.getByRole("link", { name: "About" })).toHaveAttribute("href", "/about");
  });

  it("states the creator, AGPL licence and attribution requirement on the about page", () => {
    render(<AboutPage />);

    expect(screen.getByRole("heading", { name: "About Irish Accounts" })).toBeInTheDocument();
    expect(screen.getByText("Powered by Irish Accounts")).toBeInTheDocument();
    expect(screen.getByText("Created by Jasper Ford")).toBeInTheDocument();
    expect(screen.getByText("Licensed under AGPL v3-or-later")).toBeInTheDocument();
    expect(screen.getByText(/creator, main contributor, and copyright holder/i)).toBeInTheDocument();
    expect(screen.getByText(/must keep a visible attribution path/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "accounts repository" })).toHaveAttribute(
      "href",
      "https://github.com/jasperfordesq-ai/accounts",
    );
  });
});
