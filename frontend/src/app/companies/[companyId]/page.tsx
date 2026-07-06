"use client";

import { useCallback, useEffect, useState, use } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Button } from "@heroui/react";
import {
  Building2
} from "lucide-react";
import { toast } from "sonner";
import { getCompany, updateCompany, deleteCompany, createPeriod, deleteOfficer, updateOfficer, createOfficer, getCharityInfo, saveCharityInfo, type Company, type Officer, type CharityInfo } from "@/lib/api";
import { useAuth } from "@/components/AuthProvider";
import { CompanyDetailSkeleton } from "@/components/Skeleton";
import { type CompanyEditFormValues } from "@/components/company/CompanyIdentityEditPanel";
import { CompanyDetailWorkbench } from "@/components/company/CompanyDetailWorkbench";

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
  const [editForm, setEditForm] = useState<CompanyEditFormValues>({
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
    <CompanyDetailWorkbench
      company={company}
      canWriteWorkingPapers={canWriteWorkingPapers}
      editing={editing}
      editForm={editForm}
      savingCompany={savingCompany}
      showNewPeriod={showNewPeriod}
      periodStart={periodStart}
      periodEnd={periodEnd}
      isFirstYear={isFirstYear}
      creatingPeriod={creatingPeriod}
      editingOfficerId={editingOfficerId}
      editOfficerName={editOfficerName}
      editOfficerRole={editOfficerRole}
      savingOfficer={savingOfficer}
      showAddOfficer={showAddOfficer}
      newOfficerName={newOfficerName}
      newOfficerRole={newOfficerRole}
      charityInfo={charityInfo}
      charityForm={charityForm}
      editingCharity={editingCharity}
      savingCharity={savingCharity}
      showDeleteModal={showDeleteModal}
      deleting={deleting}
      onStartEditCompany={startEditing}
      onEditFormChange={setEditForm}
      onSaveCompany={handleSaveCompany}
      onCancelEditCompany={() => setEditing(false)}
      onDeleteCompanyRequest={() => setShowDeleteModal(true)}
      onConfirmDeleteCompany={handleDelete}
      onCancelDeleteCompany={() => setShowDeleteModal(false)}
      onShowNewPeriod={() => setShowNewPeriod(true)}
      onCancelNewPeriod={() => setShowNewPeriod(false)}
      onPeriodStartChange={setPeriodStart}
      onPeriodEndChange={setPeriodEnd}
      onFirstYearChange={setIsFirstYear}
      onCreatePeriod={handleCreatePeriod}
      onShowAddOfficer={() => setShowAddOfficer(true)}
      onNewOfficerNameChange={setNewOfficerName}
      onNewOfficerRoleChange={setNewOfficerRole}
      onCancelAddOfficer={() => {
        setShowAddOfficer(false);
        setNewOfficerName("");
        setNewOfficerRole("Director");
      }}
      onAddOfficer={handleAddOfficer}
      onStartEditOfficer={handleStartEditOfficer}
      onEditOfficerNameChange={setEditOfficerName}
      onEditOfficerRoleChange={setEditOfficerRole}
      onSaveOfficer={handleSaveOfficer}
      onCancelEditOfficer={() => setEditingOfficerId(null)}
      onDeleteOfficer={handleDeleteOfficer}
      onStartEditCharity={startEditingCharity}
      onCancelEditCharity={() => setEditingCharity(false)}
      onSaveCharity={handleSaveCharity}
      onCharityFormChange={setCharityForm}
    />
  );
}
