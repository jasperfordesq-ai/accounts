/**
 * The legal framework FilingBridge applies, as reference text an agent can
 * attach before it reasons about a company.
 *
 * The thresholds mirror the SizeThresholds section of
 * backend/Accounts.Api/appsettings.json; legal-framework.test.ts reads that
 * file and fails if they differ. FilingBridge's own classification engine is
 * the authority for any particular company: an agent should run
 * size_classify rather than apply these numbers by hand.
 */
export interface SizeThreshold {
  Turnover: number;
  BalanceSheet: number;
  Employees: number;
}

export const SIZE_THRESHOLDS = {
  EffectiveFrom: '2024-01-01',
  EarlyElectionEffectiveFrom: '2023-01-01',
  Micro: { Turnover: 900_000, BalanceSheet: 450_000, Employees: 10 },
  Small: { Turnover: 15_000_000, BalanceSheet: 7_500_000, Employees: 50 },
  Medium: { Turnover: 50_000_000, BalanceSheet: 25_000_000, Employees: 250 },
  HistoricalMicro: { Turnover: 700_000, BalanceSheet: 350_000, Employees: 10 },
  HistoricalSmall: { Turnover: 12_000_000, BalanceSheet: 6_000_000, Employees: 50 },
  HistoricalMedium: { Turnover: 40_000_000, BalanceSheet: 20_000_000, Employees: 250 },
} as const;

export const LEGAL_FRAMEWORK = {
  jurisdiction: 'Ireland',
  statute: 'Companies Act 2014 (as amended)',
  sizeThresholds: SIZE_THRESHOLDS,
  sizeTest:
    'A company qualifies for a size class when it meets at least two of the three limits (turnover, balance '
    + 'sheet total, average employees). It must qualify in two consecutive years to move between classes, '
    + 'and a company in its first financial year qualifies on that year alone. The current limits apply to '
    + 'financial years beginning on or after EffectiveFrom, or EarlyElectionEffectiveFrom where the directors '
    + 'elected early; earlier years use the historical limits.',
  microExclusions:
    'Holding companies, investment undertakings, subsidiaries included in consolidated statements and the '
    + 'ineligible entities of section 280F and Schedule 5 cannot use the micro regime.',
  filingRegimes: [
    'Micro: FRS 105, section 280D statement, abridged filing.',
    'Small: FRS 102 section 1A.',
    'Small Abridged: FRS 102 section 1A filed with a section 352 statement.',
    'Medium: FRS 102 in full, with some disclosure exemptions.',
    'Full: FRS 102 in full.',
  ],
  auditExemption:
    'Section 360 Companies Act 2014, lost where members holding 10% serve notice (section 334) or where '
    + 'returns are filed late.',
  directorLoans: 'Sections 236 to 245 (loans and related arrangements) and section 307 (disclosure).',
  directorsApproval: 'Section 324: the financial statements are approved and signed by the directors.',
  corporationTax:
    'Trading income at 12.5% and non-trading income at 25%; capital allowances for plant and machinery under '
    + 'section 284 TCA 1997 (12.5% over 8 years).',
  filingBoundary: 'CRO and ROS final submission remain external actions recorded in workflow state.',
  authority:
    'FilingBridge\'s classification, regime and deadline engines are the authority for a particular company. '
    + 'This summary is orientation, not advice.',
} as const;
