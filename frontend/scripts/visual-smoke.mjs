import { createHash, createHmac, randomUUID } from "node:crypto";
import { lstat, mkdir, readFile, realpath, unlink, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { chromium, expect } from "@playwright/test";
import { withScreenshotEvidence } from "./visual-smoke-artifacts.mjs";
import { findOverlappingTextBlocks, formatLayoutIssues } from "./visual-smoke-layout.mjs";
import {
  expectedVisualSmokeManifest,
  expectedVisualSmokeRouteAudits,
  canonicalUrlTemplateForState,
  canonicalStateUrlMatches,
  MIN_LARGE_TEXT_CONTRAST_RATIO,
  MIN_NORMAL_TEXT_CONTRAST_RATIO,
  MIN_UI_COMPONENT_CONTRAST_RATIO,
  passedVisualSmokeContrastResult,
  passedVisualSmokeLayoutResults,
  resolveVisualSmokeStateHref,
  visualSmokeLayoutChecks,
  visualSmokeStateInventory,
  visualSmokeThemes,
  visualSmokeViewports,
} from "./visual-smoke-plan.mjs";

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

function loginErrorAlert(page) {
  return page.locator('form [role="alert"]');
}

const TOTP_PERIOD_MS = 30_000;
const LOGIN_UI_STATES = new Set([
  "credentials",
  "mfa-enrollment",
  "mfa-challenge",
  "recovery-codes",
  "authentication-error",
  "unknown",
]);

function safeLoginFailureDiagnostic(error, uiState, sensitiveValues = []) {
  let message = error instanceof Error ? error.message : String(error);
  for (const value of sensitiveValues) {
    if (typeof value === "string" && value.length > 0) {
      message = message.replaceAll(value, "[redacted]");
    }
  }
  message = message.replace(/\b[A-Z2-7]{16,128}\b/g, "[redacted]");
  const safeState = LOGIN_UI_STATES.has(uiState) ? uiState : "unknown";
  return `${message}\nLogin UI state: ${safeState}`.trim();
}

function assertRequiredMfaCompleted(mfaState, mfaSubmitted) {
  if (mfaState.secret && !mfaSubmitted) {
    throw new Error("Privileged visual smoke login bypassed the required fresh MFA challenge.");
  }
}

async function observedLoginUiState(page) {
  if (await page.locator('[aria-label="Authenticator setup key"]').isVisible().catch(() => false)) {
    return "mfa-enrollment";
  }
  if (await page.getByRole("button", { name: "I have stored these codes" }).isVisible().catch(() => false)) {
    return "recovery-codes";
  }
  if (await page.locator('input[autocomplete="one-time-code"]').isVisible().catch(() => false)) {
    return "mfa-challenge";
  }
  if (await loginErrorAlert(page).isVisible().catch(() => false)) {
    return "authentication-error";
  }
  if (await page.locator('input[type="email"]').isVisible().catch(() => false)) {
    return "credentials";
  }
  return "unknown";
}

function decodeBase32Secret(value) {
  const normalized = value.replace(/[\s=-]/g, "").toUpperCase();
  if (!/^[A-Z2-7]{16,128}$/.test(normalized)) {
    throw new Error("The visual smoke MFA handoff contained an invalid Base32 secret.");
  }

  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bytes = [];
  let buffer = 0;
  let bits = 0;
  for (const character of normalized) {
    buffer = (buffer * 32) + alphabet.indexOf(character);
    bits += 5;
    while (bits >= 8) {
      bits -= 8;
      bytes.push(Math.floor(buffer / (2 ** bits)) & 0xff);
      buffer %= 2 ** bits;
    }
  }
  return Buffer.from(bytes);
}

function totpCodeForCounter(secret, counter) {
  if (!Number.isSafeInteger(counter) || counter < 0) {
    throw new Error("The visual smoke MFA counter must be a non-negative safe integer.");
  }
  const key = decodeBase32Secret(secret);
  const counterBytes = Buffer.alloc(8);
  counterBytes.writeBigUInt64BE(BigInt(counter));
  const digest = createHmac("sha1", key).update(counterBytes).digest();
  key.fill(0);
  const offset = digest[digest.length - 1] & 0x0f;
  const binary = ((digest[offset] & 0x7f) << 24)
    | ((digest[offset + 1] & 0xff) << 16)
    | ((digest[offset + 2] & 0xff) << 8)
    | (digest[offset + 3] & 0xff);
  return String(binary % 1_000_000).padStart(6, "0");
}

async function nextFreshTotpCode(mfaState, {
  now = () => Date.now(),
  wait = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds)),
} = {}) {
  if (!mfaState.secret) {
    throw new Error("Privileged visual smoke login requires the disposable runner MFA handoff.");
  }

  let nowMs = now();
  let counter = Math.floor(nowMs / TOTP_PERIOD_MS);
  if (mfaState.lastUsedCounter !== null && counter <= mfaState.lastUsedCounter) {
    const waitMilliseconds = ((mfaState.lastUsedCounter + 1) * TOTP_PERIOD_MS) - nowMs + 250;
    await wait(Math.max(waitMilliseconds, 1));
    nowMs = now();
    counter = Math.floor(nowMs / TOTP_PERIOD_MS);
  }
  if (mfaState.lastUsedCounter !== null && counter <= mfaState.lastUsedCounter) {
    throw new Error("The visual smoke MFA counter did not advance beyond the last accepted code.");
  }

  return { code: totpCodeForCounter(mfaState.secret, counter), counter };
}

function pathIsWithin(candidatePath, rootPath) {
  const relative = path.relative(rootPath, candidatePath);
  return relative === "" || (!relative.startsWith(`..${path.sep}`) && relative !== ".." && !path.isAbsolute(relative));
}

