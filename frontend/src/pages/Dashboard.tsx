import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { Plus, Building2, Calendar, ArrowRight } from 'lucide-react';
import { getCompanies, type Company } from '../api/companies';

export default function Dashboard() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getCompanies()
      .then(res => setCompanies(res.data))
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="text-center py-12 text-gray-500">Loading companies...</div>;

  return (
    <div>
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Companies</h1>
          <p className="text-gray-500 mt-1">Manage your Irish company accounts</p>
        </div>
        <Link
          to="/companies/new"
          className="flex items-center gap-2 bg-emerald-600 text-white px-4 py-2 rounded-lg hover:bg-emerald-700 transition-colors"
        >
          <Plus className="w-4 h-4" />
          Add Company
        </Link>
      </div>

      {companies.length === 0 ? (
        <div className="text-center py-20 bg-white rounded-xl border border-gray-200">
          <Building2 className="w-12 h-12 text-gray-300 mx-auto mb-4" />
          <h3 className="text-lg font-medium text-gray-900 mb-2">No companies yet</h3>
          <p className="text-gray-500 mb-6">Add your first company to get started with accounts preparation.</p>
          <Link
            to="/companies/new"
            className="inline-flex items-center gap-2 bg-emerald-600 text-white px-4 py-2 rounded-lg hover:bg-emerald-700"
          >
            <Plus className="w-4 h-4" />
            Add Company
          </Link>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {companies.map(company => (
            <Link
              key={company.id}
              to={`/companies/${company.id}`}
              className="bg-white rounded-xl border border-gray-200 p-6 hover:border-emerald-300 hover:shadow-md transition-all group"
            >
              <div className="flex items-start justify-between mb-3">
                <div className="bg-emerald-50 p-2 rounded-lg">
                  <Building2 className="w-5 h-5 text-emerald-600" />
                </div>
                <ArrowRight className="w-4 h-4 text-gray-300 group-hover:text-emerald-500 transition-colors" />
              </div>
              <h3 className="font-semibold text-gray-900 mb-1">{company.legalName}</h3>
              {company.tradingName && (
                <p className="text-sm text-gray-500 mb-2">t/a {company.tradingName}</p>
              )}
              <div className="flex items-center gap-4 text-xs text-gray-400 mt-4">
                {company.croNumber && <span>CRO: {company.croNumber}</span>}
                <span className="flex items-center gap-1">
                  <Calendar className="w-3 h-3" />
                  {company.periodCount || 0} period{(company.periodCount || 0) !== 1 ? 's' : ''}
                </span>
              </div>
              <div className="mt-3">
                <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${
                  company.isTrading
                    ? 'bg-green-50 text-green-700'
                    : company.isDormant
                    ? 'bg-gray-100 text-gray-600'
                    : 'bg-yellow-50 text-yellow-700'
                }`}>
                  {company.isTrading ? 'Trading' : company.isDormant ? 'Dormant' : 'Inactive'}
                </span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
