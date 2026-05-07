# Building an Automated Accountant for Irish Statutory Accounts and Charity Reporting

## Problem framing and legal boundary conditions

This research covers statutory year-end financial statements, directors’ reports, and filing workflows for companies and charities in entity["country","Ireland","republic of ireland"], with an emphasis on the exact “bridge” work accountants do after exporting figures from bookkeeping software (trial balance / ledgers) and before filing. citeturn2view1turn10view5turn13view0

A key boundary: software can prepare statutory financial statements and narrative reports, but it cannot remove (a) directors’ legal responsibility for maintaining adequate accounting records and for approving reports, or (b) the legal requirement for an audit where no audit exemption applies (or where members force an audit). The Companies Act explicitly links “adequate accounting records” to the ability to determine assets/liabilities/profit or loss and to enable compliant financial statements and (where applicable) an audit. citeturn10view5turn22view0turn25view3turn2view0

Practically, this means the product you’re designing needs two distinct “modes”:
- **Preparation and compliance mode**: generate accounts and disclosures that directors can approve and sign, with a defensible audit trail from outputs back to underlying records. citeturn10view5turn25view3turn22view0  
- **Filing-pack mode**: generate the specific artefacts required for filing (often abridged) and ensure deadlines, certifications, and upload constraints are satisfied. citeturn19view1turn2view0turn11view1

## Filing outputs and deadlines for the entity["organization","Companies Registration Office","company registry ireland"]

Irish company filing is anchored on the annual return (Form B1 via CORE) and the financial statements that must be annexed (or validly exempted/abridged). An annual return must be delivered within **56 days** after its effective date, with specific rules on how that 56-day window behaves on weekends/public holidays. citeturn2view0

Where financial statements must be attached, the CRO describes the filing deadline as the earlier of:
- **ARD + 56 days**, or  
- **Financial year-end + nine months + 56 days**. citeturn2view0turn19view0

The CRO also notes important “operational” constraints that your software should treat as hard requirements in its filing-pack workflow:
- A B1 is **not deemed submitted until after the payment stage** on CORE. citeturn19view1  
- Financial statements and the signature page must be uploaded (each as a **single PDF**) before payment can be made within the 56-day window. citeturn19view1  
- CRO “send back” corrections are time-critical: if an annual return is returned for correction/fees, the Companies Act requires correction and re-delivery within **14 days** or the original is deemed not delivered, potentially triggering late fees and loss of audit exemption eligibility. citeturn2view0

Late filing has multiple consequences that your system should proactively prevent with calendar logic, pre-submission checks, and “cannot file” blockers:
- Late filing fees (with the CRO stating €100 initial plus €3 per day up to a maximum) and statements that Revenue does not treat such fees as tax deductible. citeturn19view0  
- Prosecution risk and categorisation of offences for non-compliance described by the CRO. citeturn19view0  
- A clear CRO warning that filing late can remove entitlement to claim audit exemption in subsequent years, and that recent legislation changes the availability of audit exemption where annual returns are filed late more than once within a five-year period. citeturn19view1turn19view0

The CRO also distinguishes between what must be **laid before members at the AGM** and what must be **filed**. It states directors must lay a profit and loss (or income and expenditure account), balance sheet, directors’ report and auditors’ report before members, and that these items are generally required to be annexed to the annual return (subject to small/micro exemptions). citeturn2view1turn10view4

For group and not-for-profit structures, the CRO highlights additional regimes and exemptions that materially affect what your software must output:
- **Holding undertakings**: section 293 group financial statement requirements, plus exemptions from consolidation for small/micro holding companies and other cases. citeturn33view0  
- **CLG / DAC limited by guarantee**: required annexed statements, modified directors’ report compliance in certain respects, and specific cases where charitable CLGs/DACs may be exempt from filing financial statements with the CRO (by order), with alternative reporting consequences. citeturn20view0turn13view0

