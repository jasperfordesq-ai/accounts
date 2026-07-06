"use client";

import { use, useCallback, useEffect, useState } from "react";
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
  type TrialBalanceLine,
} from "@/lib/api";

export default function StatementsPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [trialBalance, setTrialBalance] = useState<TrialBalanceLine[] | null>(null);
  const [pnl, setPnl] = useState<ProfitAndLoss | null>(null);
  const [bs, setBs] = useState<BalanceSheet | null>(null);
  const [tax, setTax] = useState<TaxComputation | null>(null);
  const [cashFlow, setCashFlow] = useState<CashFlowStatement | null>(null);
  const [equity, setEquity] = useState<EquityChanges | null>(null);
  const [directorsReport, setDirectorsReport] = useState<DirectorsReportData | null>(null);
  const [sources, setSources] = useState<StatementSourceSummary[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      const results = await Promise.allSettled([
        getTrialBalance(cId, pId),
        getProfitAndLoss(cId, pId),
        getBalanceSheet(cId, pId),
        getTaxComputation(cId, pId),
        getCashFlowStatement(cId, pId),
        getEquityChanges(cId, pId),
        getDirectorsReportData(cId, pId),
        getStatementSources(cId, pId),
      ]);

      setTrialBalance(results[0].status === "fulfilled" ? results[0].value : null);
      setPnl(results[1].status === "fulfilled" ? results[1].value : null);
      setBs(results[2].status === "fulfilled" ? results[2].value : null);
      setTax(results[3].status === "fulfilled" ? results[3].value : null);
      setCashFlow(results[4].status === "fulfilled" ? results[4].value : null);
      setEquity(results[5].status === "fulfilled" ? results[5].value : null);
      setDirectorsReport(results[6].status === "fulfilled" ? results[6].value : null);
      setSources(results[7].status === "fulfilled" ? results[7].value : null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  if (loading) {
    return <PeriodWorkspaceSkeleton />;
  }

  return (
    <FinancialStatementsWorkbench
      company={company}
      period={period}
      companyId={companyId}
      periodId={periodId}
      error={error}
      trialBalance={trialBalance}
      pnl={pnl}
      bs={bs}
      tax={tax}
      cashFlow={cashFlow}
      equity={equity}
      directorsReport={directorsReport}
      sources={sources}
      onRetry={loadData}
    />
  );
}
