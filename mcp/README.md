# filingbridge-mcp

The FilingBridge connector: a [Model Context Protocol](https://modelcontextprotocol.io) server that lets an
AI client (Claude Code, Codex, Claude Desktop) prepare Irish statutory accounts in FilingBridge **as the
person who connected it**, within their role and company assignments, at the access level and data scope
they chose when they signed in.

FilingBridge is created by Jasper Ford with hOUR Timebank CLG (Ireland) as contributor, and licensed
AGPL-3.0-or-later; this package is part of the same work.

> CRO and ROS final submission remain external actions recorded in workflow state. Nothing in this
> connector, and no route in FilingBridge, files anything with the CRO, Revenue or the Charities Regulator.

## Install

Node 24 is required (`engines: >=24 <25`). From the repository root:

```bash
npm ci --prefix mcp
npm run build --prefix mcp
```

The connector is `mcp/dist/cli.js` (bin name `filingbridge-mcp`). Never run `npm install --prefix mcp`
from the repository root: npm treats that as installing the root package into `mcp`. To change
dependencies, `cd mcp && npm install`.

The server must have the connector enabled (`Connector__Enabled=true` with a `connector_signing_key`); see
`Docs/architecture/mcp-connector.md`.

## Connect (once, yourself, in a terminal)

```bash
node mcp/dist/cli.js connect --profile private --base-url http://127.0.0.1:3500 \
  --tenant-slug <workspace> --email you@example.ie --access-level write
```

It prints the FilingBridge address first, then asks for your password (not shown) and, for Owner,
Accountant and Reviewer accounts, your authenticator code (or pass `--totp-code`, or `--recovery-code`).
The connector credential is stored in the OS keychain (service `filingbridge-mcp`, one entry per
FilingBridge origin) and is bound to that origin: it is never sent anywhere else, whatever later edits the
AI client's configuration.

| Option | Meaning |
| --- | --- |
| `--access-level read\|write\|admin` | What the agent may do. `read` changes nothing; `write` can do ordinary year-end work; `admin` is needed for anything FilingBridge asks a person to approve, user administration, privacy and operations. Default `write` (`admin` on `--profile local`). Fixed for the life of the connection. |
| `--data-scope withheld\|full` | Whether personal data reaches the agent. Default `withheld`; `full` is Owner only. See *Personal data* below. |
| `--device-label <name>` | How this machine appears in FilingBridge's list of connected agents. |

The connection lasts until you revoke it: 15-minute access tokens are renewed automatically from a
single-use refresh token (30 days idle, 90 days at most). Signing out of the browser does not disconnect
agents; changing your password, being offboarded or deactivated, or an Owner revoking your sessions does.

Other commands: `status` (who the stored credential acts as, and the level and scope FilingBridge holds),
`disconnect` (revoke in FilingBridge and forget the credential), `approve <id>` (below), `help`, `version`.

### Profiles

A profile pins which hosts a sign-in may go to, before any password is typed.

| Profile | Reaches |
| --- | --- |
| `local` | A Development stack on this machine (`http://localhost:3000` by default). The only profile allowing `--password-stdin` and `FILINGBRIDGE_CREDENTIAL_FILE` (test harnesses). |
| `private` | A Private Server: its loopback frontend (`http://127.0.0.1:3500`) or `https://<host>.<tailnet>.ts.net` through Tailscale Serve. |
| `default` | Any `https` host (Public Production behind its HTTPS ingress). |

The base URL is the FilingBridge **frontend origin**; every call goes to `<base>/api/...` through the
Next.js proxy. `FILINGBRIDGE_BASE_URL` may stand in for `--base-url`.

## Add it to an AI client

The server speaks MCP over stdio: `serve` is the default command.

**Claude Code**

```bash
claude mcp add filingbridge -- node /abs/path/to/accounts/mcp/dist/cli.js serve \
  --profile private --base-url https://<host>.<tailnet>.ts.net --download-dir /abs/path/to/filingbridge-downloads
```

**Codex** (`~/.codex/config.toml`)

```toml
[mcp_servers.filingbridge]
command = "node"
args = ["/abs/path/to/accounts/mcp/dist/cli.js", "serve", "--profile", "private", "--base-url", "https://<host>.<tailnet>.ts.net"]
```

**Claude Desktop** (`claude_desktop_config.json`)

```json
{
  "mcpServers": {
    "filingbridge": {
      "command": "node",
      "args": ["C:\\path\\to\\accounts\\mcp\\dist\\cli.js", "serve", "--profile", "private", "--base-url", "http://127.0.0.1:3500"]
    }
  }
}
```

A one-click Claude Desktop bundle arrives in phase 3. For Claude.ai, or to connect without running
anything on your machine, use the remote connector below.

## Remote connector (Claude.ai, Claude Code, Codex)

The stack runs this package as the `mcp` service (`filingbridge-mcp serve --http`, image
`Dockerfile.mcp`), reached only through the frontend at **`https://<your FilingBridge host>/mcp`**. A
remote client signs in with FilingBridge's own OAuth 2.1 server (dynamic client registration, PKCE S256,
public clients): it opens `https://<host>/oauth/authorize`, where you type your workspace, email,
password and authenticator code and choose the access level and data scope, exactly as `connect` asks
in a terminal. The access token it receives is an ordinary connector session: the same level, data
scope, per-action approvals, change budget and revocation as the stdio connector.