## Company size regimes and accounting frameworks

A correct “company regime classification” is the first branching decision in your automated accountant, because it determines:
- whether the entity can use the **micro** or **small** companies regime,  
- whether it can file **abridged** accounts,  
- whether it might be entitled to **audit exemption**, and  
- which directors’ report content is mandatory or exempt. citeturn10view0turn10view1turn9view0turn34view0

### Updated small and micro thresholds

The revised Companies Act provisions set out the current size thresholds (reflecting the 2024 adjustments):

A company qualifies as **small** if it fulfils 2+ of: turnover ≤ €15m, balance sheet total ≤ €7.5m, average employees ≤ 50 (with rules for first/subsequent years and exclusions for holding or “ineligible” companies). citeturn10view0turn30view0

A company qualifies as **micro** if it qualifies for the small companies regime and fulfils 2+ of: turnover ≤ €900k, balance sheet total ≤ €450k, average employees ≤ 10 (with explicit exclusions for certain investment/holding/subsidiary scenarios). citeturn10view1

For **groups**, the small group test is similarly “2 out of 3” but uses net or gross aggregate turnover/balance sheet total (with net meaning after elimination of group transactions). citeturn31view0turn33view0

### Ineligible entities must be detected early

Your software should explicitly ask whether the company is an “ineligible entity”, because the Companies Act defines “ineligible entities” to include (among others) undertakings with transferable securities admitted to trading on a regulated market, credit institutions, insurance undertakings, and certain Schedule 5 / Accounting Directive-designated entities. citeturn30view0

This matters because multiple size regimes/exemptions are unavailable where a company (or group member) is ineligible. citeturn10view0turn31view0turn34view0

### Directors’ report and business review exemptions

The revised Companies Act provisions create major simplifications for smaller companies:
- A company qualifying for the **small companies regime** is not required to include a **business review** in the directors’ report. citeturn22view0turn21view0  
- A company qualifying for the **micro companies regime** can be exempt from preparing a directors’ report entirely, provided its “own shares” information (section 328) is instead included as a note/footnote to the balance sheet. citeturn22view0turn25view1turn2view2  
- Separately, the directors’ report “business review” requirements (fair review of the business; principal risks/uncertainties; KPIs; etc.) do not apply to small or micro companies. citeturn21view0

### Abridged filing versus full statutory accounts

Abridgement is not “a different set of accounts”; it is an excerpt of the statutory financial statements prepared under the Act. Section 352 provides the filing exemption mechanics: if a company qualifies for the small or micro regime and has not elected to prepare group financial statements under section 293, it may file abridged financial statements (plus, where relevant, the special statutory auditors’ report), instead of filing the full statutory accounts set. citeturn9view0turn11view0turn11view1

Section 353 specifies the content of abridged financial statements as extracted from statutory financial statements. In broad terms, it always includes the balance sheet plus required notes (and, for certain frameworks, other statements/notes as required). citeturn11view0turn11view1

The CRO’s interpretation for micro and small filing outputs is operationally important:
- **Micro companies** claiming both audit and abridgement exemptions file the balance sheet (with the audit exemption statement) and notes, and are exempt from many note requirements; and micro companies are not required to prepare a directors’ report if section 328 information is included on/with the balance sheet. citeturn2view2turn34view0turn22view0  
- **Small companies** claiming the abridgement (size) exemption file a balance sheet with the required statement, notes, and (where they are not audit exempt) the auditor’s report/special report requirements for abridged accounts. citeturn11view1turn34view0turn2view3

### Audit exemption is conditional and can be overridden

The Companies Act provides a statutory right for members to require an audit even where audit exemption would otherwise be available (10% voting rights for share companies, with notice timing constraints). citeturn28view0turn34view0

For CLGs, the Act modifies this so that any member may serve such a notice. citeturn28view1turn34view0turn20view0

