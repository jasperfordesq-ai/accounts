"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  Button,
  Card,
  Checkbox,
  Input,
  Label,
  TextField,
  Spinner,
} from "@heroui/react";
import { Building2, ChevronLeft, ChevronRight, Plus, Trash2, Users, Check } from "lucide-react";
import { toast } from "sonner";
import { createCompany, createOfficer, type Officer } from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { useAuth } from "@/components/AuthProvider";
import { validateStep } from "@/lib/validation";

const COMPANY_TYPES = [
  { value: "Private", label: "Private Company Limited by Shares" },
  { value: "PrivateUnlimited", label: "Private Unlimited Company" },
  { value: "DesignatedActivityCompany", label: "Designated Activity Company (DAC)" },
  { value: "CompanyLimitedByGuarantee", label: "Company Limited by Guarantee (CLG)" },
  { value: "PublicLimitedCompany", label: "Public Limited Company (PLC)" },
];

const IRISH_COUNTIES = [
  "Carlow", "Cavan", "Clare", "Cork", "Donegal", "Dublin",
  "Galway", "Kerry", "Kildare", "Kilkenny", "Laois", "Leitrim",
  "Limerick", "Longford", "Louth", "Mayo", "Meath", "Monaghan",
  "Offaly", "Roscommon", "Sligo", "Tipperary", "Waterford",
  "Westmeath", "Wexford", "Wicklow",
];

const MONTHS = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

const STEP_LABELS = ["Legal Details", "Structure", "Address & Periods", "Officers"];

interface OfficerEntry {
  name: string;
  role: string;
}

const selectClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors";

