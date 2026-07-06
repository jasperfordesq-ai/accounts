"use client";

import { Pencil, Trash2 } from "lucide-react";
import { Button } from "@heroui/react";
import type { CharityInfo, Company, Officer } from "@/lib/api";
import { ConfirmModal } from "@/components/ConfirmModal";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { CompanyCharityInfoPanel } from "@/components/company/CompanyCharityInfoPanel";
import { CompanyIdentityEditPanel, type CompanyEditFormValues } from "@/components/company/CompanyIdentityEditPanel";
import { CompanyOfficersPanel } from "@/components/company/CompanyOfficersPanel";
import { CompanyPeriodsWorkbench } from "@/components/company/CompanyPeriodsWorkbench";
import { CompanyStatutoryProfile } from "@/components/company/CompanyStatutoryProfile";
import { CompanyWorkspaceOverview } from "@/components/company/CompanyWorkspaceOverview";
import { PageShell, StatusBadge } from "@/components/workbench";

interface CompanyDetailWorkbenchProps {
  company: Company;
  canWriteWorkingPapers: boolean;
  editing: boolean;
  editForm: CompanyEditFormValues;
  savingCompany: boolean;
  showNewPeriod: boolean;
  periodStart: string;
  periodEnd: string;
  isFirstYear: boolean;
  creatingPeriod: boolean;
  editingOfficerId: number | null;
  editOfficerName: string;
  editOfficerRole: string;
  savingOfficer: boolean;
  showAddOfficer: boolean;
  newOfficerName: string;
  newOfficerRole: string;
  charityInfo: CharityInfo | null;
  charityForm: CharityInfo;
  editingCharity: boolean;
  savingCharity: boolean;
  showDeleteModal: boolean;
  deleting: boolean;
  onStartEditCompany: () => void;
  onEditFormChange: (form: CompanyEditFormValues) => void;
  onSaveCompany: () => void;
  onCancelEditCompany: () => void;
  onDeleteCompanyRequest: () => void;
  onConfirmDeleteCompany: () => void;
  onCancelDeleteCompany: () => void;
  onShowNewPeriod: () => void;
  onCancelNewPeriod: () => void;
  onPeriodStartChange: (value: string) => void;
  onPeriodEndChange: (value: string) => void;
  onFirstYearChange: (value: boolean) => void;
  onCreatePeriod: () => void;
  onShowAddOfficer: () => void;
  onNewOfficerNameChange: (value: string) => void;
  onNewOfficerRoleChange: (value: string) => void;
  onCancelAddOfficer: () => void;
  onAddOfficer: () => void;
  onStartEditOfficer: (officer: Officer) => void;
  onEditOfficerNameChange: (value: string) => void;
  onEditOfficerRoleChange: (value: string) => void;
  onSaveOfficer: () => void;
  onCancelEditOfficer: () => void;
  onDeleteOfficer: (officerId: number) => void;
  onStartEditCharity: () => void;
  onCancelEditCharity: () => void;
  onSaveCharity: () => void;
  onCharityFormChange: (form: CharityInfo) => void;
}