For group companies, the Act links audit exemption to whether the group qualifies as a small group (via the small group criteria) and includes further conditions/exclusions. citeturn32view0turn31view0turn33view0

Finally, your product must treat “audit” as a separate professional function: where an audit is required, the system can prepare a clean, auditable pack, but cannot generate the statutory auditor’s opinion. The CRO explicitly states that financial statements must be audited unless audit exemption applies and is validly claimed. citeturn2view0turn34view0

### Applicable accounting standards and framework selection

Irish statutory accounts are prepared under “Irish law and applicable accounting standards”, with the Companies Act describing “accounting standards” as being issued by a prescribed body; the revised Act notes the prescribed body for that definition includes the Financial Reporting Council. citeturn30view0

At a practical implementation level, your software should treat the framework choice as a controlled configuration:
- FRS 102 is the single financial reporting standard applicable in the UK and Republic of Ireland for entities not using adopted IFRS, FRS 101, or FRS 105. citeturn12search2  
- FRS 105 is intended for companies that qualify for the micro-entities regime. citeturn12search1turn12search9  

## How accountants bridge bookkeeping data to statutory accounts

Accountants typically start with “bookkeeping truth” (general ledger / trial balance as exported by a bookkeeping system) and produce “statutory truth” (financial statements that comply with the Companies Act, are properly approved/signed, and meet the required disclosure framework). The Companies Act explicitly connects the quality of accounting records to the directors’ ability to ensure financial statements and directors’ reports comply with the Act. citeturn10view5turn22view0

Below is the bridge process your software needs to replicate, expressed as functional requirements rather than generic accounting advice.

### Ingest and normalise

Your intake layer needs to transform diverse accounting-package exports into a standard internal representation:
- **Chart of accounts and trial balance** at period end (with comparative period, if available).  
- **General ledger detail** for sampling and disclosure evidence (especially for related party items, accruals/prepayments, fixed assets, and director transactions).  
- **Subsidiary schedules**: fixed asset register, aged receivables/payables, loans, leases, inventory/stock, deferred income, and tax/VAT control accounts (as applicable).

This step is not optional: section 282 expects accounting records to correctly record/explain transactions, record day-to-day money movements, and contain a record of assets and liabilities. citeturn10view5

### Validate “adequate records” and lock the period

A competent year-end process begins with “are the bookkeeping records adequate and closed for the period?”. The statute links adequacy to reasonable accuracy of assets/liabilities/financial position/profit or loss and to the ability to prepare compliant financial statements (and be audited where required). citeturn10view5

Software implications:
- Require explicit confirmation of the **financial year start/end**, and enforce CRO constraints where relevant (first year ≤ 18 months; later years ≈ 12 months unless changed via appropriate CRO process). citeturn2view1  
- Run “hard” integrity checks (trial balance balances; opening balances tie to prior filed period; control accounts reconcile to subsidiary ledgers).  
- Create a period lock and a versioned audit trail: subsequent adjustments must be posted via explicit “year-end journals” with reasons and evidence references.

### Determine legal form, size regime, and exemptions

This is the decision point that drives the rest of the workflow. At minimum, the system must collect enough data to decide:
- company legal form (LTD, DAC, CLG, etc.); citeturn34view0turn20view0  
- whether the entity is ineligible; citeturn30view0  
- small/micro qualification (or group small qualification), using current thresholds and “two consecutive years” logic; citeturn10view0turn10view1turn31view0  
- whether abridgement is allowed and which abridged package must be filed; citeturn9view0turn11view0turn11view1  
- whether the company can claim audit exemption (and whether any member notice makes audit mandatory). citeturn28view0turn28view2turn34view0

Critically, your software must separate:
- **what must be prepared for AGM/member approval** (full statutory accounts must still be laid before the AGM even where filing exemptions exist), citeturn34view0turn10view4turn2view1  
- from **what must be filed** (abridged vs full; auditor report vs audit-exempt statements). citeturn34view0turn11view1turn2view2

