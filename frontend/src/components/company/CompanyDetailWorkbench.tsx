"use client";

import { Archive, Pencil } from "lucide-react";
import { Button } from "@heroui/react";
import type { CharityInfo, Company, Officer } from "@/lib/api";
import { ConfirmModal } from "@/components/ConfirmModal";
import { ShareCapitalCard } from "@/components/ShareCapitalCard";
import { CompanyCharityInfoPanel } from "@/components/company/CompanyCharityInfoPanel";
import { CompanyIdentityEditPanel, type CompanyEditFormValues } from "@/components/company/CompanyIdentityEditPanel";
import { CompanyOfficersPanel } from "@/components/company/CompanyOfficersPanel";
import { CompanyPeriodsWorkbench } from "@/components/company/CompanyPeriodsWorkbench";
import { CompanyStatutoryProfile } from "@/components/company/CompanyStatutoryProfile";
import { CompanyAnnualReturnDatePanel } from "@/components/company/CompanyAnnualReturnDatePanel";
import { CompanyWorkspaceOverview } from "@/components/company/CompanyWorkspaceOverview";
import { PageShell, ReadOnlyNotice, StatusBadge } from "@/components/workbench";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import type { ResourceState } from "@/lib/resourceState";

interface CompanyDetailWorkbenchProps {
  company: Company;
  canWriteWorkingPapers: boolean;
  canDeleteCompany: boolean;
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
  editOfficerAppointedDate?: string;
  editOfficerResignedDate?: string;
  savingOfficer: boolean;
  showAddOfficer: boolean;
  newOfficerName: string;
  newOfficerRole: string;
  newOfficerAppointedDate?: string;
  charityInfo: CharityInfo | null;
  charityResourceState?: ResourceState;
  onRetryCharity?: () => void | Promise<void>;
  charityForm: CharityInfo;
  editingCharity: boolean;
  savingCharity: boolean;
  showDeleteModal: boolean;
  deleting: boolean;
  quarantineConfirmation: string;
  quarantineReason: string;
  onStartEditCompany: () => void;
  onEditFormChange: (form: CompanyEditFormValues) => void;
  onSaveCompany: () => void;
  onCancelEditCompany: () => void;
  onDeleteCompanyRequest: () => void;
  onConfirmDeleteCompany: () => void;
  onCancelDeleteCompany: () => void;
  onQuarantineConfirmationChange: (value: string) => void;
  onQuarantineReasonChange: (value: string) => void;
  onShowNewPeriod: () => void;
  onCancelNewPeriod: () => void;
  onPeriodStartChange: (value: string) => void;
  onPeriodEndChange: (value: string) => void;
  onFirstYearChange: (value: boolean) => void;
  onCreatePeriod: () => void;
  onShowAddOfficer: () => void;
  onNewOfficerNameChange: (value: string) => void;
  onNewOfficerRoleChange: (value: string) => void;
  onNewOfficerAppointedDateChange?: (value: string) => void;
  onCancelAddOfficer: () => void;
  onAddOfficer: () => void;
  onStartEditOfficer: (officer: Officer) => void;
  onEditOfficerNameChange: (value: string) => void;
  onEditOfficerRoleChange: (value: string) => void;
  onEditOfficerAppointedDateChange?: (value: string) => void;
  onEditOfficerResignedDateChange?: (value: string) => void;
  onSaveOfficer: () => void;
  onCancelEditOfficer: () => void;
  onDeleteOfficer: (officerId: number) => void | Promise<void>;
  onStartEditCharity: () => void;
  onCancelEditCharity: () => void;
  onSaveCharity: () => void;
  onCharityFormChange: (form: CharityInfo) => void;
  onAnnualReturnDateChanged: () => void | Promise<void>;
}

