# Adopting the charity / CLG chart of accounts on an existing company

This runbook moves an existing company that was set up on the standard trading chart onto the
charity / company-limited-by-guarantee chart (`CharityClg`). It never moves a transaction, never
deletes an account and never creates opening balances. Design:
`Docs/superpowers/specs/2026-10-11-charity-chart-of-accounts-design.md`.

## What adoption does

- **Renamed in place (same account id):** the bank control account becomes **1200 Bank current
  account** and the retained-earnings account becomes **3000 Members' funds brought forward**. No
  transaction is categorised against either, so nothing about your transactions changes.
- **Legacy accounts:** existing accounts that carry postings and whose code clashes with a charity
  code are re-coded `G-<old code>` with "(legacy)" added to the name. They keep their postings and
  presentation and stay selectable, so the statements are unchanged until you re-categorise.
- **Archived accounts:** unused standard-chart accounts are archived (hidden from pickers, kept in
  the record).
- **New accounts:** the charity chart's accounts are added for every code then free.

## Before you start

1. **Back up.** On a Private Server: `./filingbridge backup -BackupRecipient '<age-recipient>'
   -OutputDirectory "$HOME/filingbridge-backups"`, then copy the new backup set off the server.
2. **Write down the regression check** for every period you have worked on: transaction count,
   uncategorised count and net movement (Period → Import/Categorise shows the counts). To read
   every row, page through
   `GET /api/companies/{id}/periods/{periodId}/transactions?pageSize=200&page=1`, then `page=2`,
   `page=3` and so on until every row has been read — the endpoint returns at most 200 rows per
   page, whatever page size is asked for.
3. Make sure no affected account has postings in a **Finalised, Filed or locked** period; adoption
   refuses in that case. Reopen the period first if that is appropriate.

## Adopt

1. Company page → **Chart of accounts** → **Adopt the charity / CLG chart**.
2. Read the **plan**: renames, legacy re-codes (with transaction counts), archives, new accounts,
   any filing packs that would become stale, and any blockers. Nothing has changed yet.
3. If filing packs would become stale, regenerate them after adoption (and expect any recorded
   directors' approval for those accounts to be withdrawn by regeneration, or withdraw it first).
4. **Confirm.** You will be asked to re-verify with your authenticator. Adoption runs in one
   transaction; if anything fails, nothing changes.

## After adopting

1. **Re-check** the numbers from step 2 above: same transaction counts, still 0 uncategorised (or
   the same uncategorised count as before), same net movement. Statement totals are unchanged.
2. **Re-categorise** each legacy account's transactions to the charity accounts, in the period's
   Categorise tab — one at a time, or in bulk where every transaction moves to the same account.
   Every change is audited and can be reversed by re-categorising back. Split accounts (one legacy
   account whose transactions belong to several charity accounts) transaction by transaction.
3. When a legacy account has no transactions left, it can be left as it is. There is no archive
   action for users, so legacy accounts stay listed (and selectable) after they are emptied.
4. Confirm **9998 Suspense** is nil before approving the statements — the readiness check blocks
   approval until it is.
5. Then enter the reviewed **take-on (opening) balances** against the charity balance-sheet
   accounts, and continue with classification, year-end, notes and the directors' approval.