### Generate the year-end adjustment workflow

The “value” accountants add is rarely typing numbers into templates; it is identifying missing accruals and statutory presentation/disclosure requirements. For your automated accountant, this is a guided questionnaire + rules engine.

You should implement a structured “adjustments and disclosures interview” derived directly from directors’ report and financial statement obligations, rather than a generic checklist:

- Directors’ report general matters require: directors list, principal activities, measures re accounting-record compliance and record location, and dividends (interim and proposed). citeturn25view0turn10view5  
- Important events after year end, R&D activity, foreign branches, and certain political donations are required “where relevant”. citeturn25view0  
- Companies using financial instruments have additional required discussion unless exempt as small/micro. citeturn25view0turn2view2  
- If the company acquired/held its own shares (including via subsidiaries), detailed reconciliations and reasons/proportions must be disclosed, or (for micro companies not preparing a directors’ report) included as a note/footnote to the balance sheet. citeturn25view1turn22view0turn2view2  
- Directors’ and secretary interests in shares/debentures must be disclosed unless disapplied (for example, the Act disapplies sections 325(1)(c) and 329 to CLGs). citeturn25view2turn26view0  
- Where an audit is performed, the directors’ report must contain the “relevant audit information” statement (and the Act defines the statement content). citeturn25view3turn34view0

The system’s interview must drive quantitative computations such as:
- depreciation schedules and fixed asset note disclosures;  
- accruals and prepayments;  
- stock/inventory valuations;  
- impairment and provisions;  
- loan classification between current/non-current;  
- related party disclosures (especially for director loans/transactions);  
- going concern and events after the reporting date.

These items are not all spelled out on the CRO pages, but the CRO explicitly states directors must follow specimen formats and disclose required information via notes, and that financial statements filed must be prepared in accordance with the Companies Act. citeturn2view1turn34view0turn11view0

### Produce two outputs: approval pack and filing pack

Your software should generate at least two document sets, because the statutory “lay before AGM” pack and the CRO filing pack are not the same thing in many cases.

**Approval pack** (for directors/members):
- Full statutory financial statements (profit and loss/income & expenditure, balance sheet, and required notes under the chosen framework). citeturn2view1turn34view0turn11view0  
- Directors’ report (or, for micro companies using the exemption, no directors’ report with section 328 handled via a balance sheet note/footnote). citeturn22view0turn10view4turn2view2  
- If audited, statutory auditors’ report (supplied by auditor, but your system should reserve placeholders and provide a controlled import). citeturn2view1turn34view0turn25view3  

**Filing pack** (for CRO):
- Abridged or full accounts as required for company type/size, plus any required special auditors’ report where abridged accounts are filed but audit exemption is not available. citeturn11view1turn9view0turn34view0  
- The correct CRO statements (audit exemption statement on the balance sheet; abridgement exemption statement; etc.). citeturn10view3turn11view1turn34view0  
- The certificate signed by director and secretary certifying the filed documents are true copies of those laid/to be laid before the AGM. citeturn2view1turn20view1  
- Operational packaging constraints: separate single-PDF uploads and payment completion rules. citeturn19view1turn2view0

## Charity reporting for the entity["organization","Charities Regulator","ireland statutory charity regulator"]

Charities have a parallel annual reporting regime under the Charities Act 2009. Section 52 requires charity trustees to prepare and submit an annual report (wording and attachment rules are being amended by the 2024 Act but those changes are explicitly shown as not commenced in the Revised Acts annotations). citeturn13view0

### Deadline and minimum attachments

The Charities Act requires the annual report to be prepared and submitted **no later than 10 months after the end of each financial year** (or a longer period if specified by the Authority). citeturn13view0turn39view0

The annual report must have attachments, including:
- the annual statement of accounts (or, where applicable, an income & expenditure account plus statement of assets and liabilities under section 48(3)); citeturn13view0turn13view1  
- the auditor’s report where accounts have been audited; or the independent person’s report where accounts have been examined. citeturn13view0

