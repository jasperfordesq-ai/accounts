import api from './client';

export interface Company {
  id?: number;
  legalName: string;
  tradingName?: string;
  croNumber?: string;
  taxReference?: string;
  companyType: string;
  incorporationDate: string;
  financialYearStartMonth: number;
  ardMonth: number;
  registeredOfficeAddress1?: string;
  registeredOfficeAddress2?: string;
  registeredOfficeCity?: string;
  registeredOfficeCounty?: string;
  registeredOfficeEircode?: string;
  isGroupMember: boolean;
  isHolding: boolean;
  isInvestment: boolean;
  isSubsidiary: boolean;
  isDormant: boolean;
  isTrading: boolean;
  isVatRegistered: boolean;
  isEmployer: boolean;
  hasStock: boolean;
  ownsAssets: boolean;
  hasBorrowings: boolean;
  hasDirectorLoans: boolean;
  createdAt?: string;
  updatedAt?: string;
  officers?: Officer[];
  periods?: AccountingPeriod[];
  periodCount?: number;
}

export interface Officer {
  id?: number;
  companyId?: number;
  name: string;
  role: string;
  appointedDate?: string;
  resignedDate?: string;
  address?: string;
}

export interface AccountingPeriod {
  id?: number;
  companyId?: number;
  periodStart: string;
  periodEnd: string;
  status: string;
  isFirstYear: boolean;
  lockedAt?: string;
  lockedBy?: string;
  sizeClassification?: SizeClassification;
  filingRegime?: FilingRegime;
}

export interface SizeClassification {
  id?: number;
  periodId: number;
  turnover: number;
  balanceSheetTotal: number;
  avgEmployees: number;
  priorYearClass?: string;
  calculatedClass: string;
  overrideClass?: string;
  overrideReason?: string;
  qualificationNotes?: string;
}

export interface FilingRegime {
  id?: number;
  periodId: number;
  canUseMicro: boolean;
  canFileAbridged: boolean;
  auditExempt: boolean;
  electedRegime: string;
}

// Companies
export const getCompanies = () => api.get<Company[]>('/companies');
export const getCompany = (id: number) => api.get<Company>(`/companies/${id}`);
export const createCompany = (data: Company) => api.post<Company>('/companies', data);
export const updateCompany = (id: number, data: Company) => api.put<Company>(`/companies/${id}`, data);
export const deleteCompany = (id: number) => api.delete(`/companies/${id}`);

// Officers
export const getOfficers = (companyId: number) => api.get<Officer[]>(`/companies/${companyId}/officers`);
export const createOfficer = (companyId: number, data: Officer) => api.post<Officer>(`/companies/${companyId}/officers`, data);
export const updateOfficer = (companyId: number, id: number, data: Officer) => api.put<Officer>(`/companies/${companyId}/officers/${id}`, data);
export const deleteOfficer = (companyId: number, id: number) => api.delete(`/companies/${companyId}/officers/${id}`);

// Periods
export const getPeriods = (companyId: number) => api.get<AccountingPeriod[]>(`/companies/${companyId}/periods`);
export const getPeriod = (companyId: number, id: number) => api.get<AccountingPeriod>(`/companies/${companyId}/periods/${id}`);
export const createPeriod = (companyId: number, data: AccountingPeriod) => api.post<AccountingPeriod>(`/companies/${companyId}/periods`, data);
export const updatePeriodStatus = (companyId: number, id: number, status: string, lockedBy?: string) =>
  api.put(`/companies/${companyId}/periods/${id}/status`, { status, lockedBy });