export default function NewCompanyPage() {
  const router = useRouter();
  const { isOwner, loading: authLoading } = useAuth();
  const [step, setStep] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Step 1: Legal Details
  const [legalName, setLegalName] = useState("");
  const [tradingName, setTradingName] = useState("");
  const [croNumber, setCroNumber] = useState("");
  const [taxReference, setTaxReference] = useState("");
  const [companyType, setCompanyType] = useState("Private");
  const [incorporationDate, setIncorporationDate] = useState("");

  // Step 2: Structure
  const [isTrading, setIsTrading] = useState(true);
  const [isDormant, setIsDormant] = useState(false);
  const [isGroupMember, setIsGroupMember] = useState(false);
  const [isHolding, setIsHolding] = useState(false);
  const [isInvestment, setIsInvestment] = useState(false);
  const [isSubsidiary, setIsSubsidiary] = useState(false);
  const [isVatRegistered, setIsVatRegistered] = useState(false);
  const [isEmployer, setIsEmployer] = useState(false);
  const [hasStock, setHasStock] = useState(false);
  const [ownsAssets, setOwnsAssets] = useState(false);
  const [hasBorrowings, setHasBorrowings] = useState(false);
  const [hasDirectorLoans, setHasDirectorLoans] = useState(false);
  const [isListedSecurities, setIsListedSecurities] = useState(false);
  const [isCreditInstitution, setIsCreditInstitution] = useState(false);
  const [isInsuranceUndertaking, setIsInsuranceUndertaking] = useState(false);
  const [isPensionFund, setIsPensionFund] = useState(false);
  const [isCharitableOrganisation, setIsCharitableOrganisation] = useState(false);

  // Step 3: Address & Periods
  const [address1, setAddress1] = useState("");
  const [address2, setAddress2] = useState("");
  const [city, setCity] = useState("");
  const [county, setCounty] = useState("");
  const [eircode, setEircode] = useState("");
  const [financialYearStartMonth, setFinancialYearStartMonth] = useState(1);
  const [ardMonth, setArdMonth] = useState(12);

  // Step 4: Officers
  const [officers, setOfficers] = useState<OfficerEntry[]>([
    { name: "", role: "Director" },
  ]);

  useEffect(() => {
    if (!authLoading && !isOwner) {
      router.replace("/");
    }
  }, [authLoading, isOwner, router]);

  function addOfficer() {
    setOfficers([...officers, { name: "", role: "Director" }]);
  }

  function removeOfficer(index: number) {
    setOfficers(officers.filter((_, i) => i !== index));
  }

  function updateOfficer(index: number, field: keyof OfficerEntry, value: string) {
    const updated = [...officers];
    updated[index] = { ...updated[index], [field]: value };
    setOfficers(updated);
  }

  function canProceed(): boolean {
    if (step === 0) return legalName.trim().length > 0;
    if (step === 3) return officers.some((o) => o.name.trim().length > 0);
    return true;
  }

  function handleNext() {
    setFieldErrors({});
    const stepData: Record<string, unknown> = {};
    if (step === 0) {
      Object.assign(stepData, { legalName, tradingName, croNumber, taxReference, companyType, incorporationDate });
    } else if (step === 2) {
      Object.assign(stepData, { address1, address2, city, county, eircode });
    }
    const errors = validateStep(step, stepData);
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      toast.error(Object.values(errors)[0]);
      return;
    }
    setStep(step + 1);
  }

  async function handleCreate() {
    if (!isOwner) {
      const msg = "Only owners can create companies";
      setError(msg);
      toast.error(msg);
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const company = await createCompany({
        legalName,
        tradingName: tradingName || undefined,
        croNumber: croNumber || undefined,
        taxReference: taxReference || undefined,
        companyType,
        incorporationDate: incorporationDate || new Date().toISOString().split("T")[0],
        financialYearStartMonth,
        ardMonth,
        registeredOfficeAddress1: address1 || undefined,
        registeredOfficeAddress2: address2 || undefined,
        registeredOfficeCity: city || undefined,
        registeredOfficeCounty: county || undefined,
        registeredOfficeEircode: eircode || undefined,
        isTrading,
        isDormant,
        isGroupMember,
        isHolding,
        isInvestment,
        isSubsidiary,
        isVatRegistered,
        isEmployer,
        hasStock,
        ownsAssets,
        hasBorrowings,
        hasDirectorLoans,
        isListedSecurities,
        isCreditInstitution,
        isInsuranceUndertaking,
        isPensionFund,
        isCharitableOrganisation,
      });

      const validOfficers = officers.filter((o) => o.name.trim().length > 0);
      for (const officer of validOfficers) {
        await createOfficer(company.id, {
          name: officer.name,
          role: officer.role,
        } as Officer);
      }

      toast.success(`${legalName} created successfully`);
      router.push(`/companies/${company.id}`);
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Failed to create company";
      setError(msg);
      toast.error(msg);
      setSaving(false);
    }
  }

  if (authLoading || !isOwner) {
    return (
      <div className="min-h-[320px] flex items-center justify-center">
        <Spinner size="sm" />
      </div>
    );
  }

  return (
    <div className="max-w-3xl mx-auto animate-fade-in">
      <Breadcrumbs items={[{ label: "New Company" }]} />

      {/* Header */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100 flex items-center gap-2">
          <Building2 className="w-7 h-7 text-emerald-600 dark:text-emerald-400" />
          New Company
        </h1>
        <p className="text-gray-500 dark:text-gray-400 mt-1">
          Set up a new company for statutory accounts production
        </p>
      </div>

      {/* Progress Bar */}
      <div className="mb-6">
        <div className="flex items-center justify-between mb-2">
          {STEP_LABELS.map((label, i) => (
            <button
              key={label}
              onClick={() => { if (i < step) setStep(i); }}
              className={`flex items-center gap-2 text-sm font-medium transition-colors ${
                i === step
                  ? "text-emerald-700 dark:text-emerald-400"
                  : i < step
                    ? "text-emerald-600 dark:text-emerald-500 cursor-pointer hover:text-emerald-800 dark:hover:text-emerald-300"
                    : "text-gray-400 dark:text-gray-500"
              }`}
              disabled={i > step}
              type="button"
            >
              <span
                className={`inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold transition-colors ${
                  i === step
                    ? "bg-emerald-600 text-white"
                    : i < step
                      ? "bg-emerald-100 dark:bg-emerald-900/50 text-emerald-700 dark:text-emerald-400"
                      : "bg-gray-200 dark:bg-neutral-700 text-gray-500 dark:text-gray-400"
                }`}
              >
                {i < step ? <Check className="w-3.5 h-3.5" /> : i + 1}
              </span>
              <span className="hidden sm:inline">{label}</span>
            </button>
          ))}
        </div>
        <div className="h-1.5 bg-gray-200 dark:bg-neutral-700 rounded-full overflow-hidden">
          <div
            className="h-full bg-emerald-500 rounded-full transition-all duration-300"
            style={{ width: `${((step + 1) / STEP_LABELS.length) * 100}%` }}
          />
        </div>
      </div>

      {/* Form Card */}
      <Card className="bg-white dark:bg-neutral-900 shadow-sm border border-gray-200 dark:border-neutral-700">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">{STEP_LABELS[step]}</Card.Title>
          <Card.Description className="text-gray-500 dark:text-gray-400">
            {step === 0 && "Enter the company's legal and registration details"}
            {step === 1 && "Configure the company's structural characteristics"}
            {step === 2 && "Set the registered office address and financial periods"}
            {step === 3 && "Add company directors and officers"}
          </Card.Description>
        </Card.Header>

        <Card.Content className="space-y-5">
          {/* Step 1: Legal Details */}
          {step === 0 && (
            <>
              <div>
                <TextField fullWidth>
                  <Label>Legal Name *</Label>
                  <Input
                    value={legalName}
                    onChange={(e) => { setLegalName(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.legalName; return n; }); }}
                    placeholder="e.g. Acme Trading Limited"
                    autoFocus
                    aria-invalid={!!fieldErrors.legalName}
                  />
                </TextField>
                {fieldErrors.legalName && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.legalName}</p>}
              </div>

              <TextField fullWidth>
                <Label>Trading Name</Label>
                <Input
                  value={tradingName}
                  onChange={(e) => setTradingName(e.target.value)}
                  placeholder="e.g. Acme"
                />
              </TextField>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <TextField fullWidth>
                    <Label>CRO Number</Label>
                    <Input
                      value={croNumber}
                      onChange={(e) => { setCroNumber(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.croNumber; return n; }); }}
                      placeholder="e.g. 123456"
                      aria-invalid={!!fieldErrors.croNumber}
                    />
                  </TextField>
                  {fieldErrors.croNumber && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.croNumber}</p>}
                </div>

                <div>
                  <TextField fullWidth>
                    <Label>Tax Reference</Label>
                    <Input
                      value={taxReference}
                      onChange={(e) => { setTaxReference(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.taxReference; return n; }); }}
                      placeholder="e.g. 1234567T"
                      aria-invalid={!!fieldErrors.taxReference}
                    />
                  </TextField>
                  {fieldErrors.taxReference && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.taxReference}</p>}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                    Company Type
                  </label>
                  <select
                    value={companyType}
                    onChange={(e) => setCompanyType(e.target.value)}
                    className={selectClass}
                    aria-label="Company type"
                    title="Company type"
                  >
                    {COMPANY_TYPES.map((ct) => (
                      <option key={ct.value} value={ct.value}>
                        {ct.label}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <TextField fullWidth>
                    <Label>Incorporation Date</Label>
                    <Input
                      type="date"
                      value={incorporationDate}
                      onChange={(e) => { setIncorporationDate(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.incorporationDate; return n; }); }}
                      aria-invalid={!!fieldErrors.incorporationDate}
                    />
                  </TextField>
                  {fieldErrors.incorporationDate && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.incorporationDate}</p>}
                </div>
              </div>
            </>
          )}

          {/* Step 2: Structure */}
          {step === 1 && (
            <div className="space-y-6">
              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Trading Status
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isTrading} onChange={setIsTrading}>
                    Currently Trading
                  </Checkbox>
                  <Checkbox isSelected={isDormant} onChange={setIsDormant}>
                    Dormant
                  </Checkbox>
                </div>
                {isTrading && isDormant && (
                  <p className="text-xs text-amber-600 dark:text-amber-400 mt-2">
                    A company is usually either trading or dormant, not both.
                  </p>
                )}
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Group Structure
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isGroupMember} onChange={setIsGroupMember}>
                    Group Member
                  </Checkbox>
                  <Checkbox isSelected={isHolding} onChange={setIsHolding}>
                    Holding Company
                  </Checkbox>
                  <Checkbox isSelected={isInvestment} onChange={setIsInvestment}>
                    Investment Company
                  </Checkbox>
                  <Checkbox isSelected={isSubsidiary} onChange={setIsSubsidiary}>
                    Subsidiary
                  </Checkbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Registrations
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isVatRegistered} onChange={setIsVatRegistered}>
                    VAT Registered
                  </Checkbox>
                  <Checkbox isSelected={isEmployer} onChange={setIsEmployer}>
                    Employer (PAYE Registered)
                  </Checkbox>
                  <Checkbox isSelected={isCharitableOrganisation} onChange={setIsCharitableOrganisation}>
                    Registered Charity
                  </Checkbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Balance Sheet Items
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={hasStock} onChange={setHasStock}>
                    Has Stock / Inventory
                  </Checkbox>
                  <Checkbox isSelected={ownsAssets} onChange={setOwnsAssets}>
                    Owns Fixed Assets
                  </Checkbox>
                  <Checkbox isSelected={hasBorrowings} onChange={setHasBorrowings}>
                    Has Borrowings
                  </Checkbox>
                  <Checkbox isSelected={hasDirectorLoans} onChange={setHasDirectorLoans}>
                    Has Director Loans
                  </Checkbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Regulatory Exclusions (Fifth Schedule)
                </h3>
                <p className="text-xs text-gray-500 dark:text-gray-400 mb-3">
                  If any apply, the company is ineligible for micro/small exemptions and must file full accounts.
                </p>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isListedSecurities} onChange={setIsListedSecurities}>
                    Listed Securities (regulated market)
                  </Checkbox>
                  <Checkbox isSelected={isCreditInstitution} onChange={setIsCreditInstitution}>
                    Credit Institution
                  </Checkbox>
                  <Checkbox isSelected={isInsuranceUndertaking} onChange={setIsInsuranceUndertaking}>
                    Insurance Undertaking
                  </Checkbox>
                  <Checkbox isSelected={isPensionFund} onChange={setIsPensionFund}>
                    Pension / Investment Fund
                  </Checkbox>
                </div>
                {(isListedSecurities || isCreditInstitution || isInsuranceUndertaking || isPensionFund) && (
                  <div className="mt-3 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400">
                    This company is an ineligible entity under the Fifth Schedule. Micro, small, and medium exemptions will not be available.
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Step 3: Address & Periods */}
          {step === 2 && (
            <>
              <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-1">
                Registered Office
              </h3>
              <TextField fullWidth>
                <Label>Address Line 1</Label>
                <Input
                  value={address1}
                  onChange={(e) => setAddress1(e.target.value)}
                  placeholder="Street address"
                />
              </TextField>

              <TextField fullWidth>
                <Label>Address Line 2</Label>
                <Input
                  value={address2}
                  onChange={(e) => setAddress2(e.target.value)}
                  placeholder="Suite, unit, floor, etc."
                />
              </TextField>

              <div className="grid grid-cols-3 gap-4">
                <TextField fullWidth>
                  <Label>City / Town</Label>
                  <Input
                    value={city}
                    onChange={(e) => setCity(e.target.value)}
                    placeholder="e.g. Dublin"
                  />
                </TextField>

                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                    County
                  </label>
                  <select
                    value={county}
                    onChange={(e) => setCounty(e.target.value)}
                    className={selectClass}
                    aria-label="County"
                    title="County"
                  >
                    <option value="">Select county</option>
                    {IRISH_COUNTIES.map((c) => (
                      <option key={c} value={c}>{c}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <TextField fullWidth>
                    <Label>Eircode</Label>
                    <Input
                      value={eircode}
                      onChange={(e) => { setEircode(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.eircode; return n; }); }}
                      placeholder="e.g. D02 AF30"
                      aria-invalid={!!fieldErrors.eircode}
                    />
                  </TextField>
                  {fieldErrors.eircode && <p className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.eircode}</p>}
                </div>
              </div>

              <div className="border-t border-gray-200 dark:border-neutral-700 pt-5 mt-5">
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Financial Periods
                </h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                      Financial Year Start Month
                    </label>
                    <select
                      value={financialYearStartMonth}
                      onChange={(e) => setFinancialYearStartMonth(Number(e.target.value))}
                      className={selectClass}
                      aria-label="Financial year start month"
                      title="Financial year start month"
                    >
                      {MONTHS.map((m, i) => (
                        <option key={m} value={i + 1}>{m}</option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                      ARD Month (Annual Return Date)
                    </label>
                    <select
                      value={ardMonth}
                      onChange={(e) => setArdMonth(Number(e.target.value))}
                      className={selectClass}
                      aria-label="Annual return date month"
                      title="Annual return date month"
                    >
                      {MONTHS.map((m, i) => (
                        <option key={m} value={i + 1}>{m}</option>
                      ))}
                    </select>
                  </div>
                </div>
              </div>
            </>
          )}

          {/* Step 4: Officers */}
          {step === 3 && (
            <div className="space-y-4">
              {officers.map((officer, index) => (
                <div
                  key={index}
                  className="flex items-start gap-3 p-4 rounded-lg border border-gray-200 dark:border-neutral-700 bg-gray-50 dark:bg-neutral-800/50 animate-fade-in"
                >
                  <Users className="w-5 h-5 text-gray-400 dark:text-gray-500 mt-2 shrink-0" />
                  <div className="flex-1 grid grid-cols-2 gap-3">
                    <TextField fullWidth>
                      <Label>Name</Label>
                      <Input
                        value={officer.name}
                        onChange={(e) => updateOfficer(index, "name", e.target.value)}
                        placeholder="Full name"
                      />
                    </TextField>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Role
                      </label>
                      <select
                        value={officer.role}
                        onChange={(e) => updateOfficer(index, "role", e.target.value)}
                        className={selectClass}
                        aria-label={`Role for officer ${index + 1}`}
                        title={`Role for officer ${index + 1}`}
                      >
                        <option value="Director">Director</option>
                        <option value="Secretary">Secretary</option>
                        <option value="Chairperson">Chairperson</option>
                        <option value="Shareholder">Shareholder</option>
                      </select>
                    </div>
                  </div>
                  {officers.length > 1 && (
                    <Button
                      variant="ghost"
                      size="sm"
                      isIconOnly
                      onPress={() => removeOfficer(index)}
                      aria-label={`Remove officer ${officer.name || index + 1}`}
                    >
                      <Trash2 className="w-4 h-4 text-red-500" />
                    </Button>
                  )}
                </div>
              ))}

              <Button variant="outline" size="sm" onPress={addOfficer}>
                <Plus className="w-4 h-4 mr-1" />
                Add Officer
              </Button>
            </div>
          )}

          {/* Error Display */}
          {error && (
            <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400">
              {error}
            </div>
          )}
        </Card.Content>

        <Card.Footer className="flex justify-between">
          <Button
            variant="outline"
            onPress={() => setStep(step - 1)}
            isDisabled={step === 0}
          >
            <ChevronLeft className="w-4 h-4 mr-1" />
            Back
          </Button>

          {step < 3 ? (
            <Button
              variant="primary"
              onPress={handleNext}
              isDisabled={!canProceed()}
            >
              Next
              <ChevronRight className="w-4 h-4 ml-1" />
            </Button>
          ) : (
            <Button
              variant="primary"
              onPress={handleCreate}
              isDisabled={saving || !canProceed()}
            >
              {saving ? (
                <>
                  <Spinner size="sm" className="mr-2" />
                  Creating...
                </>
              ) : (
                <>
                  <Building2 className="w-4 h-4 mr-1" />
                  Create Company
                </>
              )}
            </Button>
          )}
        </Card.Footer>
      </Card>
    </div>
  );
}
