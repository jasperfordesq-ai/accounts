"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import {
  Button,
  Card,
  Chip,
  Checkbox,
  Input,
  Label,
  TextField,
  Spinner,
} from "@heroui/react";
import { Building2, ChevronLeft, ChevronRight, Plus, Trash2, Users } from "lucide-react";
import { createCompany, createOfficer, type Officer } from "@/lib/api";

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

export default function NewCompanyPage() {
  const router = useRouter();
  const [step, setStep] = useState(0);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

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

  async function handleCreate() {
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
      });

      const validOfficers = officers.filter((o) => o.name.trim().length > 0);
      for (const officer of validOfficers) {
        await createOfficer(company.id, {
          name: officer.name,
          role: officer.role,
        } as Officer);
      }

      router.push(`/companies/${company.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to create company");
      setSaving(false);
    }
  }

  return (
    <div className="max-w-3xl mx-auto">
      {/* Header */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-2">
          <Building2 className="w-7 h-7 text-emerald-600" />
          New Company
        </h1>
        <p className="text-gray-500 mt-1">Set up a new company for statutory accounts production</p>
      </div>

      {/* Step Indicators */}
      <div className="flex items-center gap-2 mb-6">
        {STEP_LABELS.map((label, i) => (
          <Button
            key={label}
            variant="ghost"
            size="sm"
            onPress={() => { if (i < step) setStep(i); }}
            className="flex items-center gap-2"
          >
            <Chip
              color={i === step ? "accent" : i < step ? "success" : "default"}
              variant={i === step ? "primary" : "soft"}
              size="sm"
            >
              {i + 1}. {label}
            </Chip>
          </Button>
        ))}
      </div>

      {/* Form Card */}
      <Card className="shadow-sm border border-gray-200">
        <Card.Header>
          <Card.Title>{STEP_LABELS[step]}</Card.Title>
          <Card.Description>
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
              <TextField fullWidth>
                <Label>Legal Name *</Label>
                <Input
                  value={legalName}
                  onChange={(e) => setLegalName(e.target.value)}
                  placeholder="e.g. Acme Trading Limited"
                />
              </TextField>

              <TextField fullWidth>
                <Label>Trading Name</Label>
                <Input
                  value={tradingName}
                  onChange={(e) => setTradingName(e.target.value)}
                  placeholder="e.g. Acme"
                />
              </TextField>

              <div className="grid grid-cols-2 gap-4">
                <TextField fullWidth>
                  <Label>CRO Number</Label>
                  <Input
                    value={croNumber}
                    onChange={(e) => setCroNumber(e.target.value)}
                    placeholder="e.g. 123456"
                  />
                </TextField>

                <TextField fullWidth>
                  <Label>Tax Reference</Label>
                  <Input
                    value={taxReference}
                    onChange={(e) => setTaxReference(e.target.value)}
                    placeholder="e.g. 1234567T"
                  />
                </TextField>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1.5">
                    Company Type
                  </label>
                  <select
                    value={companyType}
                    onChange={(e) => setCompanyType(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
                  >
                    {COMPANY_TYPES.map((ct) => (
                      <option key={ct.value} value={ct.value}>
                        {ct.label}
                      </option>
                    ))}
                  </select>
                </div>

                <TextField fullWidth>
                  <Label>Incorporation Date</Label>
                  <Input
                    type="date"
                    value={incorporationDate}
                    onChange={(e) => setIncorporationDate(e.target.value)}
                  />
                </TextField>
              </div>
            </>
          )}

          {/* Step 2: Structure */}
          {step === 1 && (
            <div className="space-y-6">
              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Trading Status</h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isTrading} onChange={setIsTrading}>
                    Currently Trading
                  </Checkbox>
                  <Checkbox isSelected={isDormant} onChange={setIsDormant}>
                    Dormant
                  </Checkbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Group Structure</h3>
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
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Registrations</h3>
                <div className="grid grid-cols-2 gap-3">
                  <Checkbox isSelected={isVatRegistered} onChange={setIsVatRegistered}>
                    VAT Registered
                  </Checkbox>
                  <Checkbox isSelected={isEmployer} onChange={setIsEmployer}>
                    Employer (PAYE Registered)
                  </Checkbox>
                </div>
              </div>

              <div>
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Balance Sheet Items</h3>
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
            </div>
          )}

          {/* Step 3: Address & Periods */}
          {step === 2 && (
            <>
              <h3 className="text-sm font-semibold text-gray-700 mb-1">Registered Office</h3>
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
                  <label className="block text-sm font-medium text-gray-700 mb-1.5">County</label>
                  <select
                    value={county}
                    onChange={(e) => setCounty(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
                  >
                    <option value="">Select county</option>
                    {IRISH_COUNTIES.map((c) => (
                      <option key={c} value={c}>{c}</option>
                    ))}
                  </select>
                </div>

                <TextField fullWidth>
                  <Label>Eircode</Label>
                  <Input
                    value={eircode}
                    onChange={(e) => setEircode(e.target.value)}
                    placeholder="e.g. D02 AF30"
                  />
                </TextField>
              </div>

              <div className="border-t border-gray-200 pt-5 mt-5">
                <h3 className="text-sm font-semibold text-gray-700 mb-3">Financial Periods</h3>
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1.5">
                      Financial Year Start Month
                    </label>
                    <select
                      value={financialYearStartMonth}
                      onChange={(e) => setFinancialYearStartMonth(Number(e.target.value))}
                      className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
                    >
                      {MONTHS.map((m, i) => (
                        <option key={m} value={i + 1}>{m}</option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-gray-700 mb-1.5">
                      ARD Month (Annual Return Date)
                    </label>
                    <select
                      value={ardMonth}
                      onChange={(e) => setArdMonth(Number(e.target.value))}
                      className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
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
                  className="flex items-start gap-3 p-4 rounded-lg border border-gray-200 bg-gray-50"
                >
                  <Users className="w-5 h-5 text-gray-400 mt-2 shrink-0" />
                  <div className="flex-1 grid grid-cols-2 gap-3">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
                      <input
                        type="text"
                        value={officer.name}
                        onChange={(e) => updateOfficer(index, "name", e.target.value)}
                        placeholder="Full name"
                        className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 mb-1">Role</label>
                      <select
                        value={officer.role}
                        onChange={(e) => updateOfficer(index, "role", e.target.value)}
                        className="w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500"
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
                      aria-label="Remove officer"
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
            <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
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
              onPress={() => setStep(step + 1)}
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
