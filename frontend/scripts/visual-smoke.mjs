import { mkdir } from "node:fs/promises";
import path from "node:path";
import { chromium, expect } from "@playwright/test";
import { findOverlappingTextBlocks, formatLayoutIssues } from "./visual-smoke-layout.mjs";
import { visualSmokeRoutes, visualSmokeThemes, visualSmokeViewports } from "./visual-smoke-plan.mjs";

function arg(name, fallback) {
  const prefix = `--${name}=`;
  const value = process.argv.find((item) => item.startsWith(prefix));
  return value ? value.slice(prefix.length) : process.env[name.toUpperCase().replaceAll("-", "_")] ?? fallback;
}

function requiredArg(name) {
  const value = arg(name, "");
  if (!value) throw new Error(`Missing required --${name}=... argument or ${name.toUpperCase().replaceAll("-", "_")} env var.`);
  return value;
}

function normalizeBaseUrl(value) {
  return value.replace(/\/+$/, "");
}

function safeName(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
}

function toAbsoluteUrl(baseUrl, href) {
  return new URL(href, `${baseUrl}/`).toString();
}

function mainText(page, text, options = {}) {
  return page.locator("main").getByText(text, options).first();
}

async function setTheme(context, theme) {
  await context.addInitScript((selectedTheme) => {
    localStorage.setItem("theme", selectedTheme);
    const applyTheme = () => {
      document.documentElement?.classList.toggle("dark", selectedTheme === "dark");
      window.dispatchEvent(new Event("accounts-theme-change"));
    };
    if (document.documentElement) {
      applyTheme();
    } else {
      document.addEventListener("DOMContentLoaded", applyTheme, { once: true });
    }
  }, theme);
}

async function setInputValue(locator, value) {
  await locator.evaluate((input, nextValue) => {
    const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    valueSetter?.call(input, nextValue);
    input.dispatchEvent(new Event("input", { bubbles: true }));
    input.dispatchEvent(new Event("change", { bubbles: true }));
  }, value);
}

async function login(page, baseUrl, email, password) {
  let lastFailure = "";

  for (let attempt = 1; attempt <= 3; attempt += 1) {
    await page.goto(`${baseUrl}/login`, { waitUntil: "domcontentloaded" });
    await page.waitForLoadState("networkidle", { timeout: 10_000 }).catch(() => {});

    const dashboardHeading = mainText(page, "Dashboard", { exact: true });
    if (!new URL(page.url()).pathname.startsWith("/login") || await dashboardHeading.isVisible().catch(() => false)) {
      await expect(dashboardHeading).toBeVisible({ timeout: 30_000 });
      return;
    }

    const emailInput = page.locator('input[type="email"]');
    const passwordInput = page.locator('input[type="password"]');
    await emailInput.waitFor({ state: "visible", timeout: 30_000 });
    await passwordInput.waitFor({ state: "visible", timeout: 30_000 });
    await setInputValue(emailInput, email);
    await setInputValue(passwordInput, password);
    const signInButton = page.getByRole("button", { name: "Sign in" });
    await expect(signInButton).toBeEnabled({ timeout: 30_000 });
    await signInButton.click();

    try {
      await page.waitForURL((url) => !url.pathname.startsWith("/login"), { timeout: 15_000 });
      await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});
      await expect(mainText(page, "Dashboard", { exact: true })).toBeVisible({ timeout: 30_000 });
      return;
    } catch (error) {
      const formText = await page.locator("form").innerText().catch(() => "");
      lastFailure = `${error instanceof Error ? error.message : String(error)}\n${formText}`.trim();
      if (attempt < 3) {
        await page.waitForTimeout(attempt * 1_000);
        continue;
      }
    }
  }

  throw new Error(`Login did not reach the dashboard after 3 attempts.\n${lastFailure}`);
}

async function firstHref(page, selector, label) {
  const href = await page.locator(selector).evaluateAll((links) => {
    const element = links.find((link) => link instanceof HTMLAnchorElement);
    return element instanceof HTMLAnchorElement ? element.getAttribute("href") : null;
  });
  if (!href) throw new Error(`Could not find ${label} link using selector ${selector}.`);
  return href;
}

async function optionalFirstHref(page, selector) {
  return page.locator(selector).evaluateAll((links) => {
    const element = links.find((link) => link instanceof HTMLAnchorElement);
    return element instanceof HTMLAnchorElement ? element.getAttribute("href") : null;
  });
}