- **Claude.ai**: Settings → Connectors → Add custom connector, URL `https://<host>/mcp`. Claude.ai
  discovers the authorization server from the 401 and opens the sign-in page. (The host must be
  reachable from the internet over HTTPS for Claude.ai; a Private Server on a tailnet is not.)
- **Claude Code**: `claude mcp add --transport http filingbridge https://<host>/mcp`, then run `/mcp` in
  Claude Code and choose *Authenticate*; your browser opens the sign-in page and Claude Code receives
  the token on a loopback redirect. For a Development stack: `http://localhost:3000/mcp`.
- **Codex**: in `~/.codex/config.toml`

  ```toml
  [mcp_servers.filingbridge]
  url = "https://<host>/mcp"
  ```

  then `codex mcp login filingbridge` to complete the OAuth sign-in in your browser. (Codex releases
  that predate built-in remote MCP support also need `experimental_use_rmcp_client = true` at the top of
  `config.toml`.)

Over HTTP the connector cannot read or write your disk, so files travel in the conversation:

- **Downloads** (accounts PDFs, filing packs, iXBRL, CT1 worksheet …) come back inside the tool result
  as a base64 embedded resource, up to **1 MiB**; anything larger is refused with a pointer to the
  stdio connector. The GDPR subject access export (`privacy_access_export`) is not offered at all over
  HTTP, because its contents must never enter the conversation.
- **The CSV bank import** takes `contentBase64` (and an optional `fileName`); evidence attachments take
  the file's base64 in place of a path. The request as a whole is capped at **5 MiB**, so the base64
  string can be at most 5 MiB (a CSV of roughly 3.7 MB).
- `--upload-root` and `--download-dir` do not exist in HTTP mode, and the tool schemas and descriptions
  say so.

Running the service yourself (it is normally the compose `mcp` service):

```bash
FILINGBRIDGE_API_URL=http://api:8080 \
FILINGBRIDGE_API_KEY_FILE=/run/secrets/accounts_mcp_api_key \
FILINGBRIDGE_PUBLIC_URL=https://fb.example \
node dist/cli.js serve --http --host 0.0.0.0 --port 3100
```

| Variable | Meaning |
|---|---|
| `FILINGBRIDGE_API_URL` | The API, called directly (default `http://api:8080`). |
| `FILINGBRIDGE_API_KEY_FILE` | File holding the service key, sent as `X-Accounts-Api-Key`; read once, never logged. The API holds only its SHA-256 (`ApiAccess:Keys:1`, "MCP service"). |
| `FILINGBRIDGE_PUBLIC_URL` | The OAuth issuer: the frontend origin people use (`Connector:PublicBaseUrl`). The MCP resource is `<origin>/mcp`. https unless loopback. |
| `FILINGBRIDGE_ALLOWED_HOSTS` | Comma-separated `Host` values accepted on `/mcp` and the metadata routes (DNS-rebinding protection). Default: the public host, `mcp:<port>`, `localhost`, `localhost:<port>`, `127.0.0.1:<port>`. |

