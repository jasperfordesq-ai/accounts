import { Outlet, Link, useLocation } from 'react-router-dom';
import { Building2, LayoutDashboard } from 'lucide-react';

export default function Layout() {
  const location = useLocation();

  return (
    <div className="min-h-screen bg-gray-50">
      <nav className="bg-white border-b border-gray-200 px-6 py-3">
        <div className="max-w-7xl mx-auto flex items-center justify-between">
          <Link to="/" className="flex items-center gap-2 text-lg font-semibold text-gray-900">
            <Building2 className="w-6 h-6 text-emerald-600" />
            Irish Accounts
          </Link>
          <div className="flex items-center gap-4">
            <Link
              to="/"
              className={`flex items-center gap-1 px-3 py-1.5 rounded-md text-sm ${
                location.pathname === '/' ? 'bg-emerald-50 text-emerald-700' : 'text-gray-600 hover:text-gray-900'
              }`}
            >
              <LayoutDashboard className="w-4 h-4" />
              Dashboard
            </Link>
          </div>
        </div>
      </nav>
      <main className="max-w-7xl mx-auto px-6 py-8">
        <Outlet />
      </main>
    </div>
  );
}
