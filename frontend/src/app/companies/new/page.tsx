"use client";

import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
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
import { onboardCompany } from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { useAuth } from "@/components/AuthProvider";
import { validateStep } from "@/lib/validation";
import { useGuardedRouter, useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

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

const STEP_LABELS = ["Legal Details", "Structure", "Address & Opening Setup", "Officers"];

interface OfficerEntry {
  name: string;
  role: string;
}

const selectClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

export default function NewCompanyPage() {
  const router = useGuardedRouter();
  const { isOwner, loading: authLoading } = useAuth();
  const [step, setStep] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  // Step 1: Legal Details
  const [legalName, setLegalName] = useState("");
  const [tradingName, setTradingName] = useState("");
  const [croNumber, setCroNumber] = useState("");
  const [taxReference, setTaxReference] = useState("");
  const [companyType, setCompanyType] = useState("Private");
  const [incorporationDate, setIncorporationDate] = useState("");
  const idempotencyKey = useRef<string | null>(null);

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
  const [annualReturnDate, setAnnualReturnDate] = useState("");
  const [annualReturnDateEvidenceReference, setAnnualReturnDateEvidenceReference] = useState("");
  const [firstPeriodEnd, setFirstPeriodEnd] = useState("");
  const [bankAccountName, setBankAccountName] = useState("Main Current Account");
  const [bankIban, setBankIban] = useState("");
  const [openingBalance, setOpeningBalance] = useState("0.00");

  // Step 4: Officers
  const [officers, setOfficers] = useState<OfficerEntry[]>([
    { name: "", role: "Director" },
  ]);

  const hasOnboardingDraft = useMemo(() =>
    legalName !== ""
    || tradingName !== ""
    || croNumber !== ""
    || taxReference !== ""
    || companyType !== "Private"
    || incorporationDate !== ""
    || !isTrading
    || isDormant
    || isGroupMember
    || isHolding
    || isInvestment
    || isSubsidiary
    || isVatRegistered
    || isEmployer
    || hasStock
    || ownsAssets
    || hasBorrowings
    || hasDirectorLoans
    || isListedSecurities
    || isCreditInstitution
    || isInsuranceUndertaking
    || isPensionFund
    || isCharitableOrganisation
    || address1 !== ""
    || address2 !== ""
    || city !== ""
    || county !== ""
    || eircode !== ""
    || financialYearStartMonth !== 1
    || annualReturnDate !== ""
    || annualReturnDateEvidenceReference !== ""
    || firstPeriodEnd !== ""
    || bankAccountName !== "Main Current Account"
    || bankIban !== ""
    || openingBalance !== "0.00"
    || officers.length !== 1
    || officers[0]?.name !== ""
    || officers[0]?.role !== "Director",
  [
    address1, address2, annualReturnDate, annualReturnDateEvidenceReference, bankAccountName, bankIban, city, companyType,
    county, croNumber, eircode, financialYearStartMonth, firstPeriodEnd, hasBorrowings,
    hasDirectorLoans, hasStock, incorporationDate, isCharitableOrganisation,
    isCreditInstitution, isDormant, isEmployer, isGroupMember, isHolding,
    isInsuranceUndertaking, isInvestment, isListedSecurities, isPensionFund,
    isSubsidiary, isTrading, isVatRegistered, legalName, officers, openingBalance,
    ownsAssets, taxReference, tradingName,
  ]);
  useUnsavedChanges(hasOnboardingDraft);

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
      Object.assign(stepData, {
        address1,
        address2,
        city,
        county,
        eircode,
        incorporationDate,
        annualReturnDate,
        annualReturnDateEvidenceReference,
        firstPeriodEnd,
        bankAccountName,
        bankIban,
        openingBalance,
      });
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

    const legalErrors = validateStep(0, {
      legalName,
      tradingName,
      croNumber,
      taxReference,
      companyType,
      incorporationDate,
    });
    if (Object.keys(legalErrors).length > 0) {
      setFieldErrors(legalErrors);
      setStep(0);
      return;
    }
    const setupErrors = validateStep(2, {
      address1,
      address2,
      city,
      county,
      eircode,
      incorporationDate,
      annualReturnDate,
      annualReturnDateEvidenceReference,
      firstPeriodEnd,
      bankAccountName,
      bankIban,
      openingBalance,
    });
    if (Object.keys(setupErrors).length > 0) {
      setFieldErrors(setupErrors);
      setStep(2);
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const validOfficers = officers.filter((o) => o.name.trim().length > 0);
      const parsedOpeningBalance = Number(openingBalance);
      if (validOfficers.length === 0) {
        setFieldErrors({ officers: "At least one officer is required" });
        setError("At least one officer is required");
        setSaving(false);
        return;
      }
      if (!idempotencyKey.current) {
        idempotencyKey.current = globalThis.crypto?.randomUUID?.()
          ?? `onboard-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      }
      const outcome = await onboardCompany({
        company: {
          legalName,
          tradingName: tradingName || undefined,
          croNumber: croNumber || undefined,
          taxReference: taxReference || undefined,
          companyType,
          incorporationDate,
          financialYearStartMonth,
          annualReturnDate,
          annualReturnDateEffectiveFrom: annualReturnDate,
          annualReturnDateSource: "CroRecord",
          annualReturnDateEvidenceReference,
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
        },
        officers: validOfficers.map((officer) => ({
          name: officer.name.trim(),
          role: officer.role,
        })),
        firstPeriod: {
          periodStart: incorporationDate,
          periodEnd: firstPeriodEnd,
          isFirstYear: true,
          memberAuditNoticeReceived: false,
          goingConcernConfirmed: true,
        },
        openingBankAccount: {
          name: bankAccountName.trim(),
          iban: bankIban.trim() || undefined,
          currency: "EUR",
          openingBalance: parsedOpeningBalance,
          openingBalanceDate: parsedOpeningBalance === 0 ? undefined : incorporationDate,
        },
      }, idempotencyKey.current);

      toast.success(`${outcome.companyLegalName} and its opening records were created successfully`);
      router.pushAfterSave(`/companies/${outcome.companyId}`);
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
        <p className="text-[var(--muted-foreground)] mt-1">
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
                  ? "text-[var(--accent)]"
                  : i < step
                    ? "cursor-pointer text-[var(--accent)] hover:text-[var(--accent-hover)]"
                    : "text-[var(--muted-foreground)]"
              }`}
              disabled={i > step}
              type="button"
            >
              <span
                className={`inline-flex items-center justify-center w-6 h-6 rounded-full text-xs font-bold transition-colors ${
                  i === step
                    ? "border border-[var(--accent)] bg-[var(--accent)] text-[var(--accent-foreground)]"
                    : i < step
                      ? "border border-[var(--accent)] bg-[var(--surface-strong)] text-[var(--accent)]"
                      : "border border-[var(--control-border)] bg-[var(--surface-subtle)] text-[var(--muted-foreground)]"
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
          <Card.Description className="text-[var(--muted-foreground)]">
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
                    aria-describedby={fieldErrors.legalName ? "legal-name-error" : undefined}
                  />
                </TextField>
                {fieldErrors.legalName && <p id="legal-name-error" role="alert" className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.legalName}</p>}
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
                      aria-describedby={fieldErrors.croNumber ? "cro-number-error" : undefined}
                    />
                  </TextField>
                  {fieldErrors.croNumber && <p id="cro-number-error" role="alert" className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.croNumber}</p>}
                </div>

                <div>
                  <TextField fullWidth>
                    <Label>Tax Reference</Label>
                    <Input
                      value={taxReference}
                      onChange={(e) => { setTaxReference(e.target.value); setFieldErrors((p) => { const n = {...p}; delete n.taxReference; return n; }); }}
                      placeholder="e.g. 1234567T"
                      aria-invalid={!!fieldErrors.taxReference}
                      aria-describedby={fieldErrors.taxReference ? "tax-reference-error" : undefined}
                    />
                  </TextField>
                  {fieldErrors.taxReference && <p id="tax-reference-error" role="alert" className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.taxReference}</p>}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label htmlFor="company-type" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                    Company Type
                  </label>
                  <select
                    id="company-type"
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
                    <Label>Incorporation Date *</Label>
                    <Input
                      type="date"
                      value={incorporationDate}
                      onChange={(e) => {
                        const nextDate = e.target.value;
                        setIncorporationDate(nextDate);
                        setFirstPeriodEnd(suggestedFirstPeriodEnd(nextDate));
                        setFieldErrors((p) => { const n = {...p}; delete n.incorporationDate; return n; });
                      }}
                      aria-invalid={!!fieldErrors.incorporationDate}
                      aria-describedby={fieldErrors.incorporationDate ? "incorporation-date-error" : undefined}
                    />
                  </TextField>
                  {fieldErrors.incorporationDate && <p id="incorporation-date-error" role="alert" className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.incorporationDate}</p>}
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
                  <OnboardingCheckbox isSelected={isTrading} onChange={setIsTrading}>
                    Currently Trading
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isDormant} onChange={setIsDormant}>
                    Dormant
                  </OnboardingCheckbox>
                </div>
                {isTrading && isDormant && (
                  <p className="mt-2 text-xs text-amber-800 dark:text-amber-300">
                    A company is usually either trading or dormant, not both.
                  </p>
                )}
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Group Structure
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <OnboardingCheckbox isSelected={isGroupMember} onChange={setIsGroupMember}>
                    Group Member
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isHolding} onChange={setIsHolding}>
                    Holding Company
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isInvestment} onChange={setIsInvestment}>
                    Investment Company
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isSubsidiary} onChange={setIsSubsidiary}>
                    Subsidiary
                  </OnboardingCheckbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Registrations
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <OnboardingCheckbox isSelected={isVatRegistered} onChange={setIsVatRegistered}>
                    VAT Registered
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isEmployer} onChange={setIsEmployer}>
                    Employer (PAYE Registered)
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isCharitableOrganisation} onChange={setIsCharitableOrganisation}>
                    Registered Charity
                  </OnboardingCheckbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Balance Sheet Items
                </h3>
                <div className="grid grid-cols-2 gap-3">
                  <OnboardingCheckbox isSelected={hasStock} onChange={setHasStock}>
                    Has Stock / Inventory
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={ownsAssets} onChange={setOwnsAssets}>
                    Owns Fixed Assets
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={hasBorrowings} onChange={setHasBorrowings}>
                    Has Borrowings
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={hasDirectorLoans} onChange={setHasDirectorLoans}>
                    Has Director Loans
                  </OnboardingCheckbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  Regulatory Exclusions (Fifth Schedule)
                </h3>
                <p className="text-xs text-[var(--muted-foreground)] mb-3">
                  If any apply, the company is ineligible for micro/small exemptions and must file full accounts.
                </p>
                <div className="grid grid-cols-2 gap-3">
                  <OnboardingCheckbox isSelected={isListedSecurities} onChange={setIsListedSecurities}>
                    Listed Securities (regulated market)
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isCreditInstitution} onChange={setIsCreditInstitution}>
                    Credit Institution
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isInsuranceUndertaking} onChange={setIsInsuranceUndertaking}>
                    Insurance Undertaking
                  </OnboardingCheckbox>
                  <OnboardingCheckbox isSelected={isPensionFund} onChange={setIsPensionFund}>
                    Pension / Investment Fund
                  </OnboardingCheckbox>
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
                  <label htmlFor="registered-office-county" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                    County
                  </label>
                  <select
                    id="registered-office-county"
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
                      aria-describedby={fieldErrors.eircode ? "eircode-error" : undefined}
                    />
                  </TextField>
                  {fieldErrors.eircode && <p id="eircode-error" role="alert" className="text-xs text-red-600 dark:text-red-400 mt-1">{fieldErrors.eircode}</p>}
                </div>
              </div>

              <div className="border-t border-gray-200 dark:border-neutral-700 pt-5 mt-5">
                <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                  First Accounting Period
                </h3>
                <p className="mb-3 text-xs text-[var(--muted-foreground)]">
                  The first period begins on the incorporation date and is created atomically with the company.
                </p>
                <div className="mb-4 grid grid-cols-2 gap-4">
                  <TextField fullWidth>
                    <Label>Period Start</Label>
                    <Input type="date" value={incorporationDate} readOnly aria-label="First period start" />
                  </TextField>
                  <div>
                    <TextField fullWidth>
                      <Label>Period End *</Label>
                      <Input
                        type="date"
                        value={firstPeriodEnd}
                        onChange={(event) => {
                          setFirstPeriodEnd(event.target.value);
                          setFieldErrors((current) => {
                            const next = { ...current };
                            delete next.firstPeriodEnd;
                            return next;
                          });
                        }}
                        aria-invalid={!!fieldErrors.firstPeriodEnd}
                        aria-describedby={fieldErrors.firstPeriodEnd ? "first-period-end-error" : undefined}
                      />
                    </TextField>
                    {fieldErrors.firstPeriodEnd && (
                      <p id="first-period-end-error" role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">
                        {fieldErrors.firstPeriodEnd}
                      </p>
                    )}
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label htmlFor="financial-year-start-month" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
                      Financial Year Start Month
                    </label>
                    <select
                      id="financial-year-start-month"
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
                    <label htmlFor="annual-return-date" className="mb-1.5 block text-sm font-medium text-gray-700 dark:text-gray-300">
                      Exact Annual Return Date (ARD) *
                    </label>
                    <input
                      id="annual-return-date"
                      type="date"
                      value={annualReturnDate}
                      onChange={(event) => {
                        setAnnualReturnDate(event.target.value);
                        setFieldErrors((current) => {
                          const next = { ...current };
                          delete next.annualReturnDate;
                          return next;
                        });
                      }}
                      className={selectClass}
                      aria-label="Exact Annual Return Date from CRO CORE"
                      aria-invalid={!!fieldErrors.annualReturnDate}
                      aria-describedby={fieldErrors.annualReturnDate ? "annual-return-date-error" : undefined}
                    />
                    {fieldErrors.annualReturnDate && (
                      <p id="annual-return-date-error" role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">{fieldErrors.annualReturnDate}</p>
                    )}
                  </div>
                </div>
                <div className="mt-4">
                  <label htmlFor="annual-return-date-evidence" className="mb-1.5 block text-sm font-medium text-gray-700 dark:text-gray-300">
                    CRO evidence reference *
                  </label>
                  <input
                    id="annual-return-date-evidence"
                    value={annualReturnDateEvidenceReference}
                    onChange={(event) => {
                      setAnnualReturnDateEvidenceReference(event.target.value);
                      setFieldErrors((current) => {
                        const next = { ...current };
                        delete next.annualReturnDateEvidenceReference;
                        return next;
                      });
                    }}
                    maxLength={300}
                    placeholder="CORE lookup, CRO extract, or retained workpaper reference"
                    className={selectClass}
                    aria-label="Annual Return Date evidence reference"
                    aria-invalid={!!fieldErrors.annualReturnDateEvidenceReference}
                    aria-describedby={fieldErrors.annualReturnDateEvidenceReference ? "annual-return-date-evidence-error" : undefined}
                  />
                  {fieldErrors.annualReturnDateEvidenceReference && (
                    <p id="annual-return-date-evidence-error" role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">{fieldErrors.annualReturnDateEvidenceReference}</p>
                  )}
                  <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                    Enter the exact date shown by CRO CORE. The platform will not infer a day from a month.
                  </p>
                </div>
              </div>

              <div className="border-t border-gray-200 pt-5 dark:border-neutral-700">
                <h3 className="mb-1 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Opening Bank & Chart of Accounts
                </h3>
                <p className="mb-3 text-xs text-[var(--muted-foreground)]">
                  A complete default chart of accounts and this opening bank account are created in the same transaction.
                </p>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <div>
                    <TextField fullWidth>
                      <Label>Bank Account Name *</Label>
                      <Input
                        value={bankAccountName}
                        onChange={(event) => {
                          setBankAccountName(event.target.value);
                          setFieldErrors((current) => {
                            const next = { ...current };
                            delete next.bankAccountName;
                            return next;
                          });
                        }}
                        aria-invalid={!!fieldErrors.bankAccountName}
                        aria-describedby={fieldErrors.bankAccountName ? "bank-account-name-error" : undefined}
                      />
                    </TextField>
                    {fieldErrors.bankAccountName && (
                      <p id="bank-account-name-error" role="alert" className="mt-1 text-xs text-red-600 dark:text-red-400">{fieldErrors.bankAccountName}</p>
                    )}
                  </div>
                  <TextField fullWidth>
                    <Label>IBAN</Label>
                    <Input value={bankIban} onChange={(event) => setBankIban(event.target.value)} />
                  </TextField>
                  <TextField fullWidth>
                    <Label>Opening Balance (EUR)</Label>
                    <Input
                      type="number"
                      step="0.01"
                      value={openingBalance}
                      onChange={(event) => setOpeningBalance(event.target.value)}
                    />
                  </TextField>
                  <div className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs text-emerald-800 dark:border-emerald-900 dark:bg-emerald-950/30 dark:text-emerald-300">
                    Non-zero opening balances are dated at the first-period start. The server rejects any partial setup.
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
                  <Users className="w-5 h-5 text-[var(--muted-foreground)] mt-2 shrink-0" />
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
                      <label htmlFor={`officer-${index}-role`} className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                        Role
                      </label>
                      <select
                        id={`officer-${index}-role`}
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
                      onPress={() => requestDestructiveAction({
                        recordLabel: `onboarding officer ${officer.name || index + 1}`,
                        consequence: `This removes the unsaved ${officer.role} entry from the onboarding draft. The officer will not be created with the company.`,
                        onConfirm: () => removeOfficer(index),
                        successAnnouncement: `Onboarding officer ${officer.name || index + 1} was removed from the draft.`,
                      })}
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
      {destructiveActionConfirmation}
    </div>
  );
}

function OnboardingCheckbox({
  isSelected,
  onChange,
  children,
}: {
  isSelected: boolean;
  onChange: (selected: boolean) => void;
  children: ReactNode;
}) {
  return (
    <Checkbox isSelected={isSelected} onChange={onChange}>
      <Checkbox.Content>
        <Checkbox.Control>
          <Checkbox.Indicator />
        </Checkbox.Control>
        <Label>{children}</Label>
      </Checkbox.Content>
    </Checkbox>
  );
}

function suggestedFirstPeriodEnd(incorporationDate: string): string {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(incorporationDate)) return "";
  const [year, month, day] = incorporationDate.split("-").map(Number);
  const end = new Date(Date.UTC(year + 1, month - 1, day));
  end.setUTCDate(end.getUTCDate() - 1);
  return end.toISOString().slice(0, 10);
}
