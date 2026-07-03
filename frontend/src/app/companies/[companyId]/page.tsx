"use client";

import { useCallback, useEffect, useState, use } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  Card,
  Button, Chip
} from "@heroui/react";
import {
  Building2, Users, Plus, Trash2, Heart
} from "lucide-react";
import { toast } from "sonner";
import { Pencil, Save, X } from "lucide-react";
import { getCompany, updateCompany, deleteCompany, createPeriod, deleteOfficer, updateOfficer, createOfficer, getCharityInfo, saveCharityInfo, type Company, type Officer, type CharityInfo } from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { useAuth } from "@/components/AuthProvider";
import { ConfirmModal } from "@/components/ConfirmModal";
import { CompanyDetailSkeleton } from "@/components/Skeleton";
import { CompanyPeriodsWorkbench } from "@/components/company/CompanyPeriodsWorkbench";
import { CompanyStatutoryProfile } from "@/components/company/CompanyStatutoryProfile";

export default function CompanyDetailPage({ params }: { params: Promise<{ companyId: string }> }) {
  const { companyId: id } = use(params);
  const router = useRouter();
  const { canWriteWorkingPapers } = useAuth();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [showNewPeriod, setShowNewPeriod] = useState(false);
  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [isFirstYear, setIsFirstYear] = useState(false);
  const [creatingPeriod, setCreatingPeriod] = useState(false);

  // Delete confirmation modal state
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // Officer management
  const [editingOfficerId, setEditingOfficerId] = useState<number | null>(null);
  const [editOfficerName, setEditOfficerName] = useState("");
  const [editOfficerRole, setEditOfficerRole] = useState("");
  const [savingOfficer, setSavingOfficer] = useState(false);
  const [showAddOfficer, setShowAddOfficer] = useState(false);
  const [newOfficerName, setNewOfficerName] = useState("");
  const [newOfficerRole, setNewOfficerRole] = useState("Director");

  // Edit company state
  const [editing, setEditing] = useState(false);
  const [editForm, setEditForm] = useState({
    legalName: "",
    tradingName: "",
    croNumber: "",
    taxReference: "",
    companyType: "Private",
    isTrading: true,
    isDormant: false,
  });
  const [savingCompany, setSavingCompany] = useState(false);

  // Charity info
  const [charityInfo, setCharityInfo] = useState<CharityInfo | null>(null);
  const [editingCharity, setEditingCharity] = useState(false);
  const [charityForm, setCharityForm] = useState<CharityInfo>({
    charityNumber: "",
    grossIncome: 0,
    sorpTier: 1,
    charitableObjectives: "",
    principalActivities: "",
    governanceCodeCompliant: false,
    hasInternationalTransfers: false,
    trusteeRemunerationPaid: false,
    trusteeRemunerationAmount: 0,
  });
  const [savingCharity, setSavingCharity] = useState(false);

  const startEditingCharity = () => {
    if (charityInfo) {
      setCharityForm({ ...charityInfo });
    }
    setEditingCharity(true);
  };

  const handleSaveCharity = async () => {
    setSavingCharity(true);
    try {
      const saved = await saveCharityInfo(Number(id), charityForm);
      setCharityInfo(saved);
      setEditingCharity(false);
      toast.success("Charity info saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save charity info");
    } finally {
      setSavingCharity(false);
    }
  };

  const startEditing = () => {
    if (!company) return;
    setEditForm({
      legalName: company.legalName || "",
      tradingName: company.tradingName || "",
      croNumber: company.croNumber || "",
      taxReference: company.taxReference || "",
      companyType: company.companyType || "Private",
      isTrading: company.isTrading ?? true,
      isDormant: company.isDormant ?? false,
    });
    setEditing(true);
  };

  const handleSaveCompany = async () => {
    if (!editForm.legalName.trim()) {
      toast.error("Legal name is required");
      return;
    }
    setSavingCompany(true);
    try {
      await updateCompany(Number(id), {
        ...company,
        legalName: editForm.legalName.trim(),
        tradingName: editForm.tradingName.trim() || undefined,
        croNumber: editForm.croNumber.trim() || undefined,
        taxReference: editForm.taxReference.trim() || undefined,
        companyType: editForm.companyType,
        isTrading: editForm.isTrading,
        isDormant: editForm.isDormant,
      });
      toast.success("Company updated successfully");
      setEditing(false);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to update company");
    } finally {
      setSavingCompany(false);
    }
  };

  const load = useCallback(() => {
    getCompany(Number(id))
      .then((companyData) => {
        setCompany(companyData);
        if (companyData.isCharitableOrganisation) {
          getCharityInfo(Number(id))
            .then((ci) => {
              if (ci && 'charityNumber' in ci) setCharityInfo(ci as CharityInfo);
            })
            .catch(() => {});
        }
      })
      .catch((err) => toast.error(err instanceof Error ? err.message : "Failed to load company"))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => { load(); }, [load]);

  const handleDelete = async () => {
    setDeleting(true);
    try {
      await deleteCompany(Number(id));
      toast.success("Company deleted successfully");
      router.push("/");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete company");
      setDeleting(false);
      setShowDeleteModal(false);
    }
  };

  const handleCreatePeriod = async () => {
    if (!periodStart || !periodEnd) {
      toast.error("Please select both start and end dates");
      return;
    }
    if (new Date(periodEnd) <= new Date(periodStart)) {
      toast.error("End date must be after start date");
      return;
    }
    setCreatingPeriod(true);
    try {
      await createPeriod(Number(id), { periodStart, periodEnd, status: "Draft", isFirstYear });
      toast.success("Accounting period created");
      setShowNewPeriod(false);
      setPeriodStart("");
      setPeriodEnd("");
      setIsFirstYear(false);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create period");
    } finally {
      setCreatingPeriod(false);
    }
  };

  const handleStartEditOfficer = (officer: Officer) => {
    setEditingOfficerId(officer.id!);
    setEditOfficerName(officer.name);
    setEditOfficerRole(officer.role);
  };

  const handleSaveOfficer = async () => {
    if (!editingOfficerId || !editOfficerName.trim()) return;
    setSavingOfficer(true);
    try {
      await updateOfficer(Number(id), editingOfficerId, { name: editOfficerName.trim(), role: editOfficerRole });
      toast.success("Officer updated");
      setEditingOfficerId(null);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to update officer");
    } finally {
      setSavingOfficer(false);
    }
  };

  const handleDeleteOfficer = async (officerId: number) => {
    try {
      await deleteOfficer(Number(id), officerId);
      toast.success("Officer removed");
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete officer");
    }
  };

  const handleAddOfficer = async () => {
    if (!newOfficerName.trim()) return;
    setSavingOfficer(true);
    try {
      await createOfficer(Number(id), { name: newOfficerName.trim(), role: newOfficerRole } as Officer);
      toast.success("Officer added");
      setNewOfficerName("");
      setNewOfficerRole("Director");
      setShowAddOfficer(false);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add officer");
    } finally {
      setSavingOfficer(false);
    }
  };

  if (loading) return <CompanyDetailSkeleton />;

  if (!company) {
    return (
      <div className="text-center py-12">
        <Building2 className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
        <p className="text-gray-500 dark:text-gray-400 font-medium">Company not found</p>
        <Link href="/">
          <Button variant="ghost" size="sm" className="mt-3">Back to Dashboard</Button>
        </Link>
      </div>
    );
  }

  return (
    <div className="animate-fade-in">
      <Breadcrumbs items={[{ label: company.legalName }]} />

      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between mb-8">
        <div className="flex min-w-0 items-center gap-3">
          <div className="shrink-0 bg-emerald-50 dark:bg-emerald-900/30 p-3 rounded-xl">
            <Building2 className="w-7 h-7 text-emerald-600 dark:text-emerald-400" />
          </div>
          <div className="min-w-0">
            <h1 className="break-words text-2xl font-bold text-gray-900 dark:text-gray-100">
              {company.legalName}
            </h1>
            {company.tradingName && (
              <p className="break-words text-gray-500 dark:text-gray-400">t/a {company.tradingName}</p>
            )}
          </div>
        </div>
        <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto sm:justify-end">
          <Button
            variant="outline"
            size="sm"
            onPress={startEditing}
            aria-label="Edit company"
          >
            <Pencil className="w-3.5 h-3.5" />
            Edit
          </Button>
          <Button
            variant="danger"
            size="sm"
            onPress={() => setShowDeleteModal(true)}
            aria-label="Delete company"
          >
            <Trash2 className="w-3.5 h-3.5" />
            Delete
          </Button>
        </div>
      </div>

      {/* Edit Company Form */}
      {editing && (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6 animate-slide-down">
          <Card.Header>
            <Card.Title className="text-gray-900 dark:text-gray-100 flex items-center gap-2">
              <Pencil className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
              Edit Company Details
            </Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Legal Name *</label>
                <input value={editForm.legalName} onChange={(e) => setEditForm({ ...editForm, legalName: e.target.value })} className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors" placeholder="Legal Name" aria-label="Legal Name" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Trading Name</label>
                <input value={editForm.tradingName} onChange={(e) => setEditForm({ ...editForm, tradingName: e.target.value })} className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors" placeholder="Trading Name (optional)" aria-label="Trading Name" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">CRO Number</label>
                <input value={editForm.croNumber} onChange={(e) => setEditForm({ ...editForm, croNumber: e.target.value })} className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors" placeholder="CRO Number" aria-label="CRO Number" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Tax Reference</label>
                <input value={editForm.taxReference} onChange={(e) => setEditForm({ ...editForm, taxReference: e.target.value })} className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors" placeholder="Tax Reference" aria-label="Tax Reference" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Company Type</label>
                <select value={editForm.companyType} onChange={(e) => setEditForm({ ...editForm, companyType: e.target.value })} className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors" aria-label="Company Type" title="Company Type">
                  <option value="Private">LTD - Private company limited by shares</option>
                  <option value="PrivateUnlimited">Unlimited company</option>
                  <option value="DesignatedActivityCompany">DAC - Designated activity company</option>
                  <option value="CompanyLimitedByGuarantee">CLG - Company limited by guarantee</option>
                  <option value="PublicLimitedCompany">PLC - Public limited company</option>
                </select>
              </div>
              <div className="flex items-center gap-6 pt-6">
                <label className="flex items-center gap-2 cursor-pointer">
                  <input type="checkbox" checked={editForm.isTrading} onChange={(e) => setEditForm({ ...editForm, isTrading: e.target.checked })} className="rounded border-gray-300 text-emerald-600 focus:ring-emerald-500" aria-label="Is Trading" title="Is Trading" />
                  <span className="text-sm text-gray-700 dark:text-gray-300">Trading</span>
                </label>
                <label className="flex items-center gap-2 cursor-pointer">
                  <input type="checkbox" checked={editForm.isDormant} onChange={(e) => setEditForm({ ...editForm, isDormant: e.target.checked })} className="rounded border-gray-300 text-emerald-600 focus:ring-emerald-500" aria-label="Is Dormant" title="Is Dormant" />
                  <span className="text-sm text-gray-700 dark:text-gray-300">Dormant</span>
                </label>
              </div>
            </div>
            <div className="flex items-center gap-3 mt-6 pt-4 border-t border-gray-100 dark:border-neutral-700">
              <Button variant="primary" size="sm" onPress={handleSaveCompany} isDisabled={savingCompany}>
                {savingCompany ? "Saving..." : <><Save className="w-3.5 h-3.5" /> Save Changes</>}
              </Button>
              <Button variant="ghost" size="sm" onPress={() => setEditing(false)}>Cancel</Button>
            </div>
          </Card.Content>
        </Card>
      )}

      <div className="mb-8">
        <CompanyStatutoryProfile company={company} />
      </div>

      <div className="mb-8">
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Header>
            <Card.Title className="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide flex items-center justify-between">
              <span className="flex items-center gap-2"><Users className="w-3.5 h-3.5" /> Officers</span>
              <Button variant="ghost" size="sm" isIconOnly onPress={() => setShowAddOfficer(true)} aria-label="Add officer">
                <Plus className="w-3.5 h-3.5" />
              </Button>
            </Card.Title>
          </Card.Header>
          <Card.Content className="space-y-2">
            {showAddOfficer && (
              <div className="flex items-center gap-2 p-2 rounded-lg bg-emerald-50 dark:bg-emerald-900/20 border border-emerald-200 dark:border-emerald-800 animate-slide-down">
                <input
                  value={newOfficerName}
                  onChange={(e) => setNewOfficerName(e.target.value)}
                  placeholder="Name"
                  className="flex-1 rounded border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-2 py-1 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-1 focus:ring-emerald-500"
                  aria-label="New officer name"
                  onKeyDown={(e) => { if (e.key === "Enter") handleAddOfficer(); }}
                />
                <select
                  value={newOfficerRole}
                  onChange={(e) => setNewOfficerRole(e.target.value)}
                  className="rounded border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-2 py-1 text-sm text-gray-900 dark:text-gray-100 outline-none"
                  aria-label="New officer role"
                  title="New officer role"
                >
                  <option value="Director">Director</option>
                  <option value="Secretary">Secretary</option>
                  <option value="Chairperson">Chairperson</option>
                  <option value="Shareholder">Shareholder</option>
                </select>
                <Button variant="primary" size="sm" isIconOnly onPress={handleAddOfficer} isDisabled={savingOfficer || !newOfficerName.trim()} aria-label="Save new officer">
                  <Save className="w-3.5 h-3.5" />
                </Button>
                <Button variant="ghost" size="sm" isIconOnly onPress={() => { setShowAddOfficer(false); setNewOfficerName(""); }} aria-label="Cancel adding officer">
                  <X className="w-3.5 h-3.5" />
                </Button>
              </div>
            )}
            {company.officers && company.officers.length > 0 ? (
              company.officers.map((o) => (
                <div key={o.id} className="flex items-center justify-between group text-sm">
                  {editingOfficerId === o.id ? (
                    <div className="flex items-center gap-2 flex-1 animate-fade-in">
                      <input
                        value={editOfficerName}
                        onChange={(e) => setEditOfficerName(e.target.value)}
                        className="flex-1 rounded border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-2 py-1 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-1 focus:ring-emerald-500"
                        aria-label="Edit officer name"
                        onKeyDown={(e) => { if (e.key === "Enter") handleSaveOfficer(); if (e.key === "Escape") setEditingOfficerId(null); }}
                        autoFocus
                      />
                      <select
                        value={editOfficerRole}
                        onChange={(e) => setEditOfficerRole(e.target.value)}
                        className="rounded border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-2 py-1 text-sm text-gray-900 dark:text-gray-100 outline-none"
                        aria-label="Edit officer role"
                        title="Edit officer role"
                      >
                        <option value="Director">Director</option>
                        <option value="Secretary">Secretary</option>
                        <option value="Chairperson">Chairperson</option>
                        <option value="Shareholder">Shareholder</option>
                      </select>
                      <Button variant="primary" size="sm" isIconOnly onPress={handleSaveOfficer} isDisabled={savingOfficer} aria-label="Save officer">
                        <Save className="w-3.5 h-3.5" />
                      </Button>
                      <Button variant="ghost" size="sm" isIconOnly onPress={() => setEditingOfficerId(null)} aria-label="Cancel editing">
                        <X className="w-3.5 h-3.5" />
                      </Button>
                    </div>
                  ) : (
                    <>
                      <div>
                        <span className="font-medium text-gray-900 dark:text-gray-100">{o.name}</span>
                        <span className="text-gray-400 dark:text-gray-500 ml-2">({o.role})</span>
                      </div>
                      <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                        <Button variant="ghost" size="sm" isIconOnly onPress={() => handleStartEditOfficer(o)} aria-label={`Edit ${o.name}`}>
                          <Pencil className="w-3 h-3 text-gray-400" />
                        </Button>
                        <Button variant="ghost" size="sm" isIconOnly onPress={() => handleDeleteOfficer(o.id!)} aria-label={`Remove ${o.name}`}>
                          <Trash2 className="w-3 h-3 text-red-400" />
                        </Button>
                      </div>
                    </>
                  )}
                </div>
              ))
            ) : (
              <div className="text-sm text-gray-400 dark:text-gray-500 italic">
                No officers recorded
              </div>
            )}
          </Card.Content>
        </Card>
      </div>

      {/* Charity Info Card */}
      {company.isCharitableOrganisation && (
        <Card className="bg-white dark:bg-neutral-900 shadow-sm border border-gray-200 dark:border-neutral-700 mb-8">
          <Card.Header className="flex flex-row items-center justify-between">
            <Card.Title className="flex items-center gap-2 text-gray-900 dark:text-gray-100">
              <Heart className="w-4 h-4 text-pink-500" />
              Charity Info
            </Card.Title>
            {!editingCharity && (
              <Button variant="outline" size="sm" onPress={startEditingCharity}>
                <Pencil className="w-3.5 h-3.5" />
                Edit
              </Button>
            )}
          </Card.Header>
          <Card.Content>
            {editingCharity ? (
              <div className="space-y-4 animate-fade-in">
                <div className="grid grid-cols-2 gap-4">
                  <div>
                    <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Charity Number</label>
                    <input
                      type="text"
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500"
                      value={charityForm.charityNumber || ""}
                      onChange={(e) => setCharityForm({ ...charityForm, charityNumber: e.target.value })}
                      placeholder="e.g. CHY12345"
                      aria-label="Charity number"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Gross Income</label>
                    <input
                      type="number"
                      className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500"
                      value={charityForm.grossIncome || ""}
                      onChange={(e) => setCharityForm({ ...charityForm, grossIncome: Number(e.target.value) })}
                      placeholder="0.00"
                      aria-label="Gross income"
                    />
                  </div>
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Charitable Objectives</label>
                  <textarea
                    className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 min-h-[80px]"
                    value={charityForm.charitableObjectives || ""}
                    onChange={(e) => setCharityForm({ ...charityForm, charitableObjectives: e.target.value })}
                    placeholder="Describe the charity's objects..."
                    aria-label="Charitable objectives"
                  />
                </div>
                <div>
                  <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Principal Activities</label>
                  <textarea
                    className="w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500 min-h-[80px]"
                    value={charityForm.principalActivities || ""}
                    onChange={(e) => setCharityForm({ ...charityForm, principalActivities: e.target.value })}
                    placeholder="Describe the charity's principal activities..."
                    aria-label="Principal activities"
                  />
                </div>
                <div className="space-y-3">
                  <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                    <input
                      type="checkbox"
                      checked={charityForm.governanceCodeCompliant}
                      onChange={(e) => setCharityForm({ ...charityForm, governanceCodeCompliant: e.target.checked })}
                      className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
                    />
                    Governance Code Compliant
                  </label>
                  <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                    <input
                      type="checkbox"
                      checked={charityForm.trusteeRemunerationPaid}
                      onChange={(e) => setCharityForm({ ...charityForm, trusteeRemunerationPaid: e.target.checked })}
                      className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
                    />
                    Trustee Remuneration Paid
                  </label>
                  {charityForm.trusteeRemunerationPaid && (
                    <div className="ml-6">
                      <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Remuneration Amount</label>
                      <input
                        type="number"
                        className="w-48 rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 outline-none focus:ring-2 focus:ring-emerald-500"
                        value={charityForm.trusteeRemunerationAmount || ""}
                        onChange={(e) => setCharityForm({ ...charityForm, trusteeRemunerationAmount: Number(e.target.value) })}
                        placeholder="0.00"
                        aria-label="Trustee remuneration amount"
                      />
                    </div>
                  )}
                  <label className="flex items-center gap-2 text-sm text-gray-700 dark:text-gray-300">
                    <input
                      type="checkbox"
                      checked={charityForm.hasInternationalTransfers}
                      onChange={(e) => setCharityForm({ ...charityForm, hasInternationalTransfers: e.target.checked })}
                      className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500"
                    />
                    International Transfers
                  </label>
                </div>
                <div className="flex items-center gap-2 justify-end pt-2">
                  <Button variant="ghost" size="sm" onPress={() => setEditingCharity(false)} isDisabled={savingCharity}>
                    Cancel
                  </Button>
                  <Button variant="primary" size="sm" onPress={handleSaveCharity} isDisabled={savingCharity}>
                    <Save className="w-3.5 h-3.5" />
                    {savingCharity ? "Saving..." : "Save Charity Info"}
                  </Button>
                </div>
              </div>
            ) : charityInfo ? (
              <div className="space-y-3 text-sm">
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">Charity No:</span>{" "}
                    <span className="font-medium text-gray-900 dark:text-gray-100">{charityInfo.charityNumber || "\u2014"}</span>
                  </div>
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">SORP Tier:</span>{" "}
                    <Chip size="sm" color={charityInfo.sorpTier === 1 ? "success" : "warning"} variant="soft">
                      Tier {charityInfo.sorpTier}
                    </Chip>
                  </div>
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">Gross Income:</span>{" "}
                    <span className="font-medium text-gray-900 dark:text-gray-100">
                      {new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(charityInfo.grossIncome)}
                    </span>
                  </div>
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">Governance Code:</span>{" "}
                    <Chip size="sm" color={charityInfo.governanceCodeCompliant ? "success" : "default"} variant="soft">
                      {charityInfo.governanceCodeCompliant ? "Compliant" : "Not confirmed"}
                    </Chip>
                  </div>
                </div>
                {charityInfo.charitableObjectives && (
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">Objectives:</span>{" "}
                    <span className="text-gray-900 dark:text-gray-100">{charityInfo.charitableObjectives}</span>
                  </div>
                )}
                {charityInfo.trusteeRemunerationPaid && (
                  <div>
                    <span className="text-gray-500 dark:text-gray-400">Trustee Remuneration:</span>{" "}
                    <span className="font-medium text-gray-900 dark:text-gray-100">
                      {new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(charityInfo.trusteeRemunerationAmount)}
                    </span>
                  </div>
                )}
                {charityInfo.hasInternationalTransfers && (
                  <Chip size="sm" color="warning" variant="soft">International Transfers</Chip>
                )}
              </div>
            ) : (
              <div className="py-4 text-center">
                <Heart className="w-8 h-8 text-gray-300 dark:text-gray-600 mx-auto mb-2" />
                <p className="text-gray-400 dark:text-gray-500 text-sm mb-3">
                  No charity info recorded yet.
                </p>
                <Button variant="primary" size="sm" onPress={startEditingCharity}>
                  <Plus className="w-3.5 h-3.5" />
                  Add Charity Info
                </Button>
              </div>
            )}
          </Card.Content>
        </Card>
      )}

      {/* Share Capital (company-scoped equity) */}
      <ShareCapitalCard companyId={company.id} canWrite={canWriteWorkingPapers} />

      <CompanyPeriodsWorkbench
        company={company}
        showNewPeriod={showNewPeriod}
        periodStart={periodStart}
        periodEnd={periodEnd}
        isFirstYear={isFirstYear}
        creatingPeriod={creatingPeriod}
        canWrite={canWriteWorkingPapers}
        onShowNewPeriod={() => setShowNewPeriod(true)}
        onCancelNewPeriod={() => setShowNewPeriod(false)}
        onPeriodStartChange={setPeriodStart}
        onPeriodEndChange={setPeriodEnd}
        onFirstYearChange={setIsFirstYear}
        onCreatePeriod={handleCreatePeriod}
      />

      {/* Delete Confirmation Modal */}
      <ConfirmModal
        open={showDeleteModal}
        title="Delete Company"
        description={`This will permanently delete "${company.legalName}" and all its accounting periods, transactions, and financial data. This action cannot be undone.`}
        confirmLabel="Delete Company"
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setShowDeleteModal(false)}
      />
    </div>
  );
}
