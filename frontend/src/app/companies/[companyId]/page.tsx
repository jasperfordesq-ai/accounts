"use client";

import { useCallback, useEffect, useMemo, useState, use } from "react";
import {
  Building2
} from "lucide-react";
import { toast } from "sonner";
import { ApiError, getCompany, updateCompany, quarantineCompany, createPeriod, deleteOfficer, updateOfficer, createOfficer, getCharityInfo, saveCharityInfo, type Company, type Officer, type CharityInfo } from "@/lib/api";
import { useAuth } from "@/components/AuthProvider";
import { CompanyDetailSkeleton } from "@/components/Skeleton";
import { type CompanyEditFormValues } from "@/components/company/CompanyIdentityEditPanel";
import { CompanyDetailWorkbench } from "@/components/company/CompanyDetailWorkbench";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import { ActionLink } from "@/components/workbench";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  completeResourceLoad,
  failResourceLoad,
  type ResourceState,
} from "@/lib/resourceState";
import { useGuardedRouter, useUnsavedChanges } from "@/lib/useUnsavedChanges";

export default function CompanyDetailPage({ params }: { params: Promise<{ companyId: string }> }) {
  const { companyId: id } = use(params);
  const router = useGuardedRouter();
  const { canWriteWorkingPapers, permissions } = useAuth();
  const [company, setCompany] = useState<Company | null>(null);
  const [companyState, setCompanyState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [showNewPeriod, setShowNewPeriod] = useState(false);
  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [isFirstYear, setIsFirstYear] = useState(false);
  const [creatingPeriod, setCreatingPeriod] = useState(false);

  // Delete confirmation modal state
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [quarantineConfirmation, setQuarantineConfirmation] = useState("");
  const [quarantineReason, setQuarantineReason] = useState("");

  // Officer management
  const [editingOfficerId, setEditingOfficerId] = useState<number | null>(null);
  const [editOfficerName, setEditOfficerName] = useState("");
  const [editOfficerRole, setEditOfficerRole] = useState("");
  const [editOfficerAppointedDate, setEditOfficerAppointedDate] = useState("");
  const [editOfficerResignedDate, setEditOfficerResignedDate] = useState("");
  const [savingOfficer, setSavingOfficer] = useState(false);
  const [showAddOfficer, setShowAddOfficer] = useState(false);
  const [newOfficerName, setNewOfficerName] = useState("");
  const [newOfficerRole, setNewOfficerRole] = useState("Director");
  const [newOfficerAppointedDate, setNewOfficerAppointedDate] = useState("");

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
  const [charityState, setCharityState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [editingCharity, setEditingCharity] = useState(false);
  const [charityForm, setCharityForm] = useState<CharityInfo>({
    charityNumber: "",
    charityType: "",
    grossIncome: 0,
    sorpTier: 1,
    charitableObjectives: "",
    principalActivities: "",
    governanceCodeCompliant: null,
    hasInternationalTransfers: false,
    trusteeRemunerationPaid: false,
    trusteeRemunerationAmount: 0,
  });
  const [savingCharity, setSavingCharity] = useState(false);

  const hasCompanyDraft = useMemo(() => {
    const companyEditDirty = Boolean(editing && company && (
      editForm.legalName !== (company.legalName ?? "")
      || editForm.tradingName !== (company.tradingName ?? "")
      || editForm.croNumber !== (company.croNumber ?? "")
      || editForm.taxReference !== (company.taxReference ?? "")
      || editForm.companyType !== (company.companyType ?? "Private")
      || editForm.isTrading !== (company.isTrading ?? true)
      || editForm.isDormant !== (company.isDormant ?? false)
    ));
    const charityEditDirty = Boolean(editingCharity && (
      charityForm.charityNumber !== (charityInfo?.charityNumber ?? "")
      || charityForm.charityType !== (charityInfo?.charityType ?? "")
      || charityForm.grossIncome !== (charityInfo?.grossIncome ?? 0)
      || charityForm.sorpTier !== (charityInfo?.sorpTier ?? 1)
      || charityForm.charitableObjectives !== (charityInfo?.charitableObjectives ?? "")
      || charityForm.principalActivities !== (charityInfo?.principalActivities ?? "")
      || charityForm.governanceCodeCompliant !== (charityInfo?.governanceCodeCompliant ?? null)
      || (charityForm.governanceCodeNote ?? "") !== (charityInfo?.governanceCodeNote ?? "")
      || (charityForm.governanceEvidenceReference ?? "") !== (charityInfo?.governanceEvidenceReference ?? "")
      || (charityForm.governanceEvidenceArtifact ?? "") !== ""
      || charityForm.hasInternationalTransfers !== (charityInfo?.hasInternationalTransfers ?? false)
      || (charityForm.internationalTransferDetails ?? "") !== (charityInfo?.internationalTransferDetails ?? "")
      || charityForm.trusteeRemunerationPaid !== (charityInfo?.trusteeRemunerationPaid ?? false)
      || charityForm.trusteeRemunerationAmount !== (charityInfo?.trusteeRemunerationAmount ?? 0)
    ));
    const editedOfficer = company?.officers?.find((officer) => officer.id === editingOfficerId);
    const officerEditDirty = Boolean(editingOfficerId && editedOfficer && (
      editOfficerName !== editedOfficer.name
      || editOfficerRole !== editedOfficer.role
      || editOfficerAppointedDate !== (editedOfficer.appointedDate ?? "")
      || editOfficerResignedDate !== (editedOfficer.resignedDate ?? "")
    ));
    return companyEditDirty
      || charityEditDirty
      || (showNewPeriod && (periodStart !== "" || periodEnd !== "" || isFirstYear))
      || officerEditDirty
      || (showAddOfficer && (
        newOfficerName !== ""
        || newOfficerRole !== "Director"
        || newOfficerAppointedDate !== ""
      ))
      || (showDeleteModal && (quarantineConfirmation !== "" || quarantineReason !== ""));
  }, [
    charityForm, charityInfo, company, editForm, editOfficerAppointedDate, editOfficerName,
    editOfficerResignedDate, editOfficerRole, editing, editingCharity, editingOfficerId,
    isFirstYear, newOfficerAppointedDate, newOfficerName, newOfficerRole,
    periodEnd, periodStart, quarantineConfirmation, quarantineReason, showAddOfficer,
    showDeleteModal, showNewPeriod,
  ]);
  useUnsavedChanges(hasCompanyDraft);

  const startEditingCharity = () => {
    if (charityInfo) {
      setCharityForm({ ...charityInfo });
    }
    setEditingCharity(true);
  };

  const handleSaveCharity = async () => {
    if (!charityForm.charityNumber?.trim() || !charityForm.charityType?.trim()) {
      toast.error("Charity number and legal form are required");
      return;
    }
    if (!charityForm.charitableObjectives?.trim() || !charityForm.principalActivities?.trim()) {
      toast.error("Charitable objectives and principal activities are required");
      return;
    }
    if (charityForm.governanceCodeCompliant === null) {
      toast.error("Select an explicit Charities Governance Code answer");
      return;
    }
    if (!charityForm.governanceEvidenceReference?.trim()) {
      toast.error("A governance evidence reference is required");
      return;
    }
    const existingEvidenceStillApplies = Boolean(
      charityInfo?.governanceEvidenceArtifactSha256
      && charityInfo.governanceCodeCompliant === charityForm.governanceCodeCompliant
      && charityInfo.governanceCodeNote === charityForm.governanceCodeNote
      && charityInfo.governanceEvidenceReference === charityForm.governanceEvidenceReference,
    );
    if (!charityForm.governanceEvidenceArtifact && !existingEvidenceStillApplies) {
      toast.error("Attach the governance review artifact for this answer");
      return;
    }
    if (charityForm.governanceCodeCompliant === false && !charityForm.governanceCodeNote?.trim()) {
      toast.error("Explain the governance position when the answer is No");
      return;
    }
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

  const loadCharity = useCallback(async () => {
    setCharityState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const result = await getCharityInfo(Number(id));
      if (result && "charityNumber" in result) {
        setCharityInfo(result as CharityInfo);
        setCharityState(completeResourceLoad(false));
      } else {
        setCharityInfo(null);
        setCharityState(completeResourceLoad(true));
      }
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load charity information";
      setCharityState((current) => failResourceLoad({
        failedResourceKeys: ["charity-info"],
        errors: { "charity-info": message },
      }, current.hasRetainedData));
    }
  }, [id]);

  const load = useCallback(async () => {
    setCompanyState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const companyData = await getCompany(Number(id));
      setCompany(companyData);
      setCompanyState(completeResourceLoad(false));
      if (companyData.isCharitableOrganisation) await loadCharity();
      else setCharityState(completeResourceLoad(true));
    } catch (loadError) {
      if (loadError instanceof ApiError && loadError.isNotFound) {
        setCompany(null);
        setCompanyState(completeResourceLoad(true));
        return;
      }
      const message = loadError instanceof Error ? loadError.message : "Failed to load company";
      setCompanyState((current) => failResourceLoad({
        failedResourceKeys: ["company"],
        errors: { company: message },
      }, current.hasRetainedData));
      toast.error(message);
    }
  }, [id, loadCharity]);

  useEffect(() => { load(); }, [load]);

  const handleDelete = async () => {
    if (!company || quarantineConfirmation !== company.legalName || quarantineReason.trim().length < 20) {
      toast.error("Enter the exact legal name and a specific reason before quarantining this company.");
      return;
    }
    setDeleting(true);
    try {
      await quarantineCompany(Number(id), {
        confirmation: quarantineConfirmation,
        reason: quarantineReason.trim(),
      });
      toast.success("Company quarantined. Its retained records can be recovered by an Owner.");
      router.pushAfterSave("/");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to quarantine company");
      setDeleting(false);
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
    setEditOfficerAppointedDate(officer.appointedDate || "");
    setEditOfficerResignedDate(officer.resignedDate || "");
  };

  const handleSaveOfficer = async () => {
    if (!editingOfficerId || !editOfficerName.trim()) return;
    if (editOfficerRole === "Director" && !editOfficerAppointedDate) {
      toast.error("A director appointment date is required for period trustee population review");
      return;
    }
    setSavingOfficer(true);
    try {
      await updateOfficer(Number(id), editingOfficerId, {
        name: editOfficerName.trim(),
        role: editOfficerRole,
        appointedDate: editOfficerAppointedDate || undefined,
        resignedDate: editOfficerResignedDate || undefined,
      });
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
      throw err;
    }
  };

  const handleAddOfficer = async () => {
    if (!newOfficerName.trim()) return;
    if (newOfficerRole === "Director" && !newOfficerAppointedDate) {
      toast.error("A director appointment date is required for period trustee population review");
      return;
    }
    setSavingOfficer(true);
    try {
      await createOfficer(Number(id), {
        name: newOfficerName.trim(),
        role: newOfficerRole,
        appointedDate: newOfficerAppointedDate || undefined,
      } as Officer);
      toast.success("Officer added");
      setNewOfficerName("");
      setNewOfficerRole("Director");
      setNewOfficerAppointedDate("");
      setShowAddOfficer(false);
      load();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to add officer");
    } finally {
      setSavingOfficer(false);
    }
  };

  if (companyState.status === "loading" && !companyState.hasRetainedData) return <CompanyDetailSkeleton />;

  if (!company) {
    if (companyState.status !== "empty") {
      return (
        <div className="mx-auto max-w-3xl space-y-4 px-4 py-12">
          <ResourceStateNotice state={companyState} label="company record" onRetry={load} />
          <ActionLink href="/" variant="ghost">Back to Dashboard</ActionLink>
        </div>
      );
    }
    return (
      <div className="text-center py-12">
        <Building2 className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
        <p className="text-[var(--muted-foreground)] font-medium">Company not found</p>
        <ActionLink href="/" variant="ghost" className="mt-3">Back to Dashboard</ActionLink>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <ResourceStateNotice state={companyState} label="company record" onRetry={load} />
      <CompanyDetailWorkbench
      company={company}
      canWriteWorkingPapers={canWriteWorkingPapers}
      canDeleteCompany={permissions.canDeleteCompany}
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
      editOfficerAppointedDate={editOfficerAppointedDate}
      editOfficerResignedDate={editOfficerResignedDate}
      savingOfficer={savingOfficer}
      showAddOfficer={showAddOfficer}
      newOfficerName={newOfficerName}
      newOfficerRole={newOfficerRole}
      newOfficerAppointedDate={newOfficerAppointedDate}
      charityInfo={charityInfo}
      charityResourceState={charityState}
      onRetryCharity={loadCharity}
      charityForm={charityForm}
      editingCharity={editingCharity}
      savingCharity={savingCharity}
      showDeleteModal={showDeleteModal}
      deleting={deleting}
      quarantineConfirmation={quarantineConfirmation}
      quarantineReason={quarantineReason}
      onStartEditCompany={startEditing}
      onEditFormChange={setEditForm}
      onSaveCompany={handleSaveCompany}
      onCancelEditCompany={() => setEditing(false)}
      onDeleteCompanyRequest={() => {
        setQuarantineConfirmation("");
        setQuarantineReason("");
        setShowDeleteModal(true);
      }}
      onConfirmDeleteCompany={handleDelete}
      onCancelDeleteCompany={() => {
        setShowDeleteModal(false);
        setQuarantineConfirmation("");
        setQuarantineReason("");
      }}
      onQuarantineConfirmationChange={setQuarantineConfirmation}
      onQuarantineReasonChange={setQuarantineReason}
      onShowNewPeriod={() => setShowNewPeriod(true)}
      onCancelNewPeriod={() => setShowNewPeriod(false)}
      onPeriodStartChange={setPeriodStart}
      onPeriodEndChange={setPeriodEnd}
      onFirstYearChange={setIsFirstYear}
      onCreatePeriod={handleCreatePeriod}
      onShowAddOfficer={() => setShowAddOfficer(true)}
      onNewOfficerNameChange={setNewOfficerName}
      onNewOfficerRoleChange={setNewOfficerRole}
      onNewOfficerAppointedDateChange={setNewOfficerAppointedDate}
      onCancelAddOfficer={() => {
        setShowAddOfficer(false);
        setNewOfficerName("");
        setNewOfficerRole("Director");
        setNewOfficerAppointedDate("");
      }}
      onAddOfficer={handleAddOfficer}
      onStartEditOfficer={handleStartEditOfficer}
      onEditOfficerNameChange={setEditOfficerName}
      onEditOfficerRoleChange={setEditOfficerRole}
      onEditOfficerAppointedDateChange={setEditOfficerAppointedDate}
      onEditOfficerResignedDateChange={setEditOfficerResignedDate}
      onSaveOfficer={handleSaveOfficer}
      onCancelEditOfficer={() => setEditingOfficerId(null)}
      onDeleteOfficer={handleDeleteOfficer}
      onStartEditCharity={startEditingCharity}
      onCancelEditCharity={() => setEditingCharity(false)}
      onSaveCharity={handleSaveCharity}
      onCharityFormChange={setCharityForm}
      onAnnualReturnDateChanged={load}
    />
    </div>
  );
}
