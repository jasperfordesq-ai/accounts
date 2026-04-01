import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createCompany, createOfficer, type Company, type Officer } from '../api/companies';
import { ChevronLeft, ChevronRight, Check, Building2, Users, MapPin, Settings } from 'lucide-react';

const COMPANY_TYPES = [
  { value: 'Private', label: 'Private Company Limited by Shares (LTD)' },
  { value: 'PrivateUnlimited', label: 'Private Unlimited Company (ULC)' },
  { value: 'DesignatedActivityCompany', label: 'Designated Activity Company (DAC)' },
  { value: 'CompanyLimitedByGuarantee', label: 'Company Limited by Guarantee (CLG)' },
  { value: 'PublicLimitedCompany', label: 'Public Limited Company (PLC)' },
];

const MONTHS = ['January','February','March','April','May','June','July','August','September','October','November','December'];
const COUNTIES = ['Carlow','Cavan','Clare','Cork','Donegal','Dublin','Galway','Kerry','Kildare','Kilkenny','Laois','Leitrim','Limerick','Longford','Louth','Mayo','Meath','Monaghan','Offaly','Roscommon','Sligo','Tipperary','Waterford','Westmeath','Wexford','Wicklow'];

const steps = [
  { title: 'Legal Details', icon: Building2 },
  { title: 'Structure', icon: Settings },
  { title: 'Address & Periods', icon: MapPin },
  { title: 'Officers', icon: Users },
];

