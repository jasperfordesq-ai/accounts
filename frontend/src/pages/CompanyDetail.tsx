import { useEffect, useState } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { getCompany, deleteCompany, createPeriod, type Company } from '../api/companies';
import { Building2, Users, Calendar, Plus, Trash2, ArrowRight, ChevronLeft } from 'lucide-react';

export default function CompanyDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [showNewPeriod, setShowNewPeriod] = useState(false);
  const [periodStart, setPeriodStart] = useState('');
  const [periodEnd, setPeriodEnd] = useState('');
  const [isFirstYear, setIsFirstYear] = useState(false);

  const load = () => {
    getCompany(Number(id))
      .then(res => setCompany(res.data))
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [id]);

  const handleDelete = async () => {
    if (!confirm('Are you sure you want to delete this company and all its data?')) return;
    await deleteCompany(Number(id));
    navigate('/');
  };

  const handleCreatePeriod = async () => {
    if (!periodStart || !periodEnd) return;
    await createPeriod(Number(id), {
      periodStart, periodEnd, status: 'Draft', isFirstYear
    });
    setShowNewPeriod(false);
    setPeriodStart('');
    setPeriodEnd('');
    load();
  };

  if (loading) return <div className="text-center py-12 text-gray-500">Loading...</div>;
  if (!company) return <div className="text-center py-12 text-red-500">Company not found</div>;

  const statusColor = (status: string) => {
    switch(status) {
      case 'Draft': return 'bg-gray-100 text-gray-600';
      case 'Review': return 'bg-blue-50 text-blue-700';
      case 'Finalised': return 'bg-emerald-50 text-emerald-700';
      case 'Filed': return 'bg-purple-50 text-purple-700';
      default: return 'bg-gray-100 text-gray-600';
    }
  };

  return (
    <div>
      <Link to="/" className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4">
        <ChevronLeft className="w-4 h-4" /> Back to Dashboard
      </Link>

      <div className="flex items-start justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-3">
            <div className="bg-emerald-50 p-2 rounded-lg">
              <Building2 className="w-6 h-6 text-emerald-600" />
            </div>
            {company.legalName}
          </h1>
          {company.tradingName && <p className="text-gray-500 mt-1 ml-14">t/a {company.tradingName}</p>}
        </div>
        <button onClick={handleDelete} className="flex items-center gap-1 text-sm text-red-500 hover:text-red-700 px-3 py-1.5 border border-red-200 rounded-lg">
          <Trash2 className="w-3.5 h-3.5" /> Delete
        </button>
      </div>

      {/* Company info cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">Registration</h3>
          <div className="space-y-2 text-sm">
            <div><span className="text-gray-500">CRO:</span> <span className="text-gray-900 font-medium">{company.croNumber || '—'}</span></div>
            <div><span className="text-gray-500">Tax Ref:</span> <span className="text-gray-900 font-medium">{company.taxReference || '—'}</span></div>
            <div><span className="text-gray-500">Type:</span> <span className="text-gray-900 font-medium">{company.companyType}</span></div>
            <div><span className="text-gray-500">Incorporated:</span> <span className="text-gray-900 font-medium">{company.incorporationDate}</span></div>
          </div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3">Address</h3>
          <div className="text-sm text-gray-700 space-y-1">
            {company.registeredOfficeAddress1 && <div>{company.registeredOfficeAddress1}</div>}
            {company.registeredOfficeAddress2 && <div>{company.registeredOfficeAddress2}</div>}
            {company.registeredOfficeCity && <div>{company.registeredOfficeCity}</div>}
            {company.registeredOfficeCounty && <div>Co. {company.registeredOfficeCounty}</div>}
            {company.registeredOfficeEircode && <div>{company.registeredOfficeEircode}</div>}
            {!company.registeredOfficeAddress1 && <div className="text-gray-400">No address recorded</div>}
          </div>
        </div>
        <div className="bg-white rounded-xl border border-gray-200 p-5">
          <h3 className="text-xs font-semibold text-gray-400 uppercase tracking-wide mb-3 flex items-center gap-2">
            <Users className="w-3.5 h-3.5" /> Officers
          </h3>
          <div className="space-y-2">
            {company.officers && company.officers.length > 0 ? company.officers.map(o => (
              <div key={o.id} className="text-sm">
                <span className="text-gray-900 font-medium">{o.name}</span>
                <span className="text-gray-400 ml-2">({o.role})</span>
              </div>
            )) : (
              <div className="text-sm text-gray-400">No officers recorded</div>
            )}
          </div>
        </div>
      </div>

      {/* Accounting Periods */}
      <div className="bg-white rounded-xl border border-gray-200">
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-100">
          <h2 className="font-semibold text-gray-900 flex items-center gap-2">
            <Calendar className="w-4 h-4 text-gray-400" />
            Accounting Periods
          </h2>
          <button onClick={() => setShowNewPeriod(true)}
            className="flex items-center gap-1 text-sm bg-emerald-600 text-white px-3 py-1.5 rounded-lg hover:bg-emerald-700">
            <Plus className="w-3.5 h-3.5" /> New Period
          </button>
        </div>

        {showNewPeriod && (
          <div className="px-6 py-4 bg-emerald-50 border-b border-emerald-100 flex items-end gap-4">
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">Period Start</label>
              <input type="date" value={periodStart} onChange={e => setPeriodStart(e.target.value)}
                className="border border-gray-300 rounded-lg px-3 py-1.5 text-sm" />
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-600 mb-1">Period End</label>
              <input type="date" value={periodEnd} onChange={e => setPeriodEnd(e.target.value)}
                className="border border-gray-300 rounded-lg px-3 py-1.5 text-sm" />
            </div>
            <label className="flex items-center gap-2 text-sm text-gray-700">
              <input type="checkbox" checked={isFirstYear} onChange={e => setIsFirstYear(e.target.checked)} className="w-4 h-4 text-emerald-600 rounded" />
              First financial year
            </label>
            <button onClick={handleCreatePeriod} className="bg-emerald-600 text-white px-4 py-1.5 rounded-lg text-sm hover:bg-emerald-700">Create</button>
            <button onClick={() => setShowNewPeriod(false)} className="text-gray-500 text-sm hover:text-gray-700">Cancel</button>
          </div>
        )}

        {company.periods && company.periods.length > 0 ? (
          <div className="divide-y divide-gray-100">
            {company.periods.map(period => (
              <Link
                key={period.id}
                to={`/companies/${company.id}/periods/${period.id}`}
                className="flex items-center justify-between px-6 py-4 hover:bg-gray-50 group"
              >
                <div className="flex items-center gap-4">
                  <div>
                    <span className="text-sm font-medium text-gray-900">{period.periodStart} — {period.periodEnd}</span>
                    {period.isFirstYear && <span className="ml-2 text-xs text-amber-600 font-medium">(First Year)</span>}
                  </div>
                  <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${statusColor(period.status)}`}>
                    {period.status}
                  </span>
                  {period.sizeClassification && (
                    <span className="text-xs text-gray-400">
                      Size: {period.sizeClassification.calculatedClass}
                    </span>
                  )}
                </div>
                <ArrowRight className="w-4 h-4 text-gray-300 group-hover:text-emerald-500" />
              </Link>
            ))}
          </div>
        ) : (
          <div className="px-6 py-12 text-center text-gray-400 text-sm">
            No accounting periods yet. Create one to start preparing accounts.
          </div>
        )}
      </div>
    </div>
  );
}