export function CompanyDetailWorkbench({
  company,
  canWriteWorkingPapers,
  editing,
  editForm,
  savingCompany,
  showNewPeriod,
  periodStart,
  periodEnd,
  isFirstYear,
  creatingPeriod,
  editingOfficerId,
  editOfficerName,
  editOfficerRole,
  savingOfficer,
  showAddOfficer,
  newOfficerName,
  newOfficerRole,
  charityInfo,
  charityForm,
  editingCharity,
  savingCharity,
  showDeleteModal,
  deleting,
  onStartEditCompany,
  onEditFormChange,
  onSaveCompany,
  onCancelEditCompany,
  onDeleteCompanyRequest,
  onConfirmDeleteCompany,
  onCancelDeleteCompany,
  onShowNewPeriod,
  onCancelNewPeriod,
  onPeriodStartChange,
  onPeriodEndChange,
  onFirstYearChange,
  onCreatePeriod,
  onShowAddOfficer,
  onNewOfficerNameChange,
  onNewOfficerRoleChange,
  onCancelAddOfficer,
  onAddOfficer,
  onStartEditOfficer,
  onEditOfficerNameChange,
  onEditOfficerRoleChange,
  onSaveOfficer,
  onCancelEditOfficer,
  onDeleteOfficer,
  onStartEditCharity,
  onCancelEditCharity,
  onSaveCharity,
  onCharityFormChange,
}: CompanyDetailWorkbenchProps) {
  return (
    <PageShell
      title={company.legalName}
      subtitle={company.tradingName ? `t/a ${company.tradingName}` : "Company statutory profile and period setup."}
      backHref="/"
      backLabel="Dashboard"
      meta={<CompanyMeta company={company} />}
      actions={
        canWriteWorkingPapers ? (
          <>
            <Button variant="outline" size="sm" onPress={onStartEditCompany} aria-label="Edit company">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button variant="danger" size="sm" onPress={onDeleteCompanyRequest} aria-label="Delete company">
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </>
        ) : undefined
      }
    >
      <div className="space-y-6">
        {editing && (
          <CompanyIdentityEditPanel
            form={editForm}
            saving={savingCompany}
            onFormChange={onEditFormChange}
            onSave={onSaveCompany}
            onCancel={onCancelEditCompany}
          />
        )}

        <CompanyWorkspaceOverview company={company} />

        <CompanyStatutoryProfile company={company} />

        <CompanyOfficersPanel
          officers={company.officers}
          showAddOfficer={showAddOfficer}
          newOfficerName={newOfficerName}
          newOfficerRole={newOfficerRole}
          editingOfficerId={editingOfficerId}
          editOfficerName={editOfficerName}
          editOfficerRole={editOfficerRole}
          savingOfficer={savingOfficer}
          canWrite={canWriteWorkingPapers}
          onShowAddOfficer={onShowAddOfficer}
          onNewOfficerNameChange={onNewOfficerNameChange}
          onNewOfficerRoleChange={onNewOfficerRoleChange}
          onCancelAddOfficer={onCancelAddOfficer}
          onAddOfficer={onAddOfficer}
          onStartEditOfficer={onStartEditOfficer}
          onEditOfficerNameChange={onEditOfficerNameChange}
          onEditOfficerRoleChange={onEditOfficerRoleChange}
          onSaveOfficer={onSaveOfficer}
          onCancelEditOfficer={onCancelEditOfficer}
          onDeleteOfficer={onDeleteOfficer}
        />

        {company.isCharitableOrganisation && (
          <CompanyCharityInfoPanel
            charityInfo={charityInfo}
            charityForm={charityForm}
            editing={editingCharity}
            saving={savingCharity}
            canWrite={canWriteWorkingPapers}
            onStartEdit={onStartEditCharity}
            onCancelEdit={onCancelEditCharity}
            onSave={onSaveCharity}
            onFormChange={onCharityFormChange}
          />
        )}

        <ShareCapitalCard companyId={company.id} canWrite={canWriteWorkingPapers} />

        <CompanyPeriodsWorkbench
          company={company}
          showNewPeriod={showNewPeriod}
          periodStart={periodStart}
          periodEnd={periodEnd}
          isFirstYear={isFirstYear}
          creatingPeriod={creatingPeriod}
          canWrite={canWriteWorkingPapers}
          onShowNewPeriod={onShowNewPeriod}
          onCancelNewPeriod={onCancelNewPeriod}
          onPeriodStartChange={onPeriodStartChange}
          onPeriodEndChange={onPeriodEndChange}
          onFirstYearChange={onFirstYearChange}
          onCreatePeriod={onCreatePeriod}
        />

        <ConfirmModal
          open={showDeleteModal}
          title="Delete Company"
          description={`This will permanently delete "${company.legalName}" and all its accounting periods, transactions, and financial data. This action cannot be undone.`}
          confirmLabel="Delete Company"
          variant="danger"
          loading={deleting}
          onConfirm={onConfirmDeleteCompany}
          onCancel={onCancelDeleteCompany}
        />
      </div>
    </PageShell>
  );
}

function CompanyMeta({ company }: { company: Company }) {
  const periodCount = company.periodCount ?? company.periods?.length ?? 0;
  const manualGate = company.companyType === "PublicLimitedCompany"
    || company.companyType === "PLC"
    || company.companyType === "PrivateUnlimited"
    || company.companyType === "UC"
    || company.isListedSecurities
    || company.isCreditInstitution
    || company.isInsuranceUndertaking
    || company.isPensionFund
    || company.isGroupMember
    || company.isHolding
    || company.isSubsidiary;

  return (
    <>
      <StatusBadge tone={manualGate ? "warn" : "good"}>{manualGate ? "Manual gate" : "Core path"}</StatusBadge>
      <StatusBadge tone={company.isDormant ? "warn" : company.isTrading ? "good" : "info"}>
        {company.isDormant ? "Dormant" : company.isTrading ? "Trading" : "Activity review"}
      </StatusBadge>
      <StatusBadge tone={periodCount > 0 ? "good" : "warn"}>
        {periodCount} period{periodCount === 1 ? "" : "s"}
      </StatusBadge>
    </>
  );
}
