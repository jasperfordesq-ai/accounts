"use client";

import { use, useState, useEffect, useCallback } from "react";
import Link from "next/link";
import {
  Button,
  Card,
  Chip,
  Spinner,
} from "@heroui/react";
import {
  ArrowLeft,
  Heart,
  Plus,
  Trash2,
  RefreshCw,
  FileText,
  Users,
  DollarSign,
  Globe,
  Shield,
} from "lucide-react";
import { toast } from "sonner";
import {
  getCompany,
  getPeriod,
  getSofa,
  getTrusteesReport,
  getFundBalances,
  createFundBalance,
  deleteFundBalance,
  type Company,
  type AccountingPeriod,
  type SofaData,
  type TrusteesReportData,
  type FundBalance,
} from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";

function eur(amount: number): string {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount);
}

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";
const selectClass = inputClass;

export default function CharityReportingPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [sofa, setSofa] = useState<SofaData | null>(null);
  const [tar, setTar] = useState<TrusteesReportData | null>(null);
  const [funds, setFunds] = useState<FundBalance[]>([]);
  const [loading, setLoading] = useState(true);

  // Add fund form
  const [showAddFund, setShowAddFund] = useState(false);
  const [newFundName, setNewFundName] = useState("");
  const [newFundType, setNewFundType] = useState("Unrestricted");
  const [newOpening, setNewOpening] = useState(0);
  const [newIncoming, setNewIncoming] = useState(0);
  const [newExpended, setNewExpended] = useState(0);
  const [newTransfers, setNewTransfers] = useState(0);
  const [newGains, setNewGains] = useState(0);
  const [addingFund, setAddingFund] = useState(false);

  // Active tab
  const [activeTab, setActiveTab] = useState<"sofa" | "funds" | "tar">("sofa");

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      try { const s = await getSofa(cId, pId); setSofa(s); } catch {}
      try { const t = await getTrusteesReport(cId, pId); setTar(t); } catch {}
      try { const f = await getFundBalances(cId, pId); setFunds(f); } catch {}
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId]);

  useEffect(() => { loadData(); }, [loadData]);

  async function handleAddFund() {
    if (!newFundName.trim()) { toast.error("Fund name is required"); return; }
    setAddingFund(true);
    try {
      await createFundBalance(cId, pId, {
        fundName: newFundName.trim(),
        fundType: newFundType,
        openingBalance: newOpening,
        incomingResources: newIncoming,
        resourcesExpended: newExpended,
        transfers: newTransfers,
        gainsLosses: newGains,
        closingBalance: 0, // Computed by backend
      });
      toast.success("Fund added");
      setNewFundName(""); setNewOpening(0); setNewIncoming(0); setNewExpended(0); setNewTransfers(0); setNewGains(0);
      setShowAddFund(false);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add fund");
    } finally {
      setAddingFund(false);
    }
  }

  async function handleDeleteFund(id: number) {
    try {
      await deleteFundBalance(cId, pId, id);
      toast.success("Fund removed");
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete fund");
    }
  }

  if (loading) return <PeriodWorkspaceSkeleton />;

  return (
    <div className="animate-fade-in">
      <Breadcrumbs
        items={[
          { label: company?.legalName ?? "Company", href: `/companies/${companyId}` },
          { label: "Period", href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Charity Reporting" },
        ]}
      />

      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-emerald-700 hover:text-emerald-800 dark:text-emerald-400 dark:hover:text-emerald-300 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100 flex items-center gap-2">
          <Heart className="w-7 h-7 text-emerald-600 dark:text-emerald-400" />
          Charity Reporting
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
          {company?.legalName} &mdash; SORP {tar ? `Tier ${tar.sorpTier}` : ""} &mdash;{" "}
          {period ? `${new Date(period.periodStart).toLocaleDateString("en-IE")} to ${new Date(period.periodEnd).toLocaleDateString("en-IE")}` : ""}
        </p>
      </div>

      {/* Tab Navigation */}
      <div className="flex gap-1 border-b border-gray-200 dark:border-neutral-700 mb-6 no-print">
        {[
          { id: "sofa" as const, label: "Statement of Financial Activities", icon: DollarSign },
          { id: "funds" as const, label: "Fund Balances", icon: FileText },
          { id: "tar" as const, label: "Trustees' Annual Report", icon: Users },
        ].map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 cursor-pointer outline-none transition-colors ${
              activeTab === tab.id
                ? "border-emerald-600 text-emerald-700 dark:text-emerald-400"
                : "border-transparent text-gray-600 dark:text-gray-400 hover:text-gray-900 dark:hover:text-gray-200"
            }`}
          >
            <tab.icon className="w-4 h-4 inline mr-1.5 -mt-0.5" />
            {tab.label}
          </button>
        ))}
      </div>

      {/* SoFA Tab */}
      {activeTab === "sofa" && (
        <div className="space-y-6">
          {sofa ? (
            <>
              {/* Income & Expenditure Summary */}
              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Total Income</p>
                    <p className="text-xl font-bold text-emerald-700 dark:text-emerald-400 mt-1">{eur(sofa.totalIncoming)}</p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Total Expenditure</p>
                    <p className="text-xl font-bold text-red-700 dark:text-red-400 mt-1">{eur(sofa.totalExpended)}</p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Net Movement</p>
                    <p className={`text-xl font-bold mt-1 ${sofa.netMovement >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>
                      {eur(sofa.netMovement)}
                    </p>
                  </Card.Content>
                </Card>
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="p-4 text-center">
                    <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Total Funds</p>
                    <p className="text-xl font-bold text-gray-900 dark:text-gray-100 mt-1">{eur(sofa.totalClosingFunds)}</p>
                  </Card.Content>
                </Card>
              </div>

              {/* Fund Columns */}
              {[
                { title: "Unrestricted Funds", funds: sofa.unrestrictedFunds },
                { title: "Restricted Funds", funds: sofa.restrictedFunds },
                { title: "Endowment Funds", funds: sofa.endowmentFunds },
              ].map(({ title, funds: fundList }) => (
                fundList.length > 0 && (
                  <Card key={title} className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                    <Card.Header>
                      <Card.Title className="text-gray-900 dark:text-gray-100">{title}</Card.Title>
                    </Card.Header>
                    <Card.Content>
                      <div className="overflow-x-auto">
                        <table className="w-full text-sm">
                          <thead>
                            <tr className="bg-gray-50 dark:bg-neutral-800 text-xs font-medium text-gray-600 dark:text-gray-400 uppercase tracking-wide">
                              <th className="text-left px-3 py-2">Fund</th>
                              <th className="text-right px-3 py-2">Opening</th>
                              <th className="text-right px-3 py-2">Incoming</th>
                              <th className="text-right px-3 py-2">Expended</th>
                              <th className="text-right px-3 py-2">Transfers</th>
                              <th className="text-right px-3 py-2">Gains/Losses</th>
                              <th className="text-right px-3 py-2">Closing</th>
                            </tr>
                          </thead>
                          <tbody>
                            {fundList.map((f, idx) => (
                              <tr key={idx} className={`border-t border-gray-100 dark:border-neutral-700 ${idx % 2 === 1 ? "bg-gray-50/50 dark:bg-neutral-800/25" : ""}`}>
                                <td className="px-3 py-2 text-gray-900 dark:text-gray-100 font-medium">{f.fundName}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.openingBalance)}</td>
                                <td className="px-3 py-2 text-right font-mono text-emerald-700 dark:text-emerald-400">{eur(f.incomingResources)}</td>
                                <td className="px-3 py-2 text-right font-mono text-red-700 dark:text-red-400">{eur(f.resourcesExpended)}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.transfers)}</td>
                                <td className="px-3 py-2 text-right font-mono text-gray-700 dark:text-gray-300">{eur(f.gainsLosses)}</td>
                                <td className="px-3 py-2 text-right font-mono font-bold text-gray-900 dark:text-gray-100">{eur(f.closingBalance)}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </Card.Content>
                  </Card>
                )
              ))}

              {sofa.unrestrictedFunds.length === 0 && sofa.restrictedFunds.length === 0 && sofa.endowmentFunds.length === 0 && (
                <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                  <Card.Content className="text-center py-12">
                    <DollarSign className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                    <p className="text-sm text-gray-500 dark:text-gray-400">No fund balances entered yet.</p>
                    <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Go to the Fund Balances tab to add funds.</p>
                  </Card.Content>
                </Card>
              )}
            </>
          ) : (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Content className="text-center py-12">
                <DollarSign className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                <p className="text-sm text-gray-500 dark:text-gray-400">Statement of Financial Activities not available.</p>
                <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Add fund balances first, then the SoFA will be generated automatically.</p>
              </Card.Content>
            </Card>
          )}
        </div>
      )}

      {/* Fund Balances Tab */}
      {activeTab === "funds" && (
        <div className="space-y-6">
          <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
            <Card.Header>
              <div className="flex items-center justify-between w-full">
                <div>
                  <Card.Title className="text-gray-900 dark:text-gray-100">Fund Balances</Card.Title>
                  <Card.Description>{funds.length} fund{funds.length !== 1 ? "s" : ""} configured</Card.Description>
                </div>
                <div className="flex items-center gap-2">
                  <Button variant="primary" size="sm" onPress={() => setShowAddFund(true)}>
                    <Plus className="w-4 h-4 mr-1" /> Add Fund
                  </Button>
                  <Button variant="ghost" size="sm" onPress={loadData}>
                    <RefreshCw className="w-4 h-4" />
                  </Button>
                </div>
              </div>
            </Card.Header>
            <Card.Content>
              {showAddFund && (
                <div className="mb-6 p-4 rounded-lg border border-emerald-200 dark:border-emerald-800 bg-emerald-50/30 dark:bg-emerald-900/10 animate-slide-down">
                  <h4 className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-3">Add New Fund</h4>
                  <div className="grid grid-cols-2 md:grid-cols-3 gap-3">
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Fund Name *</label>
                      <input className={inputClass} value={newFundName} onChange={(e) => setNewFundName(e.target.value)} placeholder="e.g. General Fund" />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Fund Type</label>
                      <select className={selectClass} value={newFundType} onChange={(e) => setNewFundType(e.target.value)} title="Fund type" aria-label="Fund type">
                        <option value="Unrestricted">Unrestricted</option>
                        <option value="Designated">Designated</option>
                        <option value="Restricted">Restricted</option>
                        <option value="Endowment">Endowment</option>
                      </select>
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Opening Balance</label>
                      <input type="number" className={inputClass} value={newOpening || ""} onChange={(e) => setNewOpening(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Incoming Resources</label>
                      <input type="number" className={inputClass} value={newIncoming || ""} onChange={(e) => setNewIncoming(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Resources Expended</label>
                      <input type="number" className={inputClass} value={newExpended || ""} onChange={(e) => setNewExpended(Number(e.target.value))} placeholder="0.00" />
                    </div>
                    <div>
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Transfers</label>
                      <input type="number" className={inputClass} value={newTransfers || ""} onChange={(e) => setNewTransfers(Number(e.target.value))} placeholder="0.00" />
                    </div>
                  </div>
                  <div className="flex items-center gap-3 mt-4">
                    <Button variant="primary" size="sm" onPress={handleAddFund} isDisabled={addingFund || !newFundName.trim()}>
                      {addingFund ? <Spinner size="sm" /> : <><Plus className="w-4 h-4 mr-1" /> Create Fund</>}
                    </Button>
                    <Button variant="ghost" size="sm" onPress={() => setShowAddFund(false)}>Cancel</Button>
                  </div>
                </div>
              )}

              {funds.length === 0 ? (
                <div className="text-center py-8">
                  <FileText className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                  <p className="text-sm text-gray-500 dark:text-gray-400">No fund balances recorded.</p>
                  <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">Add unrestricted, restricted, and endowment funds to generate the SoFA.</p>
                </div>
              ) : (
                <div className="space-y-3">
                  {funds.map((fund) => (
                    <div
                      key={fund.id}
                      className="flex items-center justify-between rounded-lg border border-gray-200 dark:border-neutral-700 px-4 py-3 hover:bg-gray-50 dark:hover:bg-neutral-800/50 transition-colors"
                    >
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">{fund.fundName}</p>
                          <Chip size="sm" variant="soft" color={
                            fund.fundType === "Restricted" ? "accent" :
                            fund.fundType === "Endowment" ? "warning" :
                            fund.fundType === "Designated" ? "default" : "success"
                          }>
                            {fund.fundType}
                          </Chip>
                        </div>
                        <div className="flex items-center gap-4 text-xs text-gray-500 dark:text-gray-400">
                          <span>Opening: {eur(fund.openingBalance)}</span>
                          <span className="text-emerald-600 dark:text-emerald-400">In: {eur(fund.incomingResources)}</span>
                          <span className="text-red-600 dark:text-red-400">Out: {eur(fund.resourcesExpended)}</span>
                          <span className="font-medium text-gray-900 dark:text-gray-100">Closing: {eur(fund.closingBalance)}</span>
                        </div>
                      </div>
                      <Button
                        variant="ghost"
                        size="sm"
                        isIconOnly
                        onPress={() => handleDeleteFund(fund.id!)}
                        aria-label={`Delete fund ${fund.fundName}`}
                      >
                        <Trash2 className="w-4 h-4 text-red-400 hover:text-red-600" />
                      </Button>
                    </div>
                  ))}
                </div>
              )}
            </Card.Content>
          </Card>
        </div>
      )}

      {/* Trustees' Annual Report Tab */}
      {activeTab === "tar" && (
        <div className="space-y-6">
          {tar ? (
            <>
              {/* Header Info */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Content className="p-5">
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <div>
                      <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Charity Number</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.charityNumber || "—"}</p>
                    </div>
                    <div>
                      <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">SORP Tier</p>
                      <Chip size="sm" variant="soft" color={tar.sorpTier === 1 ? "success" : tar.sorpTier === 2 ? "warning" : "danger"} className="mt-1">
                        Tier {tar.sorpTier}
                      </Chip>
                    </div>
                    <div>
                      <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Filing Deadline</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.filingDeadline}</p>
                    </div>
                    <div>
                      <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">Trustees</p>
                      <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mt-1">{tar.trusteeNames.length}</p>
                    </div>
                  </div>
                </Card.Content>
              </Card>

              {/* Trustees */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Trustees</Card.Title></Card.Header>
                <Card.Content>
                  <div className="flex flex-wrap gap-2">
                    {tar.trusteeNames.map((name, i) => (
                      <Chip key={i} size="sm" variant="soft" color="default">
                        <Users className="w-3 h-3" /> {name}
                      </Chip>
                    ))}
                  </div>
                </Card.Content>
              </Card>

              {/* Objectives & Activities */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Objectives &amp; Activities</Card.Title></Card.Header>
                <Card.Content className="space-y-3">
                  <div>
                    <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Charitable Objectives</p>
                    <p className="text-sm text-gray-900 dark:text-gray-100">{tar.charitableObjectives}</p>
                  </div>
                  <div>
                    <p className="text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide mb-1">Principal Activities</p>
                    <p className="text-sm text-gray-900 dark:text-gray-100">{tar.principalActivities}</p>
                  </div>
                </Card.Content>
              </Card>

              {/* Financial Review */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Financial Review</Card.Title></Card.Header>
                <Card.Content>
                  <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                    <div className="rounded-lg bg-emerald-50 dark:bg-emerald-900/20 p-4 text-center">
                      <p className="text-xs text-gray-500 dark:text-gray-400">Total Income</p>
                      <p className="text-lg font-bold text-emerald-700 dark:text-emerald-400 mt-1">{eur(tar.totalIncome)}</p>
                    </div>
                    <div className="rounded-lg bg-red-50 dark:bg-red-900/20 p-4 text-center">
                      <p className="text-xs text-gray-500 dark:text-gray-400">Total Expenditure</p>
                      <p className="text-lg font-bold text-red-700 dark:text-red-400 mt-1">{eur(tar.totalExpenditure)}</p>
                    </div>
                    <div className="rounded-lg bg-blue-50 dark:bg-blue-900/20 p-4 text-center">
                      <p className="text-xs text-gray-500 dark:text-gray-400">Net Movement</p>
                      <p className={`text-lg font-bold mt-1 ${tar.netMovement >= 0 ? "text-emerald-700 dark:text-emerald-400" : "text-red-700 dark:text-red-400"}`}>{eur(tar.netMovement)}</p>
                    </div>
                    <div className="rounded-lg bg-gray-50 dark:bg-neutral-800 p-4 text-center">
                      <p className="text-xs text-gray-500 dark:text-gray-400">Closing Funds</p>
                      <p className="text-lg font-bold text-gray-900 dark:text-gray-100 mt-1">{eur(tar.closingFunds)}</p>
                    </div>
                  </div>
                </Card.Content>
              </Card>

              {/* Governance & Compliance */}
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
                <Card.Header><Card.Title className="text-gray-900 dark:text-gray-100">Governance &amp; Compliance</Card.Title></Card.Header>
                <Card.Content className="space-y-4">
                  <div className="flex items-center gap-3">
                    <Shield className={`w-5 h-5 ${tar.governanceCodeCompliant ? "text-emerald-500" : "text-red-500"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      Charities Governance Code: {tar.governanceCodeCompliant ? "Compliant" : "Non-compliant"}
                    </span>
                  </div>
                  {tar.governanceCodeNote && (
                    <p className="text-sm text-gray-500 dark:text-gray-400 pl-8">{tar.governanceCodeNote}</p>
                  )}

                  <div className="flex items-center gap-3">
                    <DollarSign className={`w-5 h-5 ${tar.trusteeRemunerationPaid ? "text-amber-500" : "text-emerald-500"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      Trustee Remuneration: {tar.trusteeRemunerationPaid ? eur(tar.trusteeRemunerationAmount) : "None paid"}
                    </span>
                  </div>
                  {tar.trusteeExpensesDetails && (
                    <p className="text-sm text-gray-500 dark:text-gray-400 pl-8">{tar.trusteeExpensesDetails}</p>
                  )}

                  <div className="flex items-center gap-3">
                    <Globe className={`w-5 h-5 ${tar.hasInternationalTransfers ? "text-amber-500" : "text-gray-400 dark:text-gray-600"}`} />
                    <span className="text-sm text-gray-900 dark:text-gray-100">
                      International Transfers: {tar.hasInternationalTransfers ? "Yes" : "None"}
                    </span>
                  </div>
                  {tar.internationalTransferDetails && (
                    <p className="text-sm text-gray-500 dark:text-gray-400 pl-8">{tar.internationalTransferDetails}</p>
                  )}
                </Card.Content>
              </Card>
            </>
          ) : (
            <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
              <Card.Content className="text-center py-12">
                <Users className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
                <p className="text-sm text-gray-500 dark:text-gray-400">Trustees&apos; Annual Report not available.</p>
                <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
                  Configure charity info on the company page and add fund balances first.
                </p>
              </Card.Content>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}