async function apiJson(page, pathName, { method = "GET", body } = {}) {
  return page.evaluate(async ({ requestPath, requestMethod, requestBody }) => {
    const headers = new Headers({ "Content-Type": "application/json" });
    const unsafeMethod = !["GET", "HEAD", "OPTIONS", "TRACE"].includes(requestMethod.toUpperCase());

    if (unsafeMethod) {
      const csrfCookie = document.cookie
        .split(";")
        .map((part) => part.trim())
        .find((part) => part.startsWith("accounts_csrf="));
      if (!csrfCookie) throw new Error("Missing accounts_csrf cookie for visual smoke fixture setup.");
      headers.set("X-CSRF-Token", decodeURIComponent(csrfCookie.slice("accounts_csrf=".length)));
    }

    const response = await fetch(requestPath, {
      method: requestMethod,
      headers,
      body: requestBody === undefined ? undefined : JSON.stringify(requestBody),
      credentials: "include",
    });
    const text = await response.text();

    if (!response.ok) {
      throw new Error(`${requestMethod} ${requestPath} failed with ${response.status}: ${text}`);
    }

    return text ? JSON.parse(text) : null;
  }, { requestPath: pathName, requestMethod: method, requestBody: body });
}

async function createSmokeCompany(page) {
  const company = await apiJson(page, "/api/companies", {
    method: "POST",
    body: {
      legalName: "CI Visual Accounts Limited",
      tradingName: "Visual Smoke",
      croNumber: "999999",
      taxReference: "999999T",
      companyType: "Private",
      incorporationDate: "2024-01-01",
      financialYearStartMonth: 1,
      ardMonth: 9,
      registeredOfficeAddress1: "1 Visual Street",
      registeredOfficeCity: "Dublin",
      registeredOfficeCounty: "Dublin",
      registeredOfficeEircode: "D02 X285",
      isGroupMember: false,
      isHolding: false,
      isInvestment: false,
      isSubsidiary: false,
      isDormant: false,
      isTrading: true,
      isVatRegistered: false,
      isEmployer: false,
      hasStock: false,
      ownsAssets: false,
      hasBorrowings: false,
      hasDirectorLoans: false,
      isListedSecurities: false,
      isCreditInstitution: false,
      isInsuranceUndertaking: false,
      isPensionFund: false,
      isCharitableOrganisation: false,
    },
  });

  await apiJson(page, `/api/companies/${company.id}/officers`, {
    method: "POST",
    body: {
      name: "CI Visual Director",
      role: "Director",
      appointedDate: "2024-01-01",
    },
  });

  const period = await apiJson(page, `/api/companies/${company.id}/periods`, {
    method: "POST",
    body: {
      periodStart: "2024-01-01",
      periodEnd: "2024-12-31",
      isFirstYear: true,
      memberAuditNoticeReceived: false,
      goingConcernConfirmed: true,
    },
  });

  return {
    companyHref: `/companies/${company.id}`,
    periodHref: `/companies/${company.id}/periods/${period.id}`,
  };
}

async function discoverRoutes(page, baseUrl) {
  await expect(mainText(page, "Production Readiness")).toBeVisible({ timeout: 30_000 });
  let companyHref = await optionalFirstHref(
    page,
    'a[href^="/companies/"]:not([href="/companies/new"]):not([href*="/periods/"])',
  );
  let periodHref = null;

  if (!companyHref) {
    const smokeCompany = await createSmokeCompany(page);
    companyHref = smokeCompany.companyHref;
    periodHref = smokeCompany.periodHref;
  }

  await page.goto(toAbsoluteUrl(baseUrl, companyHref), { waitUntil: "domcontentloaded" });
  await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});
  await expect(mainText(page, "Accounting Periods")).toBeVisible({ timeout: 30_000 });
  periodHref ??= await firstHref(page, 'a[href*="/periods/"]', "period workspace");

  return {
    dashboard: "/",
    readiness: "/production-readiness",
    company: companyHref,
    period: periodHref,
    filing: periodHref,
  };
}

async function checkNoPageOverflow(page, routeName) {
  const result = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  if (result.scrollWidth > result.clientWidth + 2) {
    throw new Error(`${routeName} has page-level horizontal overflow: ${result.scrollWidth}px > ${result.clientWidth}px.`);
  }
}

