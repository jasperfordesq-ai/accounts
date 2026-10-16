# Charity/CLG chart of accounts — design

**Status:** written autonomously on 11 October 2026 from the owner's round-2 brief (item 1), for the
owner's review. Nothing here is committed or deployed until the owner has reviewed it.
**Research:** a code-coupling inventory with file:line references was used to write this (retained
outside the repository; summary in §2).

## 1. Problem

FilingBridge has one per-company account table (`account_categories`), seeded from a generic
trading chart (52 rows). The transaction picker shows all rows; the Opening Balances picker shows the
balance-sheet rows of the same table. So the app's two lists never disagree with each other — both
are a trading chart that does not fit a company limited by guarantee:

- it offers Share Capital, Dividends Paid and Corporation Tax Payable to a CLG with a charitable tax
  exemption;
- it lacks members' funds, restricted funds, a finance-lease split, distinct donation/grant/
  fundraising income, a charitable-activity vs support-and-governance cost split, and a suspense
  control that must be nil before approval.

Underneath, the statements engine decides presentation **by code number** (37 places), e.g.
1400–1499 = cash, 1200 = prepayments, 3000 = share capital, 27xx/29xx = creditors after one year,
69xx = interest, 8xxx = tax. Automatic adjustments find accounts by code, ignoring type. A charity
chart reuses those numbers with other meanings (1200 bank, 2300 lease > 1 yr, 3000 members' funds,
8000 depreciation, 8100 interest), so seeding it today would silently misstate the balance sheet and
mis-post depreciation, prepayments and tax.

## 2. Decisions

1. **Engine first.** Every account carries an explicit statement line, cash-flow class and optional
   system role. The engine reads those, never the code. Existing companies are back-filled with
   exactly today's code rules (one pure function used for the backfill, the runtime fallback, and a
   parity test). **Acceptance gate: the entire existing backend and frontend suites pass with no test
   edits, and generated outputs/fingerprints for generic companies are unchanged.**
2. **Variant, not replacement.** `companies.ChartOfAccountsVariant` = `GenericTrading` (default,
   today's 52 rows byte-identical) or `CharityClg`. New companies choose at creation (defaulted to
   `CharityClg` for a CLG that is a charitable organisation).
3. **Charity chart = the owner's chart, sections 1–11**, with its BS map as the statement line. The
   bank account number in the owner's chart never enters the repository: generic names ("Bank
   current account", "Bank overdraft"); a company can rename its own rows.
4. **Adopting the chart on an existing company never moves a transaction and never deletes an
   account.**
   - Renamed in place (id preserved), structural only: bank control 1400 → 1200 "Bank current
     account" and retained earnings 3100 → 3000 "Members' funds brought forward". No transaction is
     categorised against either.
   - A row "clashes" when its code equals a charity code, falls inside a charity code range with a
     different meaning (e.g. a generic 2500 or 6900 inside the charity creditor/cost ranges), or its
     role is taken by a charity row. Clashing changes only the code, name and (where a charity row
     takes it) the role — never the type or statement line.
   - Generic rows **with postings** whose code clashes with a charity code are re-coded to a legacy
     code (`G-<code>`, name suffixed "(legacy)"), keep their type/line/postings and **stay
     selectable**, so statements are unchanged and the owner re-categorises (and splits) from them,
     transaction by transaction or in bulk, in the app. This is the owner's instruction: no
     automatic remap.
   - Generic rows without postings are archived (hidden from pickers, kept in the table and
     history; a legacy code is given only where needed to free a charity code).
   - Charity rows are inserted for every code not otherwise present.
   - Dry-run plan first, then execute with the plan's SHA-256, in one transaction under the company
     lock, with two-phase code assignment (codes permute: 1400→1200 while a clashing 1200 is
     re-coded). Refuses if any affected account has postings in a Finalised/Filed/locked period, or if
     a CRO filing pack would go stale without acknowledgement. Audited with before/after snapshots.
   - Live acceptance: a fixture mirroring the live company's shape: six categorised accounts and
     their transactions; counts, uncategorised and net movement unchanged after adoption.
5. **Suspense** (9998): a non-nil balance in any suspense-line account is a readiness blocker
   ("clear it before the statements are approved"); evaluated only when such an account exists, so
   generic companies are unaffected.
6. **Hidden accounts.** Archived rows and the bank control are rejected as categorisation /
   opening-balance targets (server-side) and hidden from pickers; a hidden row with a balance is still
   shown so it can be cleared. For a company holding a charitable tax exemption, automatic
   adjustments post no corporation-tax provision.
7. **Charity-variant presentation** (gated on the variant so generic companies are unchanged):
   members' funds labels (no share capital row), an overdrawn bank presented within creditors due
   within one year, and the finance-lease < 1 yr / > 1 yr split via the 2210/2300 statement lines.
8. **Re-categorising** an already-categorised transaction is already supported, audited (old and
   new value) and blocked only for finalised/filed/locked periods — it is reversible by
   re-categorising back. Tests pin this.

## 3. Statement lines

BS: TangibleFixedAssets, Stocks, Debtors, Prepayments, CashAtBank, TradeCreditors, Accruals,
TaxCreditors, OtherCreditorsWithinYear, LoansAfterYear, OtherCreditorsAfterYear, ShareCapital,
Reserves, RestrictedFunds, DividendsPaid, Suspense, OpeningBalanceControl.
P&L: Turnover, OtherOperatingIncome, CostOfSales, AdministrativeExpenses, Depreciation,
InterestPayable, TaxOnProfit.
Cash-flow class: Operating, Investing, Tax, Dividend, Loan, ShareIssue, OtherFinancing, Cash.
System roles: BankControl, RetainedEarnings, Depreciation, Prepayments, Stock,
CostOfSalesStockMovement, Accruals, TradeDebtors, TradeDebtorIncome, TradeCreditors,
DirectorLoanLiability, DirectorLoanReceivable, CorporationTaxCharge, CorporationTaxPayable,
DividendsPaid, DividendsPayable, DisposalLoss, DisposalGain, VatPayable, VatReceivable, Suspense,
OpeningBalanceControl.