The Charities Act also provides that, for charities with gross income or expenditure not exceeding €100,000, trustees may prepare an income and expenditure account and a statement of assets and liabilities instead of a full annual statement of accounts (with amendments in the 2024 Act shown prospectively but not commenced as of the revision). citeturn13view1turn13view0

### Charitable companies and interaction with CRO filing exemptions

Section 52 contains special logic for charitable organisations that are companies and are not required to annex their accounts to their CRO annual return: a copy of accounts prepared under the Companies Acts must be attached to the charity’s annual report submission. citeturn13view0turn20view0

This is essential for product design because many Irish charities are CLGs. The Companies Act disapplies certain directors’ report requirements to a CLG (sections 325(1)(c) and 329), which affects what your system must ask for and generate. citeturn26view0turn20view0turn25view2

### Governance Code reporting and evidence retention

As part of annual charity reporting practice, charities must report on compliance with the Charities Governance Code; the compliance record form is not generally filed, but must be approved and retained as the regulator may request it. citeturn38view0turn39view0

Software implication: your product should support a “Governance Code evidence binder” feature: capture the answers, store references to board minutes/policies, and generate a completed compliance record form and/or structured evidence log for internal use, with explicit retention and retrieval. citeturn38view0turn39view0

### The Charities Amendment Act 2024 status and change management

The Charities (Amendment) Act 2024 is enacted, but commencement is staged. The first commencement order (S.I. No. 10 of 2025) brought Part 1 and certain specified sections into operation on 27 January 2025. citeturn17view0turn35search5turn39view0

Separately, the Revised Acts text for core reporting sections (notably section 52 and section 48) flags prospective amendments by the 2024 Act as “not commenced as of date of revision”, meaning your software must be built with versioned rules and effective-date toggles rather than hard-coding future thresholds/terminology. citeturn13view0turn13view1

## Build specification for an automated Irish accountant system

This section converts the legal research into a build-oriented specification you can hand to Claude Code. It is written as “system behaviour”, not UI copy.

### Product goal and outputs

Your system is an **automated statutory accounts and reporting engine** that:
- imports bookkeeping exports,  
- runs a legally-driven interview to collect year-end adjustments and disclosures,  
- generates statutory financial statements and directors’/trustees’ reports for Irish entities, and  
- generates CRO and charity filing packs with the correct abridgements, statements, certificates, and packaging constraints. citeturn2view1turn11view1turn19view1turn13view0

The system must treat directors/trustees as approvers and signers (not the software), and must detect when an auditor is required and produce an “auditor handoff pack” rather than attempting to fabricate auditor outputs. citeturn34view0turn28view0turn13view0

### Core modules

**Entity and regime classifier**
- Inputs: legal form (LTD/DAC/CLG/etc), group status, ineligible entity flags, turnover/balance sheet total/employees for current and prior year, and whether consolidation exemptions apply. citeturn30view0turn10view0turn10view1turn31view0turn33view0  
- Outputs: micro/small/medium/large; small group status; eligible exemptions (audit exemption, dormant exemption, abridgement); and required report components (directors’ report full vs partial vs micro exemption). citeturn22view0turn34view0turn32view0turn21view0  

**Questionnaire and rules engine**
- Must be *regime-aware*: micro/small exemptions remove or reduce question sets (for example, business review, financial instruments narrative, etc.). citeturn21view0turn25view0turn2view2  
- Must be *company-type-aware*: CLG disapplications and modified member audit notice rules must change required prompts. citeturn26view0turn28view1turn20view0  
- Must maintain a “reason and evidence” field per answer, to support the directors’ “relevant audit information” statement when audited and to support defensibility generally. citeturn25view3turn10view5  

