"use client";

import { useEffect, useState, use } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  Card, CardContent, CardHeader, CardTitle,
  Button, Chip, TextField, Input, Label, Checkbox
} from "@heroui/react";
import {
  Building2, Users, Calendar, Plus, Trash2, ArrowRight, MapPin
} from "lucide-react";
import { toast } from "sonner";
import { Pencil, Save, X } from "lucide-react";
import { getCompany, deleteCompany, createPeriod, deleteOfficer, updateOfficer, createOfficer, type Company, type Officer } from "@/lib/api";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { ConfirmModal } from "@/components/ConfirmModal";
import { CompanyDetailSkeleton } from "@/components/Skeleton";

export default function CompanyDetailPage({ params }: { params: Promise<{ companyId: string }> }) {
  const { companyId: id } = use(params);
  const router = useRouter();
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

  const load = () => {
    getCompany(Number(id))
      .then(setCompany)
      .catch((err) => toast.error(err instanceof Error ? err.message : "Failed to load company"))
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [id]);

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

  const statusColor = (status: string) => {
    switch (status) {
      case "Draft": return "default" as const;
      case "Review": return "accent" as const;
      case "Finalised": return "success" as const;
      case "Filed": return "success" as const;
      default: return "default" as const;
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

      <div className="flex items-start justify-between mb-8">
        <div className="flex items-center gap-3">
          <div className="bg-emerald-50 dark:bg-emerald-900/30 p-3 rounded-xl">
            <Building2 className="w-7 h-7 text-emerald-600 dark:text-emerald-400" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {company.legalName}
            </h1>
            {company.tradingName && (
              <p className="text-gray-500 dark:text-gray-400">t/a {company.tradingName}</p>
            )}
          </div>
        </div>
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

      {/* Info cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <Card className="border border-gray-200 dark:border-neutral-700">
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide">
              Registration
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div>
              <span className="text-gray-500 dark:text-gray-400">CRO:</span>{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {company.croNumber || "\u2014"}
              </span>
            </div>
            <div>
              <span className="text-gray-500 dark:text-gray-400">Tax Ref:</span>{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {company.taxReference || "\u2014"}
              </span>
            </div>
            <div>
              <span className="text-gray-500 dark:text-gray-400">Type:</span>{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {company.companyType}
              </span>
            </div>
            <div>
              <span className="text-gray-500 dark:text-gray-400">Incorporated:</span>{" "}
              <span className="font-medium text-gray-900 dark:text-gray-100">
                {company.incorporationDate}
              </span>
            </div>
          </CardContent>
        </Card>

        <Card className="border border-gray-200 dark:border-neutral-700">
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide flex items-center gap-2">
              <MapPin className="w-3.5 h-3.5" /> Address
            </CardTitle>
          </CardHeader>
          <CardContent className="text-sm space-y-1 text-gray-900 dark:text-gray-100">
            {company.registeredOfficeAddress1 && <div>{company.registeredOfficeAddress1}</div>}
            {company.registeredOfficeAddress2 && <div>{company.registeredOfficeAddress2}</div>}
            {company.registeredOfficeCity && <div>{company.registeredOfficeCity}</div>}
            {company.registeredOfficeCounty && <div>Co. {company.registeredOfficeCounty}</div>}
            {company.registeredOfficeEircode && <div>{company.registeredOfficeEircode}</div>}
            {!company.registeredOfficeAddress1 && (
              <div className="text-gray-400 dark:text-gray-500 italic">No address recorded</div>
            )}
          </CardContent>
        </Card>

        <Card className="border border-gray-200 dark:border-neutral-700">
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase tracking-wide flex items-center justify-between">
              <span className="flex items-center gap-2"><Users className="w-3.5 h-3.5" /> Officers</span>
              <Button variant="ghost" size="sm" isIconOnly onPress={() => setShowAddOfficer(true)} aria-label="Add officer">
                <Plus className="w-3.5 h-3.5" />
              </Button>
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
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
          </CardContent>
        </Card>
      </div>

      {/* Accounting Periods */}
      <Card className="border border-gray-200 dark:border-neutral-700">
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="flex items-center gap-2 text-gray-900 dark:text-gray-100">
            <Calendar className="w-4 h-4 text-gray-400" />
            Accounting Periods
          </CardTitle>
          <Button variant="primary" size="sm" onPress={() => setShowNewPeriod(true)}>
            <Plus className="w-3.5 h-3.5" />
            New Period
          </Button>
        </CardHeader>

        {showNewPeriod && (
          <div className="px-6 py-4 bg-emerald-50 dark:bg-emerald-900/20 border-b border-emerald-100 dark:border-emerald-800/30 flex flex-wrap items-end gap-4 animate-slide-down">
            <TextField className="w-44" value={periodStart} onChange={setPeriodStart}>
              <Label>Period Start</Label>
              <Input type="date" />
            </TextField>
            <TextField className="w-44" value={periodEnd} onChange={setPeriodEnd}>
              <Label>Period End</Label>
              <Input type="date" />
            </TextField>
            <Checkbox isSelected={isFirstYear} onChange={setIsFirstYear}>
              First year
            </Checkbox>
            <Button
              variant="primary"
              size="sm"
              onPress={handleCreatePeriod}
              isDisabled={creatingPeriod}
            >
              {creatingPeriod ? "Creating..." : "Create"}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onPress={() => setShowNewPeriod(false)}
            >
              Cancel
            </Button>
          </div>
        )}

        <CardContent>
          {company.periods && company.periods.length > 0 ? (
            <div className="divide-y divide-gray-100 dark:divide-neutral-700">
              {company.periods.map((period) => (
                <Link
                  key={period.id}
                  href={`/companies/${company.id}/periods/${period.id}`}
                  className="flex items-center justify-between py-4 hover:bg-gray-50 dark:hover:bg-neutral-800/50 px-2 rounded-lg group transition-colors"
                >
                  <div className="flex items-center gap-4">
                    <span className="text-sm font-medium text-gray-900 dark:text-gray-100">
                      {period.periodStart} &mdash; {period.periodEnd}
                    </span>
                    {period.isFirstYear && (
                      <Chip size="sm" color="warning" variant="soft">First Year</Chip>
                    )}
                    <Chip size="sm" color={statusColor(period.status)} variant="soft">
                      {period.status}
                    </Chip>
                    {period.sizeClassification && (
                      <span className="text-xs text-gray-400 dark:text-gray-500">
                        Size: {period.sizeClassification.calculatedClass}
                      </span>
                    )}
                  </div>
                  <ArrowRight className="w-4 h-4 text-gray-300 dark:text-gray-600 group-hover:text-emerald-500 transition-colors" />
                </Link>
              ))}
            </div>
          ) : (
            <div className="py-12 text-center">
              <Calendar className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
              <p className="text-gray-400 dark:text-gray-500 text-sm">
                No accounting periods yet. Create one to start preparing accounts.
              </p>
            </div>
          )}
        </CardContent>
      </Card>

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
