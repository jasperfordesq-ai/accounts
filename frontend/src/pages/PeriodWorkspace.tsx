import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { getPeriod, getCompany, type AccountingPeriod, type Company } from '../api/companies';
import { ChevronLeft, Upload, HelpCircle, Settings, FileText, Download, Calculator } from 'lucide-react';

const TABS = [
  { id: 'import', label: 'Import', icon: Upload },
  { id: 'categorise', label: 'Categorise', icon: Settings },
  { id: 'questions', label: 'Year-End Questions', icon: HelpCircle },
  { id: 'adjustments', label: 'Adjustments', icon: Calculator },
  { id: 'statements', label: 'Statements', icon: FileText },
  { id: 'filing', label: 'Filing', icon: Download },
];

export default function PeriodWorkspace() {
  const { companyId, periodId } = useParams<{ companyId: string; periodId: string }>();
  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [activeTab, setActiveTab] = useState('import');
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([
      getCompany(Number(companyId)),
      getPeriod(Number(companyId), Number(periodId))
    ]).then(([compRes, periodRes]) => {
      setCompany(compRes.data);
      setPeriod(periodRes.data);
    }).catch(console.error)
    .finally(() => setLoading(false));
  }, [companyId, periodId]);

  if (loading) return <div className="text-center py-12 text-gray-500">Loading workspace...</div>;
  if (!company || !period) return <div className="text-center py-12 text-red-500">Not found</div>;

  return (
    <div>
      <Link to={`/companies/${companyId}`} className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4">
        <ChevronLeft className="w-4 h-4" /> {company.legalName}
      </Link>

      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold text-gray-900">
            Period: {period.periodStart} — {period.periodEnd}
          </h1>
          <div className="flex items-center gap-3 mt-1">
            <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${
              period.status === 'Draft' ? 'bg-gray-100 text-gray-600' :
              period.status === 'Review' ? 'bg-blue-50 text-blue-700' :
              period.status === 'Finalised' ? 'bg-emerald-50 text-emerald-700' :
              'bg-purple-50 text-purple-700'
            }`}>{period.status}</span>
            {period.sizeClassification && (
              <span className="text-sm text-gray-500">
                Classification: <strong>{period.sizeClassification.calculatedClass}</strong>
              </span>
            )}
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-gray-100 rounded-lg p-1 mb-6">
        {TABS.map(tab => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={`flex items-center gap-2 px-4 py-2 rounded-md text-sm font-medium transition-colors ${
              activeTab === tab.id
                ? 'bg-white text-emerald-700 shadow-sm'
                : 'text-gray-600 hover:text-gray-900'
            }`}
          >
            <tab.icon className="w-4 h-4" />
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content (placeholders — will be built in later phases) */}
      <div className="bg-white rounded-xl border border-gray-200 p-8">
        {activeTab === 'import' && (
          <div className="text-center py-12 text-gray-400">
            <Upload className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Import Bank Transactions</h3>
            <p className="text-sm">Upload CSV files from your bank to import transactions for this period.</p>
          </div>
        )}
        {activeTab === 'categorise' && (
          <div className="text-center py-12 text-gray-400">
            <Settings className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Categorise Transactions</h3>
            <p className="text-sm">Review and categorise imported transactions by account type.</p>
          </div>
        )}
        {activeTab === 'questions' && (
          <div className="text-center py-12 text-gray-400">
            <HelpCircle className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Year-End Questions</h3>
            <p className="text-sm">Answer questions about debtors, creditors, assets, loans, payroll, and tax.</p>
          </div>
        )}
        {activeTab === 'adjustments' && (
          <div className="text-center py-12 text-gray-400">
            <Calculator className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Adjustments</h3>
            <p className="text-sm">Review auto-generated adjustments and add manual entries.</p>
          </div>
        )}
        {activeTab === 'statements' && (
          <div className="text-center py-12 text-gray-400">
            <FileText className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Financial Statements</h3>
            <p className="text-sm">Preview trial balance, P&L, and balance sheet.</p>
          </div>
        )}
        {activeTab === 'filing' && (
          <div className="text-center py-12 text-gray-400">
            <Download className="w-10 h-10 mx-auto mb-3 text-gray-300" />
            <h3 className="text-lg font-medium text-gray-600 mb-2">Filing Package</h3>
            <p className="text-sm">Generate and download CRO filing package and Revenue CT1 support.</p>
          </div>
        )}
      </div>
    </div>
  );
}
