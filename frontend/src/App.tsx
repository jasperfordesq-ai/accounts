import { Routes, Route } from 'react-router-dom';
import Dashboard from './pages/Dashboard';
import CompanyOnboarding from './pages/CompanyOnboarding';
import CompanyDetail from './pages/CompanyDetail';
import PeriodWorkspace from './pages/PeriodWorkspace';
import Layout from './components/Layout';

export default function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<Dashboard />} />
        <Route path="/companies/new" element={<CompanyOnboarding />} />
        <Route path="/companies/:id" element={<CompanyDetail />} />
        <Route path="/companies/:companyId/periods/:periodId" element={<PeriodWorkspace />} />
      </Route>
    </Routes>
  );
}
