// Year-end disclosure records (post balance sheet events, related party transactions, contingent
// liabilities): the shapes the year-end questionnaire and its API callers share. Kept out of api.ts
// so that facade stays within its module budget.

export interface PostBalanceSheetEvent {
  id?: number;
  periodId?: number;
  description: string;
  eventDate: string;
  isAdjusting: boolean;
  financialImpact?: number;
  actionRequired?: string;
}

export interface RelatedPartyTransaction {
  id?: number;
  periodId?: number;
  partyName: string;
  relationship: string;
  transactionType: string;
  amount: number;
  balanceOwed?: number;
  terms?: string;
}

export interface ContingentLiability {
  id?: number;
  periodId?: number;
  description: string;
  nature: string;
  estimatedAmount?: number;
  likelihood: string;
}
