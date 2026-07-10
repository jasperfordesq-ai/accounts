"use client";

import { use, useCallback, useEffect, useState } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import { FinancialStatementsWorkbench } from "@/components/statements/FinancialStatementsWorkbench";
import {
  getBalanceSheet,
  getCashFlowStatement,
  getCompany,
  getDirectorsReportData,
  getEquityChanges,
  getPeriod,
  getProfitAndLoss,
  getStatementSources,
  getTaxComputation,
  getCorporationTaxFilingSupport,
  getTrialBalance,
  type AccountingPeriod,
  type BalanceSheet,
  type CashFlowStatement,
  type Company,
  type DirectorsReportData,
  type EquityChanges,
  type ProfitAndLoss,
  type StatementSourceSummary,
  type TaxComputation,
  type CorporationTaxFilingSupportResponse,
  type TrialBalanceLine,
} from "@/lib/api";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  type ResourceState,
} from "@/lib/resourceState";
import { useAuth } from "@/components/AuthProvider";
import {
  enumSearchParam,
  patchSearchHref,
  useLatestRequestSequence,
} from "@/lib/interactionState";
import {
  FINANCIAL_STATEMENT_TAB_ID_SET,
  type FinancialStatementTabId,
} from "@/lib/statementTabs";

export default function StatementsPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const { canReadInternalWorkingPapers } = useAuth();
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const shellRequestSequence = useLatestRequestSequence();
  const statementRequestSequence = useLatestRequestSequence();
  const cId = Number(companyId);
  const pId = Number(periodId);
  const selectedStatementTab = enumSearchParam(
    searchParams,
    "statementTab",
    FINANCIAL_STATEMENT_TAB_ID_SET,
    "trial-balance",
  );

  const handleStatementTabChange = useCallback((tab: FinancialStatementTabId) => {
    const currentSearch = typeof window === "undefined" ? searchParams.toString() : window.location.search;
    router.push(patchSearchHref(pathname, currentSearch, {
      statementTab: tab === "trial-balance" ? null : tab,
    }), { scroll: false });
  }, [pathname, router, searchParams]);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [trialBalance, setTrialBalance] = useState<TrialBalanceLine[] | null>(null);
  const [pnl, setPnl] = useState<ProfitAndLoss | null>(null);
  const [bs, setBs] = useState<BalanceSheet | null>(null);
  const [tax, setTax] = useState<TaxComputation | null>(null);
  const [taxFilingSupport, setTaxFilingSupport] = useState<CorporationTaxFilingSupportResponse | null>(null);
  const [cashFlow, setCashFlow] = useState<CashFlowStatement | null>(null);
  const [equity, setEquity] = useState<EquityChanges | null>(null);
  const [directorsReport, setDirectorsReport] = useState<DirectorsReportData | null>(null);
  const [sources, setSources] = useState<StatementSourceSummary[] | null>(null);
  const [shellState, setShellState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [statementState, setStatementState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);

  const loadShell = useCallback(async (onlyKeys?: string[]) => {
    const request = shellRequestSequence.begin();
    const loaders = {
      company: () => getCompany(cId),
      period: () => getPeriod(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setShellState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values.company) setCompany(result.values.company);
    if (result.values.period) setPeriod(result.values.period);
    if (result.failedResourceKeys.length === 0) setShellState(completeResourceLoad(false));
    else setShellState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId, shellRequestSequence]);

  const loadStatements = useCallback(async (onlyKeys?: string[]) => {
    const request = statementRequestSequence.begin();
    const loaders = {
      "trial-balance": () => getTrialBalance(cId, pId),
      pnl: () => getProfitAndLoss(cId, pId),
      "balance-sheet": () => getBalanceSheet(cId, pId),
      tax: () => getTaxComputation(cId, pId),
      "tax-filing-support": () => getCorporationTaxFilingSupport(cId, pId),
      "cash-flow": () => getCashFlowStatement(cId, pId),
      equity: () => getEquityChanges(cId, pId),
      "directors-report": () => getDirectorsReportData(cId, pId),
      sources: () => getStatementSources(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setStatementState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (!request.isLatest()) return;
    if (result.values["trial-balance"] !== undefined) setTrialBalance(result.values["trial-balance"]);
    if (result.values.pnl !== undefined) setPnl(result.values.pnl);
    if (result.values["balance-sheet"] !== undefined) setBs(result.values["balance-sheet"]);
    if (result.values.tax !== undefined) setTax(result.values.tax);
    if (result.values["tax-filing-support"] !== undefined) setTaxFilingSupport(result.values["tax-filing-support"]);
    if (result.values["cash-flow"] !== undefined) setCashFlow(result.values["cash-flow"]);
    if (result.values.equity !== undefined) setEquity(result.values.equity);
    if (result.values["directors-report"] !== undefined) setDirectorsReport(result.values["directors-report"]);
    if (result.values.sources !== undefined) setSources(result.values.sources);
    if (result.failedResourceKeys.length === 0) setStatementState(completeResourceLoad(false));
    else setStatementState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId, statementRequestSequence]);

  const loadData = useCallback(async () => {
    await Promise.all([loadShell(), loadStatements()]);
  }, [loadShell, loadStatements]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadData();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [loadData]);

  useEffect(() => () => {
    shellRequestSequence.invalidate();
    statementRequestSequence.invalidate();
  }, [shellRequestSequence, statementRequestSequence]);

  if (shellState.status === "loading" && !shellState.hasRetainedData) {
    return <PeriodWorkspaceSkeleton />;
  }

  return (
    <FinancialStatementsWorkbench
      company={company}
      period={period}
      companyId={companyId}
      periodId={periodId}
      error={shellState.error}
      shellState={shellState}
      statementState={statementState}
      trialBalance={trialBalance}
      pnl={pnl}
      bs={bs}
      tax={tax}
      taxFilingSupport={taxFilingSupport}
      cashFlow={cashFlow}
      equity={equity}
      directorsReport={directorsReport}
      sources={sources}
      onRetry={() => loadShell(shellState.failedResourceKeys)}
      onRetryStatements={() => loadStatements(statementState.failedResourceKeys)}
      canViewWorkingPapers={canReadInternalWorkingPapers}
      selectedStatementTab={selectedStatementTab}
      onStatementTabChange={handleStatementTabChange}
    />
  );
}