**Year-end journals subsystem**
- Supports adjustments, each with: journal lines, rationale, linked evidence, date, and reviewer approval.  
- Enforces that statutory outputs are always traceable back to (a) imported ledger data and (b) explicit year-end journals, consistent with the Act’s emphasis on records enabling accurate determination of financial position and compliant reporting. citeturn10view5  

**Disclosure checker**
- Implement as a ruleset keyed by `{framework, regime, company_type, group_status}` deciding which disclosures apply and which are exempt, including:  
  - directors’ report matters in sections 326–331 (with exemptions), citeturn25view0turn21view0turn22view0  
  - abridgement extraction requirements (section 353) and section 352 filing substitution logic, citeturn11view0turn9view0  
  - audit exemption statement content requirements (section 335) and member notice override (section 334). citeturn10view3turn28view0turn34view0  

**Document generator**
- Outputs:
  - Full statutory financial statements pack (for AGM/member laying). citeturn2view1turn34view0turn10view4  
  - Abridged CRO filing pack (where eligible), including required abridgement statement and correct set of notes. citeturn11view1turn11view0turn9view0  
  - Micro filing pack (balance sheet + required audit exemption statement if claiming; notes) with required micro identifiers (company name/legal form/number/registered office/place of registration, winding-up status). citeturn2view2turn10view3  
  - Charities annual report submission pack: activity report + required financial attachments. citeturn13view0turn39view0  
- Must generate the director/secretary certificate for CRO filing (“true copy”) and enforce signature availability. citeturn2view1turn20view1  

**Filing packager**
- CRO packaging constraints: generate separate single PDFs for accounts and signature page; block “submit” until required uploads exist; and make clear that submission is only complete after payment. citeturn19view1turn2view0  
- Charity packager: generate a single upload bundle aligned to the annual report/return portal requirements, with governance code compliance declaration support and internal retention of compliance record form. citeturn13view0turn38view0turn39view0  

**Compliance calendar and deadline engine**
- Must track ARD, financial year-end, and “earlier of” deadline rule for annexing financial statements. citeturn2view0turn19view0  
- Must implement special rules: returned filings correction within 14 days; late fees escalation; and audit exemption jeopardy signals. citeturn2view0turn19view0turn19view1  
- Must track charity annual report deadline (10 months after financial year end) with escalation warnings and “late” state. citeturn13view0turn39view0  

### Data residency and record accessibility constraints for a SaaS design

If your product is a cloud SaaS, the Companies Act record-keeping provisions should be treated as design constraints: accounting records must be readily accessible and readily convertible into written form in an official language of the State, and the Act includes rules about the location of the “server computer” that provides necessary access services (subject to exceptions where records are kept outside the State under further provisions). citeturn10view5

From an implementation standpoint: build an export layer that can produce complete written-form accounting records, and treat “where data is hosted” as a configurable compliance feature (e.g., EU/Ireland region hosting, plus a documented process for companies that keep records outside the State). citeturn10view5turn25view0

### Prompt-ready implementation brief for Claude Code

The block below is intentionally written as an engineering-facing “build brief” you can paste into Claude Code. It encodes the above requirements into a concrete build plan without assuming you are writing bookkeeping software.