async function consumeEphemeralMfaHandoff(filePath, {
  temporaryRoot = process.env.RUNNER_TEMP || os.tmpdir(),
} = {}) {
  if (!filePath) return { secret: null, lastUsedCounter: null };

  const resolvedRoot = path.resolve(temporaryRoot);
  const resolvedPath = path.resolve(filePath);
  if (!pathIsWithin(resolvedPath, resolvedRoot)) {
    throw new Error("The visual smoke MFA handoff must remain inside runner-temporary storage.");
  }

  let payloadText;
  let safeToUnlink = false;
  try {
    const metadata = await lstat(resolvedPath);
    if (metadata.isSymbolicLink() || !metadata.isFile()) {
      throw new Error("The visual smoke MFA handoff must be a regular file, not a filesystem link.");
    }
    const [canonicalRoot, canonicalPath] = await Promise.all([realpath(resolvedRoot), realpath(resolvedPath)]);
    if (!pathIsWithin(canonicalPath, canonicalRoot)) {
      throw new Error("The visual smoke MFA handoff must not escape runner-temporary storage through a filesystem link.");
    }
    safeToUnlink = true;
    if (metadata.size <= 0 || metadata.size > 1_024) {
      throw new Error("The visual smoke MFA handoff must be a small regular file.");
    }
    if (process.platform !== "win32" && (metadata.mode & 0o077) !== 0) {
      throw new Error("The visual smoke MFA handoff must use mode 0600.");
    }
    payloadText = await readFile(resolvedPath, "utf8");
  } catch (error) {
    if (error?.code === "ENOENT") {
      throw new Error("The required visual smoke MFA handoff file is missing.");
    }
    throw error;
  } finally {
    if (safeToUnlink) {
      await unlink(resolvedPath).catch((error) => {
        if (error?.code !== "ENOENT") throw error;
      });
    }
  }

  const payload = JSON.parse(payloadText);
  if (
    payload?.schemaVersion !== "accounts-visual-mfa-handoff-v1"
    || typeof payload.secret !== "string"
    || !Number.isSafeInteger(payload.lastAcceptedCounter)
    || payload.lastAcceptedCounter < 0
  ) {
    throw new Error("The visual smoke MFA handoff did not match its fail-closed contract.");
  }
  decodeBase32Secret(payload.secret);
  return {
    secret: payload.secret,
    lastUsedCounter: payload.lastAcceptedCounter,
  };
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

async function login(page, baseUrl, email, password, mfaState) {
  let lastFailure = "";

  for (let attempt = 1; attempt <= 3; attempt += 1) {
    let mfaSubmitted = false;
    await page.goto(`${baseUrl}/login`, { waitUntil: "domcontentloaded" });
    await page.waitForLoadState("networkidle", { timeout: 10_000 }).catch(() => {});

    const dashboardHeading = mainText(page, "Firm command centre", { exact: true });
    if (!new URL(page.url()).pathname.startsWith("/login") || await dashboardHeading.isVisible().catch(() => false)) {
      await expect(dashboardHeading).toBeVisible({ timeout: 30_000 });
      assertRequiredMfaCompleted(mfaState, false);
      return;
    }

    const emailInput = page.locator('input[type="email"]');
    const passwordInput = page.locator('input[type="password"]');
    await emailInput.waitFor({ state: "visible", timeout: 30_000 });
    await passwordInput.waitFor({ state: "visible", timeout: 30_000 });
    await emailInput.fill(email);
    await passwordInput.fill(password);
    const signInButton = page.getByRole("button", { name: "Sign in" });
    await expect(signInButton).toBeEnabled({ timeout: 30_000 });
    await signInButton.click();

    try {
      await page.waitForFunction(() => (
        window.location.pathname !== "/login"
        || document.querySelector('input[autocomplete="one-time-code"]')
        || document.querySelector('form [role="alert"]')
      ), undefined, { timeout: 15_000 });

      if (new URL(page.url()).pathname.startsWith("/login")) {
        const alert = loginErrorAlert(page);
        if (await alert.isVisible().catch(() => false)) {
          throw new Error("Login page reported an authentication error.");
        }

        const setupKey = page.locator('[aria-label="Authenticator setup key"]');
        if (await setupKey.isVisible().catch(() => false)) {
          const enrollmentSecret = (await setupKey.innerText()).trim();
          decodeBase32Secret(enrollmentSecret);
          mfaState.secret = enrollmentSecret;
          mfaState.lastUsedCounter = null;
        }

        const credential = await nextFreshTotpCode(mfaState);
        mfaState.lastUsedCounter = credential.counter;
        await page.locator('input[autocomplete="one-time-code"]').fill(credential.code);
        await page.getByRole("button", { name: /Verify and (?:enable MFA|sign in)/ }).click();
        mfaSubmitted = true;
        await page.waitForFunction(() => (
          window.location.pathname !== "/login"
          || Array.from(document.querySelectorAll("button")).some((button) => button.textContent?.includes("I have stored these codes"))
          || document.querySelector('form [role="alert"]')
        ), undefined, { timeout: 15_000 });

        const recoveryContinuation = page.getByRole("button", { name: "I have stored these codes" });
        if (await recoveryContinuation.isVisible().catch(() => false)) {
          await recoveryContinuation.click();
        } else if (new URL(page.url()).pathname.startsWith("/login")) {
          const mfaAlert = loginErrorAlert(page);
          if (await mfaAlert.isVisible().catch(() => false)) {
            throw new Error("Login page reported an MFA verification error.");
          }
        }
      }

      await page.waitForURL((url) => !url.pathname.startsWith("/login"), { timeout: 15_000 });
      await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});
      await expect(mainText(page, "Firm command centre", { exact: true })).toBeVisible({ timeout: 30_000 });
      assertRequiredMfaCompleted(mfaState, mfaSubmitted);
      return;
    } catch (error) {
      const uiState = await observedLoginUiState(page);
      lastFailure = safeLoginFailureDiagnostic(error, uiState, [email, password, mfaState.secret]);
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

function companyHrefFromPeriodHref(href) {
  const match = href?.match(/^\/companies\/([^/]+)\/periods\/[^/]+/);
  return match ? `/companies/${match[1]}` : null;
}

function periodPathFromHref(href, baseUrl) {
  return new URL(href, `${baseUrl}/`).pathname;
}

async function apiJson(page, pathName, { method = "GET", body, idempotencyKey } = {}) {
  return page.evaluate(async ({ requestPath, requestMethod, requestBody, requestIdempotencyKey }) => {
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
    if (requestIdempotencyKey) {
      headers.set("Idempotency-Key", requestIdempotencyKey);
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
  }, {
    requestPath: pathName,
    requestMethod: method,
    requestBody: body,
    requestIdempotencyKey: idempotencyKey,
  });
}

function visualFixtureIdempotencyKey(operation, nonce = randomUUID()) {
  const key = `visual-${operation}-${nonce}`;
  if (!/^[A-Za-z0-9._:-]{8,128}$/.test(key)) {
    throw new Error("Visual smoke fixture idempotency keys must match the API header contract.");
  }
  return key;
}

async function createSmokeCompany(page) {
  const company = await apiJson(page, "/api/companies", {
    method: "POST",
    idempotencyKey: visualFixtureIdempotencyKey("company"),
    body: {
      legalName: "CI Visual Accounts Limited",
      tradingName: "Visual Smoke",
      croNumber: "999999",
      taxReference: "999999T",
      companyType: "Private",
      incorporationDate: "2024-01-01",
      financialYearStartMonth: 1,
      annualReturnDate: "2025-09-30",
      annualReturnDateEffectiveFrom: "2025-09-30",
      annualReturnDateSource: "CroRecord",
      annualReturnDateEvidenceReference: "CI-VISUAL-SMOKE-CRO-ARD-FIXTURE",
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
      isCharitableOrganisation: true,
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
    idempotencyKey: visualFixtureIdempotencyKey("period"),
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

const VISUAL_SMOKE_DASHBOARD_DISCOVERY_TEXT = "Platform release status";

async function discoverRoutes(page, baseUrl) {
  await expect(mainText(page, VISUAL_SMOKE_DASHBOARD_DISCOVERY_TEXT)).toBeVisible({ timeout: 30_000 });
  let periodHref = await optionalFirstHref(page, 'a[href^="/companies/"][href*="/periods/"]');
  let companyHref = companyHrefFromPeriodHref(periodHref);
  companyHref ??= await optionalFirstHref(
    page,
    'a[href^="/companies/"]:not([href="/companies/new"]):not([href*="/periods/"])',
  );

  if (!companyHref) {
    const smokeCompany = await createSmokeCompany(page);
    companyHref = smokeCompany.companyHref;
    periodHref = smokeCompany.periodHref;
  }

  await page.goto(toAbsoluteUrl(baseUrl, companyHref), { waitUntil: "domcontentloaded" });
  await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});
  await expect(mainText(page, "Company command centre")).toBeVisible({ timeout: 30_000 });
  periodHref ??= await firstHref(page, 'a[href*="/periods/"]', "period workspace");
  const periodPath = periodPathFromHref(periodHref, baseUrl);

  return {
    login: "/login",
    changePassword: "/change-password",
    dashboard: "/",
    onboarding: "/companies/new",
    readiness: "/production-readiness",
    company: companyHref,
    period: periodPath,
    filing: periodPath,
    classification: `${periodPath}/classify`,
    yearEnd: `${periodPath}/year-end`,
    notes: `${periodPath}/notes`,
    charity: `${periodPath}/charity`,
    financialStatements: `${periodPath}/statements`,
    workbenchPreview: "/workbench-preview",
  };
}

async function checkNoPageOverflow(page, routeName) {
  const result = await page.evaluate(() => {
    function isContainedByHorizontalScroller(element) {
      let current = element.parentElement;
      while (current && current !== document.body && current !== document.documentElement) {
        const style = window.getComputedStyle(current);
        const clipsHorizontalOverflow = ["auto", "scroll", "hidden", "clip"].includes(style.overflowX);
        if (clipsHorizontalOverflow) {
          const rect = current.getBoundingClientRect();
          if (rect.left >= -2 && rect.right <= document.documentElement.clientWidth + 2) {
            return true;
          }
        }
        current = current.parentElement;
      }

      return false;
    }

    const scrollWidth = document.documentElement.scrollWidth;
    const clientWidth = document.documentElement.clientWidth;
    const overflowingElements = [];

    if (scrollWidth > clientWidth + 2) {
      const viewportWidth = document.documentElement.clientWidth;
      overflowingElements.push(
        ...Array.from(document.querySelectorAll("body *"))
        .map((element) => {
          const rect = element.getBoundingClientRect();
          const overflowsRight = rect.right > viewportWidth + 2;
          const overflowsLeft = rect.left < -2;
          if (!overflowsRight && !overflowsLeft) return null;
          if (isContainedByHorizontalScroller(element)) return null;

          return {
            tag: element.tagName.toLowerCase(),
            className: String(element.getAttribute("class") || "").slice(0, 180),
            text: String(element.textContent || "").replace(/\s+/g, " ").trim().slice(0, 120),
            left: Math.round(rect.left),
            right: Math.round(rect.right),
            width: Math.round(rect.width),
          };
        })
        .filter(Boolean)
        .sort((a, b) => b.right - a.right)
        .slice(0, 8),
      );
    }

    return {
      clientWidth,
      overflowingElements,
      scrollWidth,
    };
  });
  if (result.overflowingElements.length > 0) {
    const diagnostics = result.overflowingElements
      .map(
        (element) =>
          `- <${element.tag}> ${element.width}px [${element.left}, ${element.right}] class="${element.className}" text="${element.text}"`,
      )
      .join("\n");
    throw new Error(
      `${routeName} has page-level horizontal overflow: ${result.scrollWidth}px > ${result.clientWidth}px.` +
        (diagnostics ? `\nOverflowing elements:\n${diagnostics}` : ""),
    );
  }
}

async function checkNoTextOverlap(page, routeName) {
  const blocks = await page.evaluate(() => {
    const root = document.querySelector("main");
    if (!root) return [];
    const blocks = [];
    const scrollLeft = window.scrollX || document.documentElement.scrollLeft || 0;
    const scrollTop = window.scrollY || document.documentElement.scrollTop || 0;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        const text = normalizeText(node.textContent || "");
        if (text.length < 2) return NodeFilter.FILTER_REJECT;

        const element = node.parentElement;
        if (!element || !isVisiblyRendered(element)) return NodeFilter.FILTER_REJECT;
        if (["SCRIPT", "STYLE", "NOSCRIPT", "SVG"].includes(element.tagName)) return NodeFilter.FILTER_REJECT;

        return NodeFilter.FILTER_ACCEPT;
      },
    });

    let index = 0;
    while (walker.nextNode()) {
      const node = walker.currentNode;
      const element = node.parentElement;
      if (!element) continue;

      const text = normalizeText(node.textContent || "");
      const range = document.createRange();
      range.selectNodeContents(node);

      for (const rect of Array.from(range.getClientRects())) {
        if (rect.width <= 1 || rect.height <= 1) continue;
        const clippedRect = clipRectToVisibleBounds(toPageRect(rect), element);
        if (!clippedRect) continue;
        blocks.push({
          label: labelFor(element, text, index),
          text,
          rect: clippedRect,
        });
        index += 1;
      }

      range.detach();
    }

    for (const element of Array.from(root.querySelectorAll("input, textarea, select"))) {
      const text = visibleControlTextFor(element);
      if (text.length < 2 || !isVisiblyRendered(element)) continue;

      const rect = element.getBoundingClientRect();
      const clippedRect = clipRectToVisibleBounds(toPageRect(rect), element);
      if (!clippedRect) continue;
      blocks.push({
        label: labelFor(element, text, index),
        text,
        rect: clippedRect,
      });
      index += 1;
    }

    return blocks;

    function visibleControlTextFor(element) {
      if (element instanceof HTMLInputElement) {
        if (!inputRendersTextValue(element)) return "";
        if (element.type === "password" && element.value) return "••••";
        return normalizeText(element.value || element.placeholder || "");
      }

      if (element instanceof HTMLTextAreaElement) {
        return normalizeText(element.value || element.placeholder || "");
      }

      if (element instanceof HTMLSelectElement) {
        return normalizeText(element.selectedOptions[0]?.textContent ?? element.value);
      }

      return "";
    }

    function inputRendersTextValue(element) {
      return !["checkbox", "radio", "range", "color", "hidden", "file", "image"].includes(element.type);
    }

    function clipRectToVisibleBounds(rect, element) {
      let clippedRect = rect;

      for (let current = element; current && root.contains(current); current = current.parentElement) {
        const style = window.getComputedStyle(current);
        if (clipsOverflow(style)) {
          const bounds = toPageRect(current.getBoundingClientRect());
          clippedRect = intersectRects(clippedRect, bounds);
          if (!clippedRect) return null;
        }

        if (current === root) break;
      }

      return clippedRect;
    }

    function clipsOverflow(style) {
      return [style.overflow, style.overflowX, style.overflowY].some((value) => (
        value === "auto" || value === "scroll" || value === "hidden" || value === "clip"
      ));
    }

    function intersectRects(first, second) {
      const left = Math.max(first.left, second.left);
      const top = Math.max(first.top, second.top);
      const right = Math.min(first.right, second.right);
      const bottom = Math.min(first.bottom, second.bottom);

      if (right <= left || bottom <= top) return null;

      return {
        left,
        top,
        right,
        bottom,
        width: right - left,
        height: bottom - top,
      };
    }

    function toPageRect(rect) {
      return {
        left: rect.left + scrollLeft,
        top: rect.top + scrollTop,
        right: rect.right + scrollLeft,
        bottom: rect.bottom + scrollTop,
        width: rect.width,
        height: rect.height,
      };
    }

    function isVisiblyRendered(element) {
      const closedDetails = element.closest("details:not([open])");
      if (closedDetails && !element.closest("summary")) return false;
      if (element.closest("[aria-hidden='true'], [hidden], [inert], [data-inert='true']")) return false;
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
  const issues = findOverlappingTextBlocks(blocks, { tolerance: 10 });
  if (issues.length > 0) {
    throw new Error(formatLayoutIssues(routeName, issues));
  }
}

async function checkThemeContrast(page, routeName) {
  const result = await page.evaluate((minimums) => {
    const root = document.querySelector("main");
    if (!root) {
      return {
        sampledTextCount: 0,
        sampledUiComponentCount: 0,
        minimumContrastRatio: 0,
        failures: ["missing main element"],
      };
    }

    const textSamples = [];
    const uiSamples = [];
    const colorProbeCanvas = document.createElement("canvas");
    colorProbeCanvas.width = 1;
    colorProbeCanvas.height = 1;
    const colorProbe = colorProbeCanvas.getContext("2d", { willReadFrequently: true });
    const parsedColorCache = new Map();
    const gradientColorCache = new Map();
    const effectiveOpacityCache = new WeakMap();
    const sampledUiBoundaries = new WeakSet();
    const unresolvedSamples = new Set();
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
      acceptNode(node) {
        const text = normalizeText(node.textContent || "");
        if (text.length < 2) return NodeFilter.FILTER_REJECT;

        const element = node.parentElement;
        if (!element || !isVisiblyRendered(element)) return NodeFilter.FILTER_REJECT;
        if (["SCRIPT", "STYLE", "NOSCRIPT", "SVG"].includes(element.tagName)) return NodeFilter.FILTER_REJECT;

        return NodeFilter.FILTER_ACCEPT;
      },
    });

    while (walker.nextNode()) {
      const node = walker.currentNode;
      const element = node.parentElement;
      if (!element) continue;

      const text = normalizeText(node.textContent || "");
      recordTextSample(element, text, "rendered text");
    }

    for (const element of Array.from(root.querySelectorAll("input, textarea, select"))) {
      if (!isVisiblyRendered(element)) continue;
      const text = controlTextFor(element);
      if (text.length < 2) continue;

      const isPlaceholder = (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)
        && !element.value
        && Boolean(element.placeholder);
      const foreground = isPlaceholder
        ? parseCssColor(window.getComputedStyle(element, "::placeholder").color)
        : undefined;
      recordTextSample(element, text, isPlaceholder ? "placeholder" : "form value", foreground);
    }

    const interactiveSelector = [
      "a[href]",
      "button",
      "input",
      "textarea",
      "select",
      "[role='button']",
      "[role='checkbox']",
      "[role='radio']",
      "[role='switch']",
      "[role='tab']",
    ].join(",");
    for (const element of Array.from(root.querySelectorAll(interactiveSelector))) {
      if (!isVisiblyRendered(element)) continue;
      recordUiComponentSample(element);
    }

    const contrastFailures = [...textSamples, ...uiSamples]
      .filter((sample) => sample.ratio < sample.requiredRatio)
      .sort((a, b) => a.ratio - b.ratio)
      .map((sample) => `${sample.label} ratio=${sample.ratio.toFixed(2)} required=${sample.requiredRatio.toFixed(2)} ${sample.detail}`);
    const failures = [...unresolvedSamples, ...contrastFailures].slice(0, 12);

    const allSamples = [...textSamples, ...uiSamples];
    const normalTextSamples = textSamples.filter((sample) => !sample.largeText);
    const largeTextSamples = textSamples.filter((sample) => sample.largeText);

    return {
      sampledTextCount: textSamples.length,
      sampledNormalTextCount: normalTextSamples.length,
      sampledLargeTextCount: largeTextSamples.length,
      sampledInteractiveTextCount: textSamples.filter((sample) => sample.interactive).length,
      sampledPlaceholderCount: textSamples.filter((sample) => sample.source === "placeholder").length,
      sampledUiComponentCount: uiSamples.length,
      sampledGradientTextCount: textSamples.filter((sample) => sample.gradientBackground).length,
      minimumContrastRatio: allSamples.length === 0
        ? 0
        : Number(Math.min(...allSamples.map((sample) => sample.ratio)).toFixed(2)),
      minimumNormalTextContrastRatio: normalTextSamples.length === 0
        ? 0
        : Number(Math.min(...normalTextSamples.map((sample) => sample.ratio)).toFixed(2)),
      minimumLargeTextContrastRatio: largeTextSamples.length === 0
        ? minimums.largeText
        : Number(Math.min(...largeTextSamples.map((sample) => sample.ratio)).toFixed(2)),
      minimumUiComponentContrastRatio: uiSamples.length === 0
        ? 0
        : Number(Math.min(...uiSamples.map((sample) => sample.ratio)).toFixed(2)),
      failures,
    };

    function recordTextSample(element, text, source, suppliedForeground) {
      const style = window.getComputedStyle(element);
      const effectiveOpacity = effectiveOpacityFor(element);
      if (effectiveOpacity <= 0) return;
      if (effectiveOpacity > 0 && effectiveOpacity < 0.9995) {
        recordUnresolvedSample(element, text, source, "partial CSS opacity requires explicit contrast-safe colours");
        return;
      }
      const foreground = suppliedForeground ?? parseCssColor(style.color);
      const backgroundResult = effectiveBackgroundsFor(element);
      if (!foreground) return;
      if (backgroundResult.unresolved) {
        recordUnresolvedSample(element, text, source, backgroundResult.unresolved);
        return;
      }
      if (backgroundResult.colors.length === 0) return;

      const ratios = backgroundResult.colors.map((background) =>
        contrastRatio(composite(foreground, background), background));
      const largeText = isLargeText(style);
      const baseRequiredRatio = largeText
        ? minimums.largeText
        : minimums.normalText;
      const requiredRatio = baseRequiredRatio + (backgroundResult.gradient ? minimums.gradientGuardBand : 0);
      const interactive = Boolean(element.closest("a[href], button, input, textarea, select, [role='button'], [role='tab']"));
      textSamples.push({
        label: labelFor(element, text),
        ratio: Math.min(...ratios),
        requiredRatio,
        largeText,
        interactive,
        source,
        gradientBackground: backgroundResult.gradient,
        detail: `${source} text="${previewText(text)}"`,
      });
    }

    function recordUiComponentSample(element, sourceElement = element, boundaryRequired = requiresVisualBoundary(sourceElement)) {
      const sourceOpacity = effectiveOpacityFor(sourceElement);
      if (sourceOpacity <= 0) return;
      if (sourceOpacity > 0 && sourceOpacity < 0.9995) {
        sampledUiBoundaries.add(element);
        recordUnresolvedSample(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName, "UI component", "partial CSS opacity requires explicit contrast-safe colours");
        return;
      }
      if (sampledUiBoundaries.has(element)) return;

      const style = window.getComputedStyle(element);
      const effectiveOpacity = effectiveOpacityFor(element);
      if (effectiveOpacity <= 0) return;
      if (effectiveOpacity > 0 && effectiveOpacity < 0.9995) {
        sampledUiBoundaries.add(element);
        recordUnresolvedSample(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName, "UI component", "partial CSS opacity requires explicit contrast-safe colours");
        return;
      }
      const outside = effectiveBackgroundsFor(element.parentElement ?? element);
      if (outside.unresolved) {
        sampledUiBoundaries.add(element);
        recordUnresolvedSample(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName, "UI component", outside.unresolved);
        return;
      }
      if (outside.colors.length === 0) return;

      const indicators = [];
      let gradientGuardRequired = outside.gradient;
      const background = parseCssColor(style.backgroundColor);
      const hasBackgroundImage = style.backgroundImage && style.backgroundImage !== "none";
      if ((background && background.a > 0.05) || hasBackgroundImage) {
        const inside = effectiveBackgroundsFor(element);
        if (inside.unresolved) {
          sampledUiBoundaries.add(element);
          recordUnresolvedSample(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName, "UI component", inside.unresolved);
          return;
        }
        if (inside.colors.length > 0) {
          gradientGuardRequired ||= inside.gradient;
          indicators.push(Math.min(...inside.colors.flatMap((insideColor) =>
            outside.colors.map((outsideColor) => contrastRatio(insideColor, outsideColor)))));
        }
      }

      const borderRatios = ["Top", "Right", "Bottom", "Left"].flatMap((side) => {
        const width = Number.parseFloat(style[`border${side}Width`]) || 0;
        const borderStyle = style[`border${side}Style`];
        const border = parseCssColor(style[`border${side}Color`]);
        if (width <= 0 || borderStyle === "none" || borderStyle === "hidden" || !border || border.a <= 0.05) {
          return [];
        }
        return [Math.min(...outside.colors.map((color) =>
          contrastRatio(composite(border, color), color)))];
      });
      if (borderRatios.length > 0) {
        indicators.push(Math.max(...borderRatios));
      }

      if (indicators.length === 0) {
        if (element === sourceElement && boundaryRequired) {
          const compositeBoundary = compositeBoundaryFor(sourceElement);
          if (compositeBoundary) {
            recordUiComponentSample(compositeBoundary, sourceElement, true);
            return;
          }
        }

        if (boundaryRequired) {
          sampledUiBoundaries.add(element);
          recordUnresolvedSample(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName, "UI component", "interactive control has no machine-verifiable visual boundary");
        }
        return;
      }

      sampledUiBoundaries.add(element);
      uiSamples.push({
        label: labelFor(sourceElement, sourceElement.getAttribute("aria-label") || sourceElement.textContent || sourceElement.tagName),
        ratio: Math.max(...indicators),
        requiredRatio: minimums.uiComponent + (gradientGuardRequired ? minimums.gradientGuardBand : 0),
        detail: `UI component${sourceElement.matches(":disabled, [aria-disabled='true']") ? " (disabled)" : ""}${element === sourceElement ? "" : " (composite boundary)"}`,
      });
    }

    function requiresVisualBoundary(element) {
      if (element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) return true;
      if (element instanceof HTMLInputElement) {
        return !["hidden", "button", "submit", "reset", "image", "checkbox", "radio", "range", "color"].includes(element.type);
      }
      return element.matches("[role='checkbox'], [role='radio'], [role='switch']");
    }

    function compositeBoundaryFor(element) {
      const genericBoundary = element.closest("[data-contrast-boundary]");
      if (genericBoundary && genericBoundary !== element && root.contains(genericBoundary)) {
        const boundaryControls = Array.from(genericBoundary.querySelectorAll([
          "input",
          "textarea",
          "select",
          "[role='checkbox']",
          "[role='radio']",
          "[role='switch']",
        ].join(","))).filter((candidate) => requiresVisualBoundary(candidate));
        if (boundaryControls.length === 1 && boundaryControls[0] === element) return genericBoundary;
      }

      const knownBoundary = element.closest([
        "[data-slot='input-group']",
        "[data-slot='search-field-group']",
        "[data-slot='number-field-group']",
        "[data-slot='date-input-group']",
        "[data-slot='color-input-group']",
        "[data-slot='combo-box-input-group']",
        "[data-slot='input-otp-group']",
      ].join(","));
      return knownBoundary && knownBoundary !== element && root.contains(knownBoundary) ? knownBoundary : null;
    }

    function effectiveBackgroundsFor(element) {
      let backgrounds = [{ r: 255, g: 255, b: 255, a: 1 }];
      let gradient = false;
      const chain = [];
      for (let current = element; current; current = current.parentElement) {
        chain.push(current);
        if (current === document.documentElement) break;
      }

      for (const current of chain.reverse()) {
        const style = window.getComputedStyle(current);
        const color = parseCssColor(style.backgroundColor);
        if (color && color.a > 0) {
          backgrounds = backgrounds.map((background) => composite(color, background));
        }

        if (style.backgroundImage && style.backgroundImage !== "none") {
          const gradientResult = gradientColors(style.backgroundImage);
          if (gradientResult.unresolved) {
            return { colors: [], gradient: true, unresolved: gradientResult.unresolved };
          }
          gradient = true;
          const combinedBackgrounds = gradientResult.colors
            .flatMap((stop) => backgrounds.map((background) => composite(stop, background)));
          if (combinedBackgrounds.length > 512) {
            return { colors: [], gradient: true, unresolved: "gradient background combinations exceed the machine-verification budget" };
          }
          backgrounds = combinedBackgrounds;
        }
      }

      return { colors: backgrounds, gradient, unresolved: null };
    }

    function gradientColors(value) {
      const candidate = String(value);
      if (gradientColorCache.has(candidate)) return gradientColorCache.get(candidate);
      const resolved = (result) => {
        gradientColorCache.set(candidate, result);
        return result;
      };
      if (!/gradient\(/i.test(candidate)) {
        return resolved({ colors: [], unresolved: "unsupported non-gradient background image" });
      }
      if (/url\(/i.test(candidate)) {
        return resolved({ colors: [], unresolved: "image-backed background requires retained human visual review" });
      }
      if (splitTopLevelLayers(candidate).length !== 1) {
        return resolved({ colors: [], unresolved: "layered gradients require retained human visual review" });
      }
      if (hasUnsupportedGradientInterpolation(candidate)) {
        return resolved({ colors: [], unresolved: "non-sRGB gradient interpolation is not machine-verifiable" });
      }

      const tokens = extractCssColorFunctions(candidate);
      const stops = tokens.map(parseCssColor);
      if (tokens.length < 2 || stops.some((stop) => !stop)) {
        return resolved({ colors: [], unresolved: "unresolved gradient colour stop" });
      }
      const usesOnlyLegacyRgbStops = tokens.every((token) => /^rgba?\(/i.test(token));
      if (!usesOnlyLegacyRgbStops && !hasExplicitSrgbGradientInterpolation(candidate)) {
        return resolved({ colors: [], unresolved: "implicit modern-colour gradient interpolation is not machine-verifiable" });
      }
      if (stops.some((stop) => stop.a < 1)) {
        return resolved({ colors: [], unresolved: "translucent gradient stops require retained human visual review" });
      }
      if (stops.length !== 2) {
        return resolved({ colors: [], unresolved: "multi-stop gradients require retained human visual review" });
      }

      const maximumSamples = 257;
      const colors = [stops[0]];
      const segmentCount = stops.length - 1;
      const availableInteriorSamples = maximumSamples - stops.length;
      const baseInteriorSamples = Math.floor(availableInteriorSamples / segmentCount);
      const extraInteriorSamples = availableInteriorSamples % segmentCount;
      for (let index = 0; index < stops.length - 1; index += 1) {
        const interiorSamples = baseInteriorSamples + (index < extraInteriorSamples ? 1 : 0);
        for (let step = 1; step <= interiorSamples; step += 1) {
          colors.push(interpolateColor(stops[index], stops[index + 1], step / (interiorSamples + 1)));
        }
        colors.push(stops[index + 1]);
      }
      return resolved({ colors, unresolved: null });
    }

    function splitTopLevelLayers(value) {
      const layers = [];
      let depth = 0;
      let start = 0;
      for (let index = 0; index < value.length; index += 1) {
        if (value[index] === "(") depth += 1;
        if (value[index] === ")") depth = Math.max(0, depth - 1);
        if (value[index] === "," && depth === 0) {
          layers.push(value.slice(start, index).trim());
          start = index + 1;
        }
      }
      layers.push(value.slice(start).trim());
      return layers.filter(Boolean);
    }

    function hasUnsupportedGradientInterpolation(value) {
      return gradientInterpolationHeaders(value)
        .some((header) => /\bin\s+(?!srgb(?:\s|$))/i.test(header));
    }

    function hasExplicitSrgbGradientInterpolation(value) {
      return gradientInterpolationHeaders(value)
        .some((header) => /\bin\s+srgb(?:\s|$)/i.test(header));
    }

    function gradientInterpolationHeaders(value) {
      const gradientPattern = /(?:repeating-)?(?:linear|radial|conic)-gradient\(/gi;
      const headers = [];
      for (const match of value.matchAll(gradientPattern)) {
        let depth = 1;
        let header = "";
        for (let index = match.index + match[0].length; index < value.length; index += 1) {
          const character = value[index];
          if (character === "(") depth += 1;
          if (character === ")") depth -= 1;
          if (character === "," && depth === 1) break;
          if (depth === 1) header += character;
          if (depth === 0) break;
        }
        headers.push(header);
      }
      return headers;
    }

    function extractCssColorFunctions(value) {
      const colorFunctions = new Set(["rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix"]);
      const tokens = [];
      for (let index = 0; index < value.length; index += 1) {
        const match = value.slice(index).match(/^([a-z-]+)\(/i);
        if (!match || !colorFunctions.has(match[1].toLowerCase())) continue;
        let depth = 0;
        let end = index;
        for (; end < value.length; end += 1) {
          if (value[end] === "(") depth += 1;
          if (value[end] === ")") {
            depth -= 1;
            if (depth === 0) {
              end += 1;
              break;
            }
          }
        }
        if (depth !== 0) return [];
        tokens.push(value.slice(index, end));
        index = end - 1;
      }
      return tokens;
    }

    function interpolateColor(first, second, position) {
      const interpolate = (start, end) => start + ((end - start) * position);
      return {
        r: Math.round(interpolate(first.r, second.r)),
        g: Math.round(interpolate(first.g, second.g)),
        b: Math.round(interpolate(first.b, second.b)),
        a: Math.max(0, Math.min(1, interpolate(first.a, second.a))),
      };
    }

    function composite(foreground, background) {
      if (foreground.a >= 1) return { r: foreground.r, g: foreground.g, b: foreground.b, a: 1 };
      const alpha = foreground.a;
      return {
        r: Math.round(foreground.r * alpha + background.r * (1 - alpha)),
        g: Math.round(foreground.g * alpha + background.g * (1 - alpha)),
        b: Math.round(foreground.b * alpha + background.b * (1 - alpha)),
        a: 1,
      };
    }

    function contrastRatio(first, second) {
      const lighter = Math.max(relativeLuminance(first), relativeLuminance(second));
      const darker = Math.min(relativeLuminance(first), relativeLuminance(second));
      return (lighter + 0.05) / (darker + 0.05);
    }

    function relativeLuminance(color) {
      const channel = (value) => {
        const normalized = value / 255;
        return normalized <= 0.03928
          ? normalized / 12.92
          : ((normalized + 0.055) / 1.055) ** 2.4;
      };
      return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
    }

    function parseCssColor(value) {
      const candidate = String(value).trim();
      if (parsedColorCache.has(candidate)) return parsedColorCache.get(candidate);
      if (!candidate || !colorProbe || !CSS.supports("color", candidate)) {
        parsedColorCache.set(candidate, null);
        return null;
      }

      colorProbe.clearRect(0, 0, 1, 1);
      colorProbe.fillStyle = "rgba(0, 0, 0, 0)";
      colorProbe.fillStyle = candidate;
      colorProbe.fillRect(0, 0, 1, 1);
      const [r, g, b, alpha] = colorProbe.getImageData(0, 0, 1, 1).data;
      const parsed = { r, g, b, a: alpha / 255 };
      parsedColorCache.set(candidate, parsed);
      return parsed;
    }

    function recordUnresolvedSample(element, text, source, reason) {
      unresolvedSamples.add(`${labelFor(element, text)} unresolved ${source} background: ${reason}`);
    }

    function isLargeText(style) {
      const size = Number.parseFloat(style.fontSize) || 0;
      const numericWeight = Number.parseInt(style.fontWeight, 10);
      const bold = style.fontWeight === "bold" || (!Number.isNaN(numericWeight) && numericWeight >= 700);
      return size >= 24 || (bold && size >= 18.66);
    }

    function isVisiblyRendered(element) {
      const closedDetails = element.closest("details:not([open])");
      if (closedDetails && !element.closest("summary")) return false;
      if (element.closest("[aria-hidden='true'], [hidden], [inert], [data-inert='true']")) return false;
      const style = window.getComputedStyle(element);
      if (style.display === "none" || style.visibility === "hidden" || Number(style.opacity) === 0) return false;
      const rect = element.getBoundingClientRect();
      return rect.width > 1 && rect.height > 1;
    }

    function effectiveOpacityFor(element) {
      if (!element) return 1;
      if (effectiveOpacityCache.has(element)) return effectiveOpacityCache.get(element);
      const parsedOpacity = Number.parseFloat(window.getComputedStyle(element).opacity);
      const ownOpacity = Number.isFinite(parsedOpacity) ? parsedOpacity : 1;
      const opacity = ownOpacity * effectiveOpacityFor(element.parentElement);
      effectiveOpacityCache.set(element, opacity);
      return opacity;
    }

    function controlTextFor(element) {
      if (element instanceof HTMLInputElement) {
        if (!inputRendersTextValue(element)) return "";
        if (element.type === "password" && element.value) return "••••";
        return normalizeText(element.value || element.placeholder || "");
      }

      if (element instanceof HTMLTextAreaElement) {
        return normalizeText(element.value || element.placeholder || "");
      }

      if (element instanceof HTMLSelectElement) {
        return normalizeText(element.selectedOptions[0]?.textContent ?? element.value);
      }

      return "";
    }

    function inputRendersTextValue(element) {
      return !["checkbox", "radio", "range", "color", "hidden", "file", "image"].includes(element.type);
    }

    function labelFor(element, text) {
      const tag = element.tagName.toLowerCase();
      const id = element.id ? `#${element.id}` : "";
      const role = element.getAttribute("role");
      const roleLabel = role ? `[role="${role}"]` : "";
      return `${tag}${id}${roleLabel} "${previewText(text)}"`;
    }

    function normalizeText(value) {
      return value.replace(/\s+/g, " ").trim();
    }

    function previewText(value) {
      const normalized = normalizeText(value);
      return normalized.length > 40 ? `${normalized.slice(0, 39)}...` : normalized;
    }
  }, {
    normalText: MIN_NORMAL_TEXT_CONTRAST_RATIO,
    largeText: MIN_LARGE_TEXT_CONTRAST_RATIO,
    uiComponent: MIN_UI_COMPONENT_CONTRAST_RATIO,
    gradientGuardBand: 0.05,
  });

  if (result.failures.length > 0) {
    throw new Error(`${routeName} has low-contrast visible text:\n${result.failures.join("\n")}`);
  }

  if (result.sampledTextCount <= 0) {
    throw new Error(`${routeName} had no visible text samples for theme contrast smoke evidence.`);
  }

  return passedVisualSmokeContrastResult({
    sampledTextCount: result.sampledTextCount,
    sampledNormalTextCount: result.sampledNormalTextCount,
    sampledLargeTextCount: result.sampledLargeTextCount,
    sampledInteractiveTextCount: result.sampledInteractiveTextCount,
    sampledPlaceholderCount: result.sampledPlaceholderCount,
    sampledUiComponentCount: result.sampledUiComponentCount,
    sampledGradientTextCount: result.sampledGradientTextCount,
    minimumContrastRatio: result.minimumContrastRatio,
    minimumNormalTextContrastRatio: result.minimumNormalTextContrastRatio,
    minimumLargeTextContrastRatio: result.minimumLargeTextContrastRatio,
    minimumUiComponentContrastRatio: result.minimumUiComponentContrastRatio,
  });
}

async function settleFiniteAnimations(page) {
  await page.evaluate(() => {
    for (const animation of document.getAnimations()) {
      const endTime = animation.effect?.getComputedTiming().endTime;
      if (typeof endTime !== "number" || !Number.isFinite(endTime)) continue;
      try {
        animation.finish();
      } catch {
        // A cancelled or not-yet-playable transition has no retained visual state to settle.
      }
    }
  });
  await page.evaluate(() => new Promise((resolve) => {
    requestAnimationFrame(() => requestAnimationFrame(resolve));
  }));
}

async function captureRoute({ page, state, routeName, href, expectedText, expectedStateText, outputPath }) {
  const consoleErrors = [];
  const pageErrors = [];
  const failedResponses = [];
  const onConsole = (message) => {
    const text = message.text();
    const isLocalDevNonceHydrationWarning =
      /^(localhost|127\.0\.0\.1)$/.test(new URL(page.url()).hostname) &&
      text.includes("A tree hydrated but some attributes of the server rendered HTML didn't match") &&
      text.includes("nonce=");

    if (message.type() === "error" && !isLocalDevNonceHydrationWarning) {
      consoleErrors.push({
        type: message.type(),
        text,
        location: message.location(),
      });
    }
  };
  const onPageError = (error) => pageErrors.push(error.message);
  const onResponse = (response) => {
    if (response.status() >= 400) {
      failedResponses.push({
        url: response.url(),
        status: response.status(),
        method: response.request().method(),
      });
    }
  };
  page.on("console", onConsole);
  page.on("pageerror", onPageError);
  page.on("response", onResponse);

  try {
    await page.goto(href, { waitUntil: "domcontentloaded" });
    await page.waitForLoadState("networkidle", { timeout: 30_000 }).catch(() => {});

    const observedUrl = relativePageUrl(page);
    if (!canonicalStateUrlMatches(observedUrl, state)) {
      throw new Error(
        `${routeName} did not retain its canonical URL state: expected ${state.canonicalPathTemplate} ` +
        `${JSON.stringify(state.canonicalQuery)}, found ${observedUrl}`,
      );
    }

    await expect(mainText(page, expectedText)).toBeVisible({ timeout: 30_000 });
    if (expectedStateText !== expectedText) {
      await expect(mainText(page, expectedStateText)).toBeVisible({ timeout: 30_000 });
    }
    await settleFiniteAnimations(page);
    const observedTabState = await observeCanonicalTabState(page, state.canonicalTabState, routeName);
    await checkNoPageOverflow(page, routeName);
    await checkNoTextOverlap(page, routeName);
    const themeContrastResult = await checkThemeContrast(page, routeName);
    const semanticContentEvidence = await readSemanticContentEvidence(page);
    await page.screenshot({ path: outputPath, fullPage: true });

    const routeErrors = unexpectedVisualSmokeBrowserErrors({
      state,
      consoleErrors,
      pageErrors,
      failedResponses,
      pageUrl: page.url(),
    });

    if (routeErrors.length > 0) {
      throw new Error(`${routeName} emitted browser errors:\n${routeErrors.join("\n")}`);
    }

    return {
      layoutCheckResults: passedVisualSmokeLayoutResults(),
      themeContrastResult,
      observedUrl,
      observedTabState,
      ...semanticContentEvidence,
    };
  } finally {
    page.off("console", onConsole);
    page.off("pageerror", onPageError);
    page.off("response", onResponse);
  }
}

function isExpectedAnonymousSessionProbeConsoleError({ state, message, failedResponses, pageUrl }) {
  return expectedAnonymousSessionProbeResponseIndex({
    state,
    message,
    failedResponses,
    pageUrl,
  }) >= 0;
}

function expectedAnonymousSessionProbeResponseIndex({ state, message, failedResponses, pageUrl }) {
  if (
    state?.id !== "login"
    || state?.authMode !== "anonymous"
    || message?.type !== "error"
    || !/^Failed to load resource: the server responded with a status of 401 \([^)]*\)$/.test(message?.text ?? "")
  ) {
    return -1;
  }

  try {
    const pageLocation = new URL(pageUrl);
    const resourceLocation = new URL(message.location?.url ?? "");
    if (
      pageLocation.pathname !== "/login"
      || pageLocation.search !== ""
      || pageLocation.hash !== ""
      || resourceLocation.origin !== pageLocation.origin
      || resourceLocation.pathname !== "/api/auth/me"
      || resourceLocation.search !== ""
      || resourceLocation.hash !== ""
    ) {
      return -1;
    }

    return failedResponses.findIndex((response) => {
      if (response.status !== 401 || response.method !== "GET") {
        return false;
      }
      try {
        return new URL(response.url).href === resourceLocation.href;
      } catch {
        return false;
      }
    });
  } catch {
    return -1;
  }
}

function unexpectedVisualSmokeBrowserErrors({ state, consoleErrors, pageErrors, failedResponses, pageUrl }) {
  const unmatchedResponses = [...failedResponses];
  const errors = pageErrors.map((message) => `pageerror: ${message}`);

  for (const message of consoleErrors) {
    const responseIndex = expectedAnonymousSessionProbeResponseIndex({
      state,
      message,
      failedResponses: unmatchedResponses,
      pageUrl,
    });
    if (responseIndex >= 0) {
      unmatchedResponses.splice(responseIndex, 1);
    } else {
      const responseRoute = safeVisualSmokeResponseRoute(message.location?.url);
      errors.push(`console: ${message.text}${responseRoute ? ` at ${responseRoute}` : ""}`);
    }
  }

  for (const response of unmatchedResponses) {
    errors.push(
      `response: ${String(response.method || "UNKNOWN").toUpperCase()} ` +
      `${safeVisualSmokeResponseRoute(response.url) || "/{redacted}"} returned ${response.status}`,
    );
  }

  return errors;
}

function safeVisualSmokeResponseRoute(rawUrl) {
  try {
    const url = new URL(String(rawUrl || ""));
    if (!url.pathname.startsWith("/api/") && !url.pathname.startsWith("/_next/")) {
      return "/{redacted}";
    }

    const segments = url.pathname.split("/").filter(Boolean).map((segment) => {
      const decoded = decodeURIComponent(segment);
      if (/^\d+$/.test(decoded)) return "{id}";
      if (/^[a-z]+(?:[.-][a-z]+)*$/.test(decoded)) return decoded;
      return "{redacted}";
    });
    return segments.length === 0 ? "/" : `/${segments.join("/")}`;
  } catch {
    return "";
  }
}

async function run() {
  const baseUrl = normalizeBaseUrl(requiredArg("base-url"));
  const email = requiredArg("email");
  const password = requiredArg("password");
  const outputDir = path.resolve(arg("output-dir", "artifacts/visual-smoke"));
  const headless = arg("headed", "false") !== "true";
  const defaultMfaHandoffPath = process.env.RUNNER_TEMP
    ? path.join(process.env.RUNNER_TEMP, "accounts-visual-auth", "totp-handoff.json")
    : "";
  const mfaState = await consumeEphemeralMfaHandoff(arg("mfa-handoff-file", defaultMfaHandoffPath));

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

        const anonymousRouteBases = {
          login: "/login",
        };
        for (const state of visualSmokeStateInventory.filter((item) => item.authMode === "anonymous")) {
          await captureState({
            page,
            state,
            routeBases: anonymousRouteBases,
            baseUrl,
            outputDir,
            theme,
            viewport,
            captures,
          });
        }

        await login(page, baseUrl, email, password, mfaState);
        const routeBases = await discoverRoutes(page, baseUrl);

        for (const state of visualSmokeStateInventory.filter((item) => item.authMode === "authenticated")) {
          await captureState({
            page,
            state,
            routeBases,
            baseUrl,
            outputDir,
            theme,
            viewport,
            captures,
          });
        }

        await context.close();
      }
    }
  } finally {
    await browser.close();
  }

  const manifestTemplate = expectedVisualSmokeManifest(outputDir);
  const manifest = {
    ...manifestTemplate,
    generatedAt: new Date().toISOString(),
    reviewProtocol: manifestTemplate.reviewProtocol,
    routeAudits: expectedVisualSmokeRouteAudits().map((audit) => ({
      ...audit,
      screenshotCount: captures.filter((capture) => capture.stateId === audit.stateId).length,
    })),
    screenshots: captures,
  };
  const manifestFileName = "visual-smoke-manifest.json";
  const manifestPath = path.join(outputDir, manifestFileName);
  await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");

  console.log(JSON.stringify({ ok: true, manifestPath, screenshots: captures }, null, 2));
}

async function captureState({ page, state, routeBases, baseUrl, outputDir, theme, viewport, captures }) {
  const canonicalUrl = resolveVisualSmokeStateHref(state, routeBases);
  const fileName = `${safeName(state.id)}-${theme}-${viewport.name}.png`;
  const outputPath = path.join(outputDir, fileName);
  const smokeCheckResults = await captureRoute({
    page,
    state,
    routeName: `${state.id}/${theme}/${viewport.name}`,
    href: toAbsoluteUrl(baseUrl, canonicalUrl),
    expectedText: state.expectedText,
    expectedStateText: state.expectedStateText,
    outputPath,
  });
  captures.push(await withScreenshotEvidence({
    stateId: state.id,
    routeName: state.name,
    routeKey: state.routeKey,
    materialRoute: state.materialRoute,
    uiState: state.uiState,
    authMode: state.authMode,
    theme,
    viewportName: viewport.name,
    fileName,
    artifactPath: outputPath,
    expectedText: state.expectedText,
    expectedStateText: state.expectedStateText,
    canonicalUrlTemplate: canonicalUrlTemplateForState(state),
    canonicalUrl,
    observedUrl: smokeCheckResults.observedUrl,
    canonicalQuery: state.canonicalQuery,
    canonicalTabState: state.canonicalTabState,
    observedTabState: smokeCheckResults.observedTabState,
    semanticContentSha256: smokeCheckResults.semanticContentSha256,
    semanticContentByteSize: smokeCheckResults.semanticContentByteSize,
    openFilingTab: false,
    reviewStatus: state.reviewStatus,
    layoutChecks: visualSmokeLayoutChecks,
    layoutCheckResults: smokeCheckResults.layoutCheckResults,
    themeContrastResult: smokeCheckResults.themeContrastResult,
  }));
}

async function observeCanonicalTabState(page, canonicalTabState, routeName) {
  if (!canonicalTabState?.kind?.endsWith("-tab")) return null;

  const selectedTab = page.getByRole("tab", { name: canonicalTabState.label, exact: true });
  await expect(selectedTab).toBeVisible({ timeout: 30_000 });
  if (await selectedTab.getAttribute("aria-selected") !== "true") {
    throw new Error(`${routeName} did not select canonical ${canonicalTabState.kind} ${canonicalTabState.id}.`);
  }

  return {
    kind: canonicalTabState.kind,
    id: canonicalTabState.id,
    label: (await selectedTab.innerText()).replace(/\s+/g, " ").trim(),
  };
}

async function readSemanticContentEvidence(page) {
  const snapshot = await page.locator("main").evaluate((main) => {
    const normalize = (value) => String(value ?? "").replace(/\s+/g, " ").trim();
    const selectedTabs = Array.from(main.querySelectorAll('[role="tab"][aria-selected="true"]'))
      .map((element) => normalize(element.textContent))
      .filter(Boolean);
    const headings = Array.from(main.querySelectorAll("h1, h2, h3"))
      .map((element) => normalize(element.textContent))
      .filter(Boolean);
    const controlNames = Array.from(main.querySelectorAll("button, a[href], input, select, textarea"))
      .map((element) => normalize(
        element.getAttribute("aria-label")
        || element.getAttribute("placeholder")
        || element.textContent
        || element.tagName,
      ))
      .filter(Boolean);

    return {
      mainText: normalize(main.innerText),
      selectedTabs,
      headings,
      controlNames,
    };
  });
  const serialized = JSON.stringify(snapshot);
  return {
    semanticContentSha256: `sha256:${createHash("sha256").update(serialized).digest("hex")}`,
    semanticContentByteSize: Buffer.byteLength(serialized, "utf8"),
  };
}

function relativePageUrl(page) {
  const url = new URL(page.url());
  return `${url.pathname}${url.search}`;
}

export {
  assertRequiredMfaCompleted,
  checkThemeContrast,
  companyHrefFromPeriodHref,
  consumeEphemeralMfaHandoff,
  isExpectedAnonymousSessionProbeConsoleError,
  nextFreshTotpCode,
  periodPathFromHref,
  resolveVisualSmokeStateHref,
  safeLoginFailureDiagnostic,
  safeVisualSmokeResponseRoute,
  VISUAL_SMOKE_DASHBOARD_DISCOVERY_TEXT,
  unexpectedVisualSmokeBrowserErrors,
  totpCodeForCounter,
  visualFixtureIdempotencyKey,
};

const invokedAsScript = process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;

if (invokedAsScript) {
  run().catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
}