## 4. Charity chart attributes (codes from the owner's chart)

| Codes | Type | Statement line | Cash flow | Role |
|---|---|---|---|---|
| 0010, 0011, 0020, 0021, 0030, 0031 | Asset | TangibleFixedAssets | Investing | — |
| 1000 | Asset | Stocks | Operating | Stock |
| 1100 | Asset | Debtors | Operating | TradeDebtors |
| 1110 | Asset | Debtors | Operating | — |
| 1120 | Asset | Prepayments | Operating | Prepayments |
| 1200 Bank current account | Asset | CashAtBank | Cash | BankControl |
| 1230 Cash in hand | Asset | CashAtBank | Cash | — |
| 2000 | Liability | TradeCreditors | Operating | TradeCreditors |
| 2100 | Liability | Accruals | Operating | Accruals |
| 2200 Bank overdraft | Liability | OtherCreditorsWithinYear | Cash | — (normally nil) |
| 2210, 2215 finance lease < 1 yr / arrears, 2220 community loan | Liability | OtherCreditorsWithinYear | Loan | — |
| 2230 Director's loan account | Liability | OtherCreditorsWithinYear | OtherFinancing | DirectorLoanLiability |
| 2240 Other creditors | Liability | OtherCreditorsWithinYear | Operating | — |
| 2250 PAYE/PRSI | Liability | TaxCreditors | Operating | — |
| 2260 VAT | Liability | TaxCreditors | Operating | VatPayable |
| 2300 finance lease > 1 yr | Liability | OtherCreditorsAfterYear | Loan | — |
| 2310 Long-term loans | Liability | LoansAfterYear | Loan | — |
| 3000 Members' funds brought forward | Equity | Reserves | OtherFinancing | RetainedEarnings |
| 3100 Surplus/(deficit) for the year | Equity | Reserves | OtherFinancing | — |
| 3200 Restricted funds | Equity | RestrictedFunds | OtherFinancing | — |
| 4000–4800 (11 income rows) | Income | Turnover | — | 4000 = TradeDebtorIncome |
| 5000, 5010 | Expense | CostOfSales | — | 5010 = CostOfSalesStockMovement |
| 6000–6300 (charitable activity), 7000–7800 (support and governance) | Expense | AdministrativeExpenses | — | — |
| 8000, 8010 depreciation | Expense | Depreciation | — | 8000 = Depreciation |
| 8100, 8110, 8120 interest | Expense | InterestPayable | — | — |
| 8200 Government levies and stamp duty | Expense | AdministrativeExpenses | — | — |
| 9998 Suspense — uncategorised | Liability | Suspense | Operating | Suspense |
| 9999 Opening balance control | Equity | OpeningBalanceControl | OtherFinancing | OpeningBalanceControl |

Rows the chart lacks but the engine may need (director-loan receivable, disposal gain/loss) are
created lazily only if a posting requires them, with non-clashing codes (1150, 7050, 4900).
Depreciation credits the asset cost account as today; the x1 accumulated-depreciation rows are
present for later use (out of scope: an accumulated-depreciation role).

## 5. Out of scope (owner decisions)

- Ledger-level fund accounting (restricted/unrestricted by transaction); the SoFA stays manual.
- The FRS 105 micro P&L format; members'-funds iXBRL tagging (Revenue iXBRL is manual-handoff).
- Applying the overdraft and members'-funds presentation to generic-chart companies.
- Automatic remapping or splitting of the live categories (the owner does this in the app).
- Entering the take-on (opening) balances for the period under review (owner's next step once the chart exists).

## 6. Testing

Parity (backfill SQL = runtime fallback = old predicates for every seed, auto-created and edge
code); full existing suites unchanged after the engine switch; charity-chart statements (bank,
overdraft, lease split, members' funds, interest, depreciation add-back, prior-year close into 3000);
suspense blocker; hidden/archived/bank-control rejection; no CT provision when exempt; adopt-chart
dry-run/execute on Postgres (ids preserved, transactions untouched, permutation, locked-period
refusal, plan-hash mismatch refusal, audit); a fixture mirroring the live company's shape: six
categorised accounts and their transactions; counts, uncategorised and net movement unchanged after
adoption.

## Revision: 16 October 2026 (hOUR Timebank FY2026 handoff)

- **Depreciation credits accumulated depreciation.** The automatic depreciation journal credits the
  class's accumulated-depreciation account (leased equipment 0011, office and IT 0021, fixtures and
  fittings 0031) and never the cost account. Crediting cost (the behaviour up to preview.13) reduced
  cost each year and left accumulated depreciation at nil, so the tangible fixed assets note could not
  show cost and accumulated depreciation separately (FRS 102 s.17.31, FRS 105 s.12). hOUR Timebank's
  FY2026 period carries two approved manual reclass journals (#21, #22) that corrected this before the
  generator was fixed; they stand, and the FY2026 depreciation journals were not regenerated. The
  generic trading chart has one net account per class and still credits that account.
- **Opening stock from opening balances is released.** When a period's opening stock comes from
  explicit opening balances (a take-on from filed accounts) rather than from the prior period's own
  closing-stock journal, the generator now posts "Opening stock released to cost of sales"
  (Dr 5010 Cr 1000) before recognising closing stock. Previously only closing stock was posted, so
  the opening figure stayed on the balance sheet and cost of sales was understated.