```text
SYSTEM: Irish Statutory Accounts & Charity Reporting Engine (not bookkeeping)

Goal:
- Import bookkeeping exports (TB/GL/etc).
- Ask regime-aware questions to capture year-end adjustments + statutory disclosures.
- Generate (1) AGM approval pack (full statutory accounts + required reports) and (2) filing packs for CRO and Charities Regulator.
- Maintain traceability from every output number/narrative back to source records and explicit year-end journals.

Key constraints:
- Never fabricate auditor outputs. If audit required, output an “auditor handoff pack” with placeholders for signed auditor reports.
- Always generate outputs consistent with Companies Act regime decisions (micro/small/abridged/audit exemption/member audit notice).
- Enforce CRO packaging rules: accounts PDF + signature page PDF; submission complete only after payment.
- Enforce deadlines engine: 56-day CRO rule; earlier-of rule with FYE+9m+56d; 14-day resubmission if returned; charity annual report due within 10 months of FYE.

Core modules:
1) Ingest
   - Accept CSV/Excel/PDF-sourced exports: chart of accounts, trial balance, GL detail, fixed asset register, aged debtors/creditors, loans, leases, inventory, bank reconciliation summary.
   - Normalise into internal schema:
     CompanyProfile, FinancialYear, TrialBalance, JournalEntry (imported), JournalEntry (year-end), EvidenceItem, DisclosureAnswer.

2) Regime classifier
   Inputs:
   - Legal form: LTD/DAC shares/DAC guarantee/CLG/PLC/ULC/etc.
   - Group status: holding company? subsidiaries? consolidation exemption?
   - “Ineligible entity” flags: listed/credit institution/insurance/schedule-5 etc.
   - Size measures: turnover, balance sheet total, avg employees current/prior year; plus group net/gross aggregates if group.
   Outputs:
   - size_category: micro/small/medium/large
   - group_category: none/small_group/other
   - exemptions: abridgement_allowed, audit_exemption_possible, dormant_exemption_possible, filing_exemption_possible
   - directors_report_mode: micro_exempt / small_no_business_review / full

3) Compliance interview engine
   - Generates a dynamic question set based on classifier output.
   - Stores each answer with: rationale, evidence links, approver, date.
   - Must cover:
     Directors’ report general matters (directors list, principal activities, dividends, accounting-record measures & location, events after year end, R&D, foreign branches, political donations).
     Own shares (if any).
     Directors/secretary interests in shares/debentures (unless disapplied e.g. CLG).
     Relevant audit information statement (only if audited).
     Going concern, subsequent events, commitments/contingencies, related parties, fixed assets & depreciation, accruals/prepayments, provisions, stock, loans, leases.

4) Year-end journals
   - All adjustments are explicit journals with audit trail.
   - Support “proposed journal” -> “approved journal” workflow.

5) Disclosure checker
   - For each regime & framework, maintain a ruleset:
     required_disclosures[], optional_disclosures[], prohibited/omitted_by_exemption[].
   - Must be versioned by “effective date” so law changes can be toggled without code changes.

6) Statement generator
   Outputs:
   A) AGM pack:
      - Full statutory financial statements (P&L or I&E, balance sheet, notes).
      - Directors’ report unless micro directors-report exemption used.
   B) CRO filing pack:
      - If abridgement: abridged financial statements extracted from statutory accounts + required statements.
      - If micro: balance sheet + notes + exemption statements.
      - Certificate page (director + secretary).
      - Signature page for B1 where needed.
   C) Charities Regulator pack:
      - Annual activity report narrative + financial attachments + governance-code declaration helper.
      - Generate a Compliance Record Form (not for filing) and an evidence binder.

7) Deadline & packaging automation
   - Build a “filing readiness score” that is blocked unless:
     TB balanced, required disclosures complete, documents produced, signature placeholders satisfied, PDFs generated as required, deadlines met.
   - Track: ARD, effective date, FYE, 56-day window, 14-day correction deadline, charity 10-month deadline.

Non-functional requirements:
- Traceability: every figure in financial statements links to TB lines + year-end journals + evidence.
- Permissions: role-based (preparer, director approver, trustee approver, auditor external).
- Data residency control + exportable accounting records archive.
- Test suite must include: micro audit exempt; small abridged + audit exempt; small abridged + audit required (member notice); CLG charity with CRO filing exemption + charity reporting; holding company small group exemption from consolidation; late filing simulation with audit exemption jeopardy flags.
```

This brief is intended to ensure the build matches the actual statutory branching logic (micro vs small vs group etc.), the directors’ report content rules, and the CRO/charity filing realities that make accountants “worth money” in year-end work. citeturn22view0turn25view0turn9view0turn19view1turn13view0turn17view0