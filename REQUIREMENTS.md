# Irish Statutory Accounts Production Platform — Full Requirements

## Core Product Concept
- Multi-company Irish accounts-production web app for private companies
- Replaces traditional year-end accountant workflow
- NOT bookkeeping software, NOT a transaction recorder
- Takes imported bank transactions + year-end business facts → statutory financial statements

## Jurisdiction
- Ireland
- Companies Act 2014, as amended
- CRO annual return and financial statements filing logic
- Revenue corporation tax filing logic through ROS, including CT1 support and iXBRL

## Main Objectives
Turn raw bank transactions and supplemental year-end facts into:
1. Adjusted accounting figures
2. Statutory financial statements
3. CRO filing-ready outputs based on company size
4. Revenue CT1 support outputs
5. iXBRL-ready financial statement data structures

## Key Legal Logic

### 1. Company Size Engine
- Micro: turnover <= €900,000, balance sheet total <= €450,000, employees <= 10
- Small: turnover <= €15,000,000, balance sheet total <= €7,500,000, employees <= 50
- Medium: turnover <= €50,000,000, balance sheet total <= €25,000,000, employees <= 250
- Large: above medium limits
- "2 out of 3" threshold logic
- First-year and subsequent-year qualification logic
- Exclusion flags (holding/subsidiary/investment)
- Configurable thresholds

### 2. Filing Regime Engine
- Micro → FRS 105, balance sheet only + limited notes
- Small → can elect abridged (no P&L to CRO)
- Medium → fuller disclosure, audit likely required
- Large → full statutory accounts, audit required
- Audit exemption eligibility (s.360 Companies Act 2014)
- Required notes per regime

### 3. Multi-Company Architecture
- Multiple companies under one user account
- Separate dashboards per company
- Period-by-period history
- Locked closed periods
- Reusable chart/category mappings
- Role-ready design (accountant, owner, reviewer)

## Primary Workflows

### A. Company Onboarding (wizard, 4 steps)
Collect: legal name, CRO number, tax reference, company type, incorporation date, FY dates, ARD, registered office, group/holding/subsidiary/investment flags, trading/dormant/VAT/employer/stock/asset/borrowing/director-loan flags

### B. Size Classification Interview
Ask for turnover, balance sheet total, avg employee count, prior year class, first year flag, exclusions. Calculate and display current size class with reasoning and available filing options.

### C. Data Import
- Import bank CSVs (AIB, BOI, Revolut, Stripe auto-detection)
- Normalise date, amount, description, account, tax, balance
- Multiple bank accounts per company
- Duplicate detection
- Categorise with confidence scoring
- Manual remapping and rules

### D. Year-End Accounting Questionnaire
Progressive plain-English questions:
- Trade debtors, trade creditors, accruals, prepayments
- Payroll totals and liabilities
- VAT/RCT/PAYE liabilities
- Fixed asset additions/disposals, depreciation policy
- Loans, director loans, dividends
- Stock and WIP, bad debts
- Related-party items
- Non-business/disallowable expenses
- Tax balances and preliminary tax paid

### E. Adjustment Engine
Auto-generate and manually edit:
- Depreciation, accruals, prepayments, stock adjustments
- Loan interest, tax accruals, director loan reclassifications
- Capital vs revenue treatment, payroll/VAT timing
- Retained earnings roll-forward
Each shows: source, reason, legal basis, P&L impact, balance sheet impact, who created/approved

### F. Outputs

Internal:
- Categorised transactions report
- Review exceptions report
- Lead schedules
- Adjusted trial balance
- P&L, Balance sheet, Notes, Working papers
- Corporation tax bridge

External (regime-based):
- Micro: statutory statements, CRO reduced filing, required notes, audit exemption wording
- Small: full statutory for members, abridged for CRO, directors' report
- Medium/Large: fuller accounts, broader disclosure checklist
- Revenue: CT1 support, tax computation, preliminary tax tracker, iXBRL export

## UX Rules
- Feel like an accountant-led review assistant, not a bookkeeping ledger
- Progressive plain-English questions
- Legal explanations only when needed
- Every final figure drillable to transactions/inputs/adjustments
- "Accounts completeness" score and "filing readiness" score
- Flag areas requiring judgement/estimates/external review
- Full audit trail of all overrides and edits
- Quick company switching from one account

## Implementation Phases
1. Foundation (backend scaffold + database) ✅ COMPLETE
2. Company Management (frontend + dashboard + onboarding wizard)
3. Rules Engines (size classification + filing regime)
4. Bank Import & Categorisation
5. Year-End Data Collection (questionnaire + all year-end entity CRUD)
6. Adjustment Engine
7. Financial Statements (trial balance, P&L, balance sheet, scoring)
8. Document Generation (QuestPDF — micro/small/medium/large templates)
9. Revenue / iXBRL
10. Polish & Seed Data (3 sample companies, filing dashboard, audit viewer)