Routes: `GET /healthz` (open), `POST /mcp` (Streamable HTTP, stateless, JSON responses; `GET`/`DELETE`
answer 405 once authenticated), `GET /.well-known/oauth-protected-resource[/mcp]` (RFC 9728, fetched from
the API and cached five minutes). Without a bearer `/mcp` answers 401 with
`WWW-Authenticate: Bearer resource_metadata="<origin>/.well-known/oauth-protected-resource/mcp"`; a bearer
FilingBridge refuses, or one past its own expiry, answers 401 `error="invalid_token"`, and so does a tool
call during which FilingBridge refuses the bearer, so the client refreshes rather than seeing a tool error.
Each request gets its own MCP server around its own bearer; the session's posture is asked of FilingBridge
and cached for at most 60 seconds per token (never past the token's expiry), and the period versions a
caller has read are kept for that caller's bearer only, so its later writes carry `If-Match`. No
inbound header but the bearer is forwarded: a browser's `Origin`, `Referer`, `Cookie` and `Sec-Fetch-*`
never reach the API. Bodies over 5 MiB answer 413. One JSON line per request goes to stderr (method, path,
status, time, JSON-RPC method and tool name; never a token, argument or result).

Serve options: `--download-dir <dir>` enables downloads (accounts PDFs, CRO filing pack, signature page,
iXBRL, CT1 worksheet, charity PDFs, evidence, handoff snapshots, subject access exports), which are written
there owner-only, never overwritten, and returned to the agent only as `{path, bytes, sha256, contentType,
filename}`. `--upload-root <dir>` enables the CSV bank import (≤ 5 MB, `.csv`/`.txt`) and evidence
attachments (signed approval pages, audit reports, review letters, authority evidence), read only from
inside that directory (no `..`, no symlink escapes, no dot-directories). Both may also come from
`FILINGBRIDGE_DOWNLOAD_DIR` / `FILINGBRIDGE_UPLOAD_ROOT`. `--toolsets a,b` narrows the tools offered;
`--verbose` logs one line per tool call (name, outcome, time; never arguments or results) to stderr.

## Approvals

Removals and irreversible or privileged steps (every delete; finalising or reopening a period; approving
adjustments; confirming year-end reviews; recording directors' approval, signing packs, auditor reports,
external validation, filed dates; chart adoption; historic anchors; user administration; privacy and
operations runs; every external-filing-handoff record) need a person's approval for that exact request.

1. The agent calls the tool. FilingBridge refuses with `APPROVAL_REQUIRED` and an `approvalId`; the stdio
   connector hands the agent the command for this connection, `filingbridge-mcp approve <id> --profile
   <profile> --base-url <origin>`, naming the profile and address it was started with (the remote connector
   passes on FilingBridge's own command). Where the web application is deployed with the remote connector,
   the response also links the approvals page (`/settings/approvals?id=<id>`), where you can grant it instead.
2. You run `node mcp/dist/cli.js approve <id> --profile ... --base-url ...` **in your own terminal**. It shows
   the target host and FilingBridge's own description of the action, then asks for your password and your
   authenticator code. It refuses to run without a terminal and never accepts a piped password, so an agent
   cannot approve its own actions.
3. The agent repeats the identical call with `approvalId`. The approval is single use, expires after a few
   minutes, and is bound to that session, method, path and body.

The agent is told never to run the command itself and never to ask for your password.

## Personal data

With `--data-scope withheld` (the default) the connector removes, from every result including `fetch` and
resource reads: officer addresses; the registered office address; who quarantined a company and why; user
emails and display names; director-loan counterparties, terms, review notes and reviewers; related-party
names and terms; audit-log actors, request ids and before/after values; CRO officer and shareholder
particulars; the reviewer named on the company list and on notes. Director-loan blockers and warnings
(which FilingBridge begins with the counterparty's name) come back labelled `Arrangement #<id>` instead, and
the text of the director-transactions (section 307) and related-party notes is replaced by a withheld
marker in `note_list`, `note_generate` and `fetch`; every other note is returned as written. It is an
allowlist per model, so a new column is withheld until someone classifies it, and a free-text field must be
withheld or rewritten. Writes that would set any of those fields are refused before anything
is sent, and FilingBridge refuses them too. Updates that replace a whole record containing withheld fields
(`company_update`, `officer_update`, `director_loan_update`) need full scope, because sending them blind
would erase what the agent cannot see. File downloads, the directors' report data, the section 307 note,
the external filing handoff, user administration and privacy work need full scope outright.

The scope is chosen at `connect` and held by FilingBridge; the AI client's configuration cannot widen it.

## Tools

233 route tools plus three always-on core tools (`session_info`, `search`, `fetch`), grouped into toolsets:

| Toolset | Tools | Toolset | Tools |
| --- | --- | --- | --- |
| companies | 10 | revenue | 12 |
| officers | 4 | deadlines | 7 |
| periods | 4 | charity | 16 |
| banking | 5 | filing | 19 |
| transactions | 6 | handoff | 7 |
| categories | 8 | admin_users | 10 |
| classification | 11 | privacy | 5 |
| year_end | 45 | operations | 3 |
| disclosures | 20 | system | 2 |
| adjustments | 7 | connector | 5 |
| audit | 4 | core | 3 |
| statements | 7 | | |
| working_papers | 7 | | |
| documents | 9 | | |

Names are `<entity>_<verb>` (`debtor_create`, `period_status_set`, `cro_filing_status_set`). Every
operation in `backend/Accounts.Api/OpenApi/accounts-api-v1.json` is a tool, a core route, or an exclusion
with a written reason in `src/route-coverage.ts` (browser sign-in routes, health probes, monitoring hooks,
and the connector's own credential routes — `approve` above all is never a tool).

`search` and `fetch` follow the shape Claude.ai's research mode expects: `search {query, types?,
companyId?, periodId?, limit?}` returns `{results: [{id: "filingbridge://...", title, url}]}`, and `fetch
{id}` returns `{id, title, text, url, metadata}` by running the matching read tool, so every gate applies.

Resources: `filingbridge://about`, `filingbridge://legal-framework` (Companies Act 2014 thresholds),
`filingbridge://filing-boundary`, and templates `filingbridge://company/{companyId}[/period/{periodId}[/readiness|/filing-status]]`.
Prompts: `year_end_walkthrough`, `categorise_uncategorised`, `pre_filing_check`, `classify_company_size`,
`bank_import_reconcile`, `director_loan_compliance_review`, `charity_sorp_review`, `deadline_review`.

## Troubleshooting

| Symptom | Meaning |
| --- | --- |
| `... has no connector sign-in route` | The server runs an older build, or `Connector__Enabled` is false there. |
| `refused ... as if it came from a browser` (`BROWSER_CLIENT_REJECTED`) | Something between the connector and FilingBridge adds an Origin, Referer, Sec-Fetch or cookie header. |
| `SECOND_FACTOR_REQUIRED` / code not accepted | Give `--totp-code` (each code works once; wait for the next) or `--recovery-code`. |
| `MFA_ENROLMENT_REQUIRED` | Enrol an authenticator in the web application first. |
| `DATA_SCOPE_FORBIDDEN` | Only an Owner may connect with `--data-scope full`. |
| `SESSION_READ_ONLY` / `SESSION_LEVEL_TOO_LOW` | Reconnect at a higher `--access-level`. |
| `DATA_SCOPE_WITHHELD` / `PERSONAL_DATA_WITHHELD` | The task needs full data scope: an Owner decision. |
| `CONNECTOR_WRITE_LIMIT` | The change budget per minute is spent; wait for `retryAfterSeconds`. |
| `accounting_concurrency_conflict` (409) | The period changed since it was read; re-read and retry. |
| `NOT_CONNECTED` | Run `connect` again (the session was revoked, expired or its refresh token reused). |
| `The stored credential was issued by ...` | The base URL changed. Disconnect and connect again if intended. |
| Keychain errors on Linux | Install and unlock a Secret Service (gnome-keyring). |

## Development

```bash
npm test --prefix mcp                     # tsc + node:test on dist
node mcp/scripts/canary.mjs               # 18 mutations that must each turn the suite red
node mcp/scripts/live-e2e.mjs [baseUrl]   # the built connector over stdio against a throwaway API
npm run generate:openapi-types --prefix mcp   # after the contract changes
```