export function CompanyDetailWorkbench({
  company,
  canWriteWorkingPapers,
  canDeleteCompany,
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
  editOfficerAppointedDate,
  editOfficerResignedDate,
  savingOfficer,
  showAddOfficer,
  newOfficerName,
  newOfficerRole,
  newOfficerAppointedDate,
  charityInfo,
  charityResourceState,
  onRetryCharity,
  charityForm,
  editingCharity,
  savingCharity,
  showDeleteModal,
  deleting,
  quarantineConfirmation,
  quarantineReason,
  onStartEditCompany,
  onEditFormChange,
  onSaveCompany,
  onCancelEditCompany,
  onDeleteCompanyRequest,
  onConfirmDeleteCompany,
  onCancelDeleteCompany,
  onQuarantineConfirmationChange,
  onQuarantineReasonChange,
  onShowNewPeriod,
  onCancelNewPeriod,
  onPeriodStartChange,
  onPeriodEndChange,
  onFirstYearChange,
  onCreatePeriod,
  onShowAddOfficer,
  onNewOfficerNameChange,
  onNewOfficerRoleChange,
  onNewOfficerAppointedDateChange,
  onCancelAddOfficer,
  onAddOfficer,
  onStartEditOfficer,
  onEditOfficerNameChange,
  onEditOfficerRoleChange,
  onEditOfficerAppointedDateChange,
  onEditOfficerResignedDateChange,
  onSaveOfficer,
  onCancelEditOfficer,
  onDeleteOfficer,
  onStartEditCharity,
  onCancelEditCharity,
  onSaveCharity,
  onCharityFormChange,
  onAnnualReturnDateChanged,
}: CompanyDetailWorkbenchProps) {
  return (
    <PageShell
      title={company.legalName}
      subtitle={company.tradingName ? `t/a ${company.tradingName}` : "Company statutory profile and period setup."}
      backHref="/"
      backLabel="Dashboard"
      meta={<CompanyMeta company={company} />}
      actions={
        canWriteWorkingPapers || canDeleteCompany ? (
          <>
            {canWriteWorkingPapers && (
              <Button variant="outline" size="sm" onPress={onStartEditCompany} aria-label="Edit company">
                <Pencil className="h-3.5 w-3.5" />
                Edit
              </Button>
            )}
            {canDeleteCompany && (
              <Button variant="danger" size="sm" onPress={onDeleteCompanyRequest} aria-label="Quarantine company">
                <Archive className="h-3.5 w-3.5" />
                Quarantine
              </Button>
            )}
          </>
        ) : undefined
      }
    >
      <div className="space-y-6">
        {!canWriteWorkingPapers && <ReadOnlyNotice subject="company profile and period setup" />}

        {canWriteWorkingPapers && editing && (
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

        <CompanyAnnualReturnDatePanel
          company={company}
          canWrite={canWriteWorkingPapers}
          onChanged={onAnnualReturnDateChanged}
        />

        <CompanyOfficersPanel
          officers={company.officers}
          showAddOfficer={showAddOfficer}
          newOfficerName={newOfficerName}
          newOfficerRole={newOfficerRole}
          newOfficerAppointedDate={newOfficerAppointedDate}
          editingOfficerId={editingOfficerId}
          editOfficerName={editOfficerName}
          editOfficerRole={editOfficerRole}
          editOfficerAppointedDate={editOfficerAppointedDate}
          editOfficerResignedDate={editOfficerResignedDate}
          savingOfficer={savingOfficer}
          canWrite={canWriteWorkingPapers}
          onShowAddOfficer={onShowAddOfficer}
          onNewOfficerNameChange={onNewOfficerNameChange}
          onNewOfficerRoleChange={onNewOfficerRoleChange}
          onNewOfficerAppointedDateChange={onNewOfficerAppointedDateChange}
          onCancelAddOfficer={onCancelAddOfficer}
          onAddOfficer={onAddOfficer}
          onStartEditOfficer={onStartEditOfficer}
          onEditOfficerNameChange={onEditOfficerNameChange}
          onEditOfficerRoleChange={onEditOfficerRoleChange}
          onEditOfficerAppointedDateChange={onEditOfficerAppointedDateChange}
          onEditOfficerResignedDateChange={onEditOfficerResignedDateChange}
          onSaveOfficer={onSaveOfficer}
          onCancelEditOfficer={onCancelEditOfficer}
          onDeleteOfficer={onDeleteOfficer}
        />

        {company.isCharitableOrganisation && (
          <div className="space-y-3">
            {charityResourceState && (
              <ResourceStateNotice state={charityResourceState} label="charity profile evidence" onRetry={onRetryCharity} />
            )}
            {(!charityResourceState || charityResourceState.status === "loaded" || charityResourceState.status === "empty" || charityResourceState.hasRetainedData) && (
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
          </div>
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

        {canDeleteCompany && (
          <ConfirmModal
            open={showDeleteModal}
            title="Quarantine Company"
            description={`This hides "${company.legalName}" and all owned records from normal workspaces without deleting them. An Owner can recover the complete retained record later.`}
            confirmLabel="Quarantine Company"
            variant="danger"
            loading={deleting}
            confirmDisabled={quarantineConfirmation !== company.legalName || quarantineReason.trim().length < 20}
            onConfirm={onConfirmDeleteCompany}
            onCancel={onCancelDeleteCompany}
          >
            <div className="space-y-4">
              <div>
                <label htmlFor="quarantine-confirmation" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Type the exact legal name to confirm
                </label>
                <input
                  id="quarantine-confirmation"
                  value={quarantineConfirmation}
                  onChange={(event) => onQuarantineConfirmationChange(event.target.value)}
                  autoComplete="off"
                  className="mt-1.5 w-full rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 outline-none focus:border-red-500 focus:ring-2 focus:ring-red-500 dark:border-neutral-600 dark:bg-neutral-800 dark:text-gray-100"
                />
              </div>
              <div>
                <label htmlFor="quarantine-reason" className="block text-sm font-medium text-gray-700 dark:text-gray-300">
                  Retained reason
                </label>
                <textarea
                  id="quarantine-reason"
                  value={quarantineReason}
                  onChange={(event) => onQuarantineReasonChange(event.target.value)}
                  rows={3}
                  maxLength={2000}
                  placeholder="Give a specific reason (minimum 20 characters)."
                  className="mt-1.5 w-full resize-y rounded-lg border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 outline-none focus:border-red-500 focus:ring-2 focus:ring-red-500 dark:border-neutral-600 dark:bg-neutral-800 dark:text-gray-100"
                />
                <p className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                  {quarantineReason.trim().length}/20 minimum characters
                </p>
              </div>
            </div>
          </ConfirmModal>
        )}
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