async function checkNoTextOverlap(page, routeName) {
  const blocks = await page.evaluate(() => {
    const root = document.querySelector("main");
    if (!root) return [];
    const selectors = [
      "a",
      "button",
      "label",
      "p",
      "span",
      "h1",
      "h2",
      "h3",
      "h4",
      "h5",
      "h6",
      "td",
      "th",
      "li",
      "summary",
      "code",
      "input",
      "textarea",
      "select",
      "[role='tab']",
      "[role='button']",
      "[role='link']",
    ];

    const elements = Array.from(root.querySelectorAll(selectors.join(",")));
    const candidates = elements
      .map((element) => ({ element, text: visibleTextFor(element) }))
      .filter(({ element, text }) => text.length >= 2 && isVisiblyRendered(element));
    const leafCandidates = candidates.filter(({ element }) =>
      !candidates.some(({ element: other }) => other !== element && element.contains(other))
    );

    return leafCandidates.map(({ element, text }, index) => {
      const rect = element.getBoundingClientRect();
      const scrollLeft = window.scrollX || document.documentElement.scrollLeft || 0;
      const scrollTop = window.scrollY || document.documentElement.scrollTop || 0;
      return {
        label: labelFor(element, text, index),
        text,
        rect: {
          left: rect.left + scrollLeft,
          top: rect.top + scrollTop,
          right: rect.right + scrollLeft,
          bottom: rect.bottom + scrollTop,
          width: rect.width,
          height: rect.height,
        },
      };
    });

    function visibleTextFor(element) {
      if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
        return normalizeText(element.value || element.placeholder || "");
      }

      if (element instanceof HTMLSelectElement) {
        return normalizeText(element.selectedOptions[0]?.textContent ?? element.value);
      }

      return normalizeText(element.innerText || element.textContent || "");
    }

    function isVisiblyRendered(element) {
      if (element.closest("[aria-hidden='true'], [hidden]")) return false;
      const style = window.getComputedStyle(element);
      if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity) === 0) return false;
      const rect = element.getBoundingClientRect();
      return rect.width > 1 && rect.height > 1;
    }

    function labelFor(element, text, index) {
      const tag = element.tagName.toLowerCase();
      const id = element.id ? `#${element.id}` : "";
      const role = element.getAttribute("role");
      const roleLabel = role ? `[role="${role}"]` : "";
      return `${tag}${id}${roleLabel} "${previewText(text)}" (${index + 1})`;
    }

    function normalizeText(value) {
      return value.replace(/\s+/g, " ").trim();
    }

    function previewText(value) {
      const normalized = normalizeText(value);
      return normalized.length > 40 ? `${normalized.slice(0, 39)}...` : normalized;
    }
  });
  const issues = findOverlappingTextBlocks(blocks);
  if (issues.length > 0) {
    throw new Error(formatLayoutIssues(routeName, issues));
  }
}

async function captureRoute({ page, routeName, href, expectedText, outputPath, openFilingTab }) {
  const routeErrors = [];
  const onConsole = (message) => {
    const text = message.text();
    const isLocalDevNonceHydrationWarning =
      /^(localhost|127\.0\.0\.1)$/.test(new URL(page.url()).hostname) &&
      text.includes("A tree hydrated but some attributes of the server rendered HTML didn't match") &&
      text.includes("nonce=");

    if (message.type() === "error" && !isLocalDevNonceHydrationWarning) {
      routeErrors.push(`console: ${text}`);
    }
  };
  const onPageError = (error) => routeErrors.push(`pageerror: ${error.message}`);
  page.on("console", onConsole);
  page.on("pageerror", onPageError);

  try {
    await page.goto(href, { waitUntil: "domcontentloaded" });
    await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});

    if (openFilingTab) {
      const filingTab = page.getByRole("tab", { name: "Filing" });
      await expect(filingTab).toBeVisible({ timeout: 30_000 });
      await filingTab.click();
    }

    await expect(mainText(page, expectedText)).toBeVisible({ timeout: 30_000 });
    await checkNoPageOverflow(page, routeName);
    await checkNoTextOverlap(page, routeName);
    await page.screenshot({ path: outputPath, fullPage: true });

    if (routeErrors.length > 0) {
      throw new Error(`${routeName} emitted browser errors:\n${routeErrors.join("\n")}`);
    }
  } finally {
    page.off("console", onConsole);
    page.off("pageerror", onPageError);
  }
}

async function run() {
  const baseUrl = normalizeBaseUrl(requiredArg("base-url"));
  const email = requiredArg("email");
  const password = requiredArg("password");
  const outputDir = path.resolve(arg("output-dir", "artifacts/visual-smoke"));
  const headless = arg("headed", "false") !== "true";

  await mkdir(outputDir, { recursive: true });
  const browser = await chromium.launch({ headless });
  const captures = [];

  try {
    for (const viewport of visualSmokeViewports) {
      for (const theme of visualSmokeThemes) {
        const context = await browser.newContext({
          viewport: { width: viewport.width, height: viewport.height },
          ignoreHTTPSErrors: true,
        });
        await setTheme(context, theme);
        const page = await context.newPage();
        await login(page, baseUrl, email, password);
        const routes = await discoverRoutes(page, baseUrl);

        const routeSpecs = visualSmokeRoutes.map((route) => ({
          ...route,
          href: routes[route.routeKey],
        }));

        for (const spec of routeSpecs) {
          const fileName = `${safeName(spec.name)}-${theme}-${viewport.name}.png`;
          const outputPath = path.join(outputDir, fileName);
          await captureRoute({
            page,
            routeName: `${spec.name}/${theme}/${viewport.name}`,
            href: toAbsoluteUrl(baseUrl, spec.href),
            expectedText: spec.expectedText,
            outputPath,
            openFilingTab: spec.openFilingTab,
          });
          captures.push(outputPath);
        }

        await context.close();
      }
    }
  } finally {
    await browser.close();
  }

  console.log(JSON.stringify({ ok: true, screenshots: captures }, null, 2));
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
