import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  logoutFailureMessage,
  shouldClearLocalSessionAfterLogout,
} from "../src/lib/logoutSession.ts";

{
  assert.equal(shouldClearLocalSessionAfterLogout(undefined), true);
  assert.equal(shouldClearLocalSessionAfterLogout({ status: 401 }), true);
}

{
  for (const error of [
    { status: 400 },
    { status: 403 },
    { status: 429 },
    { status: 500 },
    { status: 502 },
    new TypeError("fetch failed"),
  ]) {
    assert.equal(shouldClearLocalSessionAfterLogout(error), false);
    assert.equal(
      logoutFailureMessage(error),
      "Sign out did not complete. Your session is still active, so please try again.",
    );
  }
}

{
  const authProvider = readFileSync(new URL("../src/components/AuthProvider.tsx", import.meta.url), "utf8")
    .replace(/\r\n/g, "\n");
  assert.equal(
    authProvider.includes("\n      router.replace(\"/login\");\n    } catch"),
    false,
    "logout success redirects must be guarded by the transition id",
  );

  const appNavbar = readFileSync(new URL("../src/components/AppNavbar.tsx", import.meta.url), "utf8")
    .replace(/\r\n/g, "\n");
  const mobileMenuIndex = appNavbar.indexOf("{/* Mobile menu */}");
  assert.ok(mobileMenuIndex > 0, "navbar should keep the mobile menu marker for this guard");
  const mobileHeader = appNavbar.slice(0, mobileMenuIndex);
  assert.match(mobileHeader, /logoutError && user/);
  assert.match(mobileHeader, /md:hidden/);
  assert.match(mobileHeader, /role="status"/);

  const desktopStatusMatch = appNavbar.match(/className="([^"]*md:block[^"]*)"/);
  assert.ok(desktopStatusMatch, "desktop logout status should be visible from the md breakpoint");
  assert.equal(
    /\blg:block\b/.test(desktopStatusMatch[1]),
    false,
    "desktop logout status must not wait until lg while desktop nav starts at md",
  );
}