export default function CompanyOnboarding() {
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [saving, setSaving] = useState(false);
  const [company, setCompany] = useState<Company>({
    legalName: '',
    companyType: 'Private',
    incorporationDate: '',
    financialYearStartMonth: 1,
    ardMonth: 1,
    isGroupMember: false,
    isHolding: false,
    isInvestment: false,
    isSubsidiary: false,
    isDormant: false,
    isTrading: true,
    isVatRegistered: false,
    isEmployer: false,
    hasStock: false,
    ownsAssets: false,
    hasBorrowings: false,
    hasDirectorLoans: false,
  });
  const [officers, setOfficers] = useState<Officer[]>([{ name: '', role: 'Director' }]);

  const update = (field: keyof Company, value: any) => setCompany(prev => ({ ...prev, [field]: value }));
  const toggleFlag = (field: keyof Company) => setCompany(prev => ({ ...prev, [field]: !prev[field] }));

  const addOfficer = () => setOfficers(prev => [...prev, { name: '', role: 'Director' }]);
  const removeOfficer = (i: number) => setOfficers(prev => prev.filter((_, idx) => idx !== i));
  const updateOfficer = (i: number, field: keyof Officer, value: string) =>
    setOfficers(prev => prev.map((o, idx) => idx === i ? { ...o, [field]: value } : o));

  const handleSave = async () => {
    setSaving(true);
    try {
      const res = await createCompany(company);
      const companyId = res.data.id!;
      for (const officer of officers.filter(o => o.name.trim())) {
        await createOfficer(companyId, officer);
      }
      navigate(`/companies/${companyId}`);
    } catch (err) {
      console.error(err);
      alert('Failed to save company. Check the console for details.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="max-w-3xl mx-auto">
      <h1 className="text-2xl font-bold text-gray-900 mb-2">Add New Company</h1>
      <p className="text-gray-500 mb-8">Set up a new Irish company for accounts preparation.</p>

      {/* Step indicator */}
      <div className="flex items-center gap-2 mb-8">
        {steps.map((s, i) => (
          <button
            key={i}
            onClick={() => i < step && setStep(i)}
            className={`flex items-center gap-2 px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
              i === step
                ? 'bg-emerald-100 text-emerald-700'
                : i < step
                ? 'bg-emerald-50 text-emerald-600 cursor-pointer hover:bg-emerald-100'
                : 'bg-gray-100 text-gray-400'
            }`}
          >
            <s.icon className="w-4 h-4" />
            {s.title}
          </button>
        ))}
      </div>

      <div className="bg-white rounded-xl border border-gray-200 p-8">
        {/* Step 1: Legal Details */}
        {step === 0 && (
          <div className="space-y-5">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Legal Name *</label>
              <input type="text" value={company.legalName} onChange={e => update('legalName', e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                placeholder="e.g. Example Trading Limited" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Trading Name</label>
              <input type="text" value={company.tradingName || ''} onChange={e => update('tradingName', e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                placeholder="Leave blank if same as legal name" />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">CRO Number</label>
                <input type="text" value={company.croNumber || ''} onChange={e => update('croNumber', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                  placeholder="e.g. 123456" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Tax Reference</label>
                <input type="text" value={company.taxReference || ''} onChange={e => update('taxReference', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                  placeholder="e.g. 1234567T" />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Company Type *</label>
                <select value={company.companyType} onChange={e => update('companyType', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none">
                  {COMPANY_TYPES.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Date of Incorporation *</label>
                <input type="date" value={company.incorporationDate} onChange={e => update('incorporationDate', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none" />
              </div>
            </div>
          </div>
        )}

        {/* Step 2: Structure */}
        {step === 1 && (
          <div className="space-y-6">
            <div>
              <h3 className="text-sm font-semibold text-gray-700 mb-3">Company Status</h3>
              <div className="grid grid-cols-2 gap-3">
                {([
                  ['isTrading', 'Currently Trading'],
                  ['isDormant', 'Dormant'],
                ] as const).map(([field, label]) => (
                  <label key={field} className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50">
                    <input type="checkbox" checked={company[field] as boolean} onChange={() => toggleFlag(field)}
                      className="w-4 h-4 text-emerald-600 rounded" />
                    <span className="text-sm text-gray-700">{label}</span>
                  </label>
                ))}
              </div>
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-700 mb-3">Group Structure</h3>
              <div className="grid grid-cols-2 gap-3">
                {([
                  ['isGroupMember', 'Part of a Group'],
                  ['isHolding', 'Holding Company'],
                  ['isInvestment', 'Investment Company'],
                  ['isSubsidiary', 'Subsidiary'],
                ] as const).map(([field, label]) => (
                  <label key={field} className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50">
                    <input type="checkbox" checked={company[field] as boolean} onChange={() => toggleFlag(field)}
                      className="w-4 h-4 text-emerald-600 rounded" />
                    <span className="text-sm text-gray-700">{label}</span>
                  </label>
                ))}
              </div>
            </div>
            <div>
              <h3 className="text-sm font-semibold text-gray-700 mb-3">Business Activities</h3>
              <div className="grid grid-cols-2 gap-3">
                {([
                  ['isVatRegistered', 'VAT Registered'],
                  ['isEmployer', 'Employer (PAYE registered)'],
                  ['hasStock', 'Holds Stock / WIP'],
                  ['ownsAssets', 'Owns Fixed Assets'],
                  ['hasBorrowings', 'Has Borrowings / Loans'],
                  ['hasDirectorLoans', 'Has Director Loans'],
                ] as const).map(([field, label]) => (
                  <label key={field} className="flex items-center gap-3 p-3 border rounded-lg cursor-pointer hover:bg-gray-50">
                    <input type="checkbox" checked={company[field] as boolean} onChange={() => toggleFlag(field)}
                      className="w-4 h-4 text-emerald-600 rounded" />
                    <span className="text-sm text-gray-700">{label}</span>
                  </label>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* Step 3: Address & Periods */}
        {step === 2 && (
          <div className="space-y-5">
            <h3 className="text-sm font-semibold text-gray-700">Registered Office</h3>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Address Line 1</label>
              <input type="text" value={company.registeredOfficeAddress1 || ''} onChange={e => update('registeredOfficeAddress1', e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Address Line 2</label>
              <input type="text" value={company.registeredOfficeAddress2 || ''} onChange={e => update('registeredOfficeAddress2', e.target.value)}
                className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none" />
            </div>
            <div className="grid grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">City / Town</label>
                <input type="text" value={company.registeredOfficeCity || ''} onChange={e => update('registeredOfficeCity', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">County</label>
                <select value={company.registeredOfficeCounty || ''} onChange={e => update('registeredOfficeCounty', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none">
                  <option value="">Select...</option>
                  {COUNTIES.map(c => <option key={c} value={c}>{c}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Eircode</label>
                <input type="text" value={company.registeredOfficeEircode || ''} onChange={e => update('registeredOfficeEircode', e.target.value)}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                  placeholder="e.g. D02 AF30" />
              </div>
            </div>

            <hr className="my-6" />

            <h3 className="text-sm font-semibold text-gray-700">Financial Year</h3>
            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Financial Year Starts</label>
                <select value={company.financialYearStartMonth} onChange={e => update('financialYearStartMonth', parseInt(e.target.value))}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none">
                  {MONTHS.map((m, i) => <option key={i+1} value={i+1}>{m}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">Annual Return Date (month)</label>
                <select value={company.ardMonth} onChange={e => update('ardMonth', parseInt(e.target.value))}
                  className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none">
                  {MONTHS.map((m, i) => <option key={i+1} value={i+1}>{m}</option>)}
                </select>
              </div>
            </div>
          </div>
        )}

        {/* Step 4: Officers */}
        {step === 3 && (
          <div className="space-y-5">
            <p className="text-sm text-gray-500">Add the company's directors and secretary. At least one director is required.</p>
            {officers.map((officer, i) => (
              <div key={i} className="flex items-start gap-3 p-4 border border-gray-200 rounded-lg">
                <div className="flex-1 grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-medium text-gray-600 mb-1">Full Name</label>
                    <input type="text" value={officer.name} onChange={e => updateOfficer(i, 'name', e.target.value)}
                      className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none"
                      placeholder="Full legal name" />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-600 mb-1">Role</label>
                    <select value={officer.role} onChange={e => updateOfficer(i, 'role', e.target.value)}
                      className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 outline-none">
                      <option value="Director">Director</option>
                      <option value="Secretary">Secretary</option>
                      <option value="CompanySecretary">Company Secretary</option>
                    </select>
                  </div>
                </div>
                {officers.length > 1 && (
                  <button onClick={() => removeOfficer(i)} className="text-red-400 hover:text-red-600 text-sm mt-6">Remove</button>
                )}
              </div>
            ))}
            <button onClick={addOfficer} className="text-sm text-emerald-600 hover:text-emerald-700 font-medium">+ Add another officer</button>
          </div>
        )}

        {/* Navigation */}
        <div className="flex items-center justify-between mt-8 pt-6 border-t border-gray-100">
          <button
            onClick={() => setStep(s => s - 1)}
            disabled={step === 0}
            className="flex items-center gap-1 text-sm text-gray-600 hover:text-gray-900 disabled:opacity-30 disabled:cursor-not-allowed"
          >
            <ChevronLeft className="w-4 h-4" /> Back
          </button>

          {step < steps.length - 1 ? (
            <button
              onClick={() => setStep(s => s + 1)}
              className="flex items-center gap-1 bg-emerald-600 text-white px-5 py-2 rounded-lg text-sm hover:bg-emerald-700"
            >
              Next <ChevronRight className="w-4 h-4" />
            </button>
          ) : (
            <button
              onClick={handleSave}
              disabled={saving || !company.legalName}
              className="flex items-center gap-2 bg-emerald-600 text-white px-5 py-2 rounded-lg text-sm hover:bg-emerald-700 disabled:opacity-50"
            >
              <Check className="w-4 h-4" />
              {saving ? 'Saving...' : 'Create Company'}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
