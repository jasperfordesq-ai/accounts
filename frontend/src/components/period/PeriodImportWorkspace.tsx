"use client";

import { cloneElement, useId, useRef, useState, type ChangeEvent, type DragEvent, type ReactElement, type ReactNode } from "react";
import { Button, Card, Chip, Spinner } from "@heroui/react";
import { ArrowRight, CheckCircle2, HelpCircle, Scale, Settings, Trash2, Upload } from "lucide-react";
import type { AccountingPeriod, AccountCategory, BankAccount, OpeningBalance } from "@/lib/api";
import { ActionLink, ReadOnlyNotice } from "@/components/workbench";
import { DuplicateReviewPanel } from "@/components/period/DuplicateReviewPanel";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

interface BankAccountForm {
  name: string;
  iban: string;
  openingBalance: string;
  openingBalanceDate: string;
  currency: string;
}

interface OpeningBalanceForm {
  categoryId: string;
  side: string;
  amount: string;
  sourceNote: string;
}

interface ImportResult {
  rowsImported: number;
  duplicateCandidates: number;
  autoCategorised: number;
  importBatchId?: number;
  sourceFilename: string;
  sourceFileSha256: string;
  sourceFileBytes: number;
  warnings: string[];
}

interface PeriodImportWorkspaceProps {
  companyId: number;
  periodId: number;
  canWrite?: boolean;
  classificationHref: string;
  period: AccountingPeriod | null;
  bankAccounts: BankAccount[];
  showBankForm: boolean;
  savingBankAccount: boolean;
  bankForm: BankAccountForm;
  selectedBankAccountId: number | "";
  categories: AccountCategory[];
  openingBalances: OpeningBalance[];
  openingBalanceCategories: AccountCategory[];
  openingBalanceForm: OpeningBalanceForm;
  openingDifference: number;
  savingOpeningBalance: boolean;
  deletingOpeningCategoryId: number | null;
  uploading: boolean;
  uploadResult: ImportResult | null;
  uploadError: string | null;
  onToggleBankForm: () => void;
  onBankFormChange: (form: BankAccountForm) => void;
  onCreateBankAccount: () => void | Promise<void>;
  onSeedCategories: () => void | Promise<void>;
  onOpeningBalanceFormChange: (form: OpeningBalanceForm) => void;
  onSaveOpeningBalance: () => void | Promise<void>;
  onDeleteOpeningBalance: (categoryId: number) => void | Promise<void>;
  onSelectBankAccount: (bankAccountId: number | "") => void;
  onUploadFile: (file: File) => void | Promise<void>;
  onDuplicateDecisionRecorded?: () => void | Promise<void>;
}

export function PeriodImportWorkspace({
  companyId,
  periodId,
  canWrite = true,
  classificationHref,
  period,
  bankAccounts,
  showBankForm,
  savingBankAccount,
  bankForm,
  selectedBankAccountId,
  categories,
  openingBalances,
  openingBalanceCategories,
  openingBalanceForm,
  openingDifference,
  savingOpeningBalance,
  deletingOpeningCategoryId,
  uploading,
  uploadResult,
  uploadError,
  onToggleBankForm,
  onBankFormChange,
  onCreateBankAccount,
  onSeedCategories,
  onOpeningBalanceFormChange,
  onSaveOpeningBalance,
  onDeleteOpeningBalance,
  onSelectBankAccount,
  onUploadFile,
  onDuplicateDecisionRecorded,
}: PeriodImportWorkspaceProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [dragOver, setDragOver] = useState(false);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  function handleFileInputChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (file) void onUploadFile(file);
    event.target.value = "";
  }

  function handleDropzoneDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    event.stopPropagation();
    setDragOver(false);
    const file = event.dataTransfer.files?.[0];
    if (file) void onUploadFile(file);
  }

  return (
    <div className="space-y-6">
      {!canWrite && <ReadOnlyNotice subject="period imports and opening balances" />}
      <Card className="shadow-sm border border-blue-200 dark:border-blue-800 bg-blue-50/30 dark:bg-blue-900/10">
        <Card.Content className="p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <Scale className="w-5 h-5 text-blue-600 dark:text-blue-400" />
              <div>
                <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Company Size Classification</h3>
                <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                  Determine micro/small/medium/large status and filing regime.
                </p>
              </div>
            </div>
            <ActionLink href={classificationHref}>
              Classify Company Size
              <ArrowRight className="w-4 h-4 ml-1.5" />
            </ActionLink>
          </div>
        </Card.Content>
      </Card>

      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
        <Card.Header>
          <div className="flex w-full flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <div>
              <Card.Title className="text-gray-900 dark:text-gray-100">Bank Accounts</Card.Title>
              <Card.Description>
                {bankAccounts.length} bank account{bankAccounts.length !== 1 ? "s" : ""} linked for import and reconciliation.
              </Card.Description>
            </div>
            {canWrite && (
              <Button id="period-add-bank-account-toggle" variant="outline" size="sm" onPress={onToggleBankForm}>
                {showBankForm ? "Cancel" : "Add Bank Account"}
              </Button>
            )}
          </div>
        </Card.Header>
        <Card.Content>
          <div className="space-y-4">
            {canWrite && showBankForm && (
              <div className="rounded-md border border-gray-200 bg-gray-50 p-4 dark:border-neutral-700 dark:bg-neutral-800/40">
                <div className="grid gap-3 md:grid-cols-5">
                  <BankFormField label="Account name">
                    <input
                      value={bankForm.name}
                      onChange={(event) => onBankFormChange({ ...bankForm, name: event.target.value })}
                      className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                      placeholder="Main current account"
                    />
                  </BankFormField>
                  <BankFormField label="IBAN">
                    <input
                      value={bankForm.iban}
                      onChange={(event) => onBankFormChange({ ...bankForm, iban: event.target.value })}
                      className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                      placeholder="IE00..."
                    />
                  </BankFormField>
                  <BankFormField label="Opening balance">
                    <input
                      type="number"
                      step="0.01"
                      value={bankForm.openingBalance}
                      onChange={(event) => onBankFormChange({ ...bankForm, openingBalance: event.target.value })}
                      className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                    />
                  </BankFormField>
                  <BankFormField label="Balance date">
                    <input
                      type="date"
                      value={bankForm.openingBalanceDate || period?.periodStart || ""}
                      onChange={(event) => onBankFormChange({ ...bankForm, openingBalanceDate: event.target.value })}
                      className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                    />
                  </BankFormField>
                  <BankFormField label="Currency">
                    <select
                      value={bankForm.currency}
                      onChange={(event) => onBankFormChange({ ...bankForm, currency: event.target.value })}
                      className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                    >
                      <option value="EUR">EUR</option>
                      <option value="GBP">GBP</option>
                      <option value="USD">USD</option>
                    </select>
                  </BankFormField>
                </div>
                <div className="mt-3 flex justify-end">
                  <Button variant="primary" size="sm" aria-label="Save bank account" onPress={onCreateBankAccount} isDisabled={savingBankAccount}>
                    {savingBankAccount ? <Spinner size="sm" /> : "Save Bank Account"}
                  </Button>
                </div>
              </div>
            )}

            {bankAccounts.length === 0 ? (
              <div className="rounded-md border border-dashed border-gray-300 px-4 py-6 text-sm text-gray-500 dark:border-neutral-700 dark:text-gray-400">
                No bank accounts linked yet. Add the account that matches the year-end bank statement before importing transactions.
              </div>
            ) : (
              <div className="overflow-hidden rounded-md border border-gray-200 dark:border-neutral-700">
                <div className="hidden grid-cols-12 gap-2 bg-gray-50 px-4 py-2 text-xs font-semibold uppercase text-gray-500 dark:bg-neutral-800 dark:text-gray-400 md:grid">
                  <div className="col-span-5">Account</div>
                  <div className="col-span-2">Currency</div>
                  <div className="col-span-3 text-right">Opening balance</div>
                  <div className="col-span-2 text-right">Import target</div>
                </div>
                <div className="divide-y divide-gray-100 dark:divide-neutral-800">
                  {bankAccounts.map((bankAccount) => (
                    <div
                      key={bankAccount.id}
                      className="grid gap-3 px-4 py-3 text-sm md:grid-cols-12 md:items-center md:gap-2"
                    >
                      <div className="min-w-0 md:col-span-5">
                        <p className="font-medium text-gray-900 dark:text-gray-100">{bankAccount.name}</p>
                        <p className="truncate text-xs text-gray-500 dark:text-gray-400">{bankAccount.iban || "No IBAN recorded"}</p>
                      </div>
                      <MobileField label="Currency" className="md:col-span-2">
                        <span className="text-gray-600 dark:text-gray-300">{bankAccount.currency}</span>
                      </MobileField>
                      <MobileField label="Opening balance" className="md:col-span-3 md:text-right">
                        <span className="font-mono text-gray-900 dark:text-gray-100">{formatCurrency(bankAccount.openingBalance)}</span>
                      </MobileField>
                      <MobileField label="Import target" className="md:col-span-2 md:text-right">
                        {selectedBankAccountId === bankAccount.id ? (
                          <Chip color="success" variant="soft" size="sm">Selected</Chip>
                        ) : canWrite ? (
                          <Button variant="ghost" size="sm" onPress={() => onSelectBankAccount(bankAccount.id)}>Use</Button>
                        ) : null}
                      </MobileField>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        </Card.Content>
      </Card>

      {categories.length === 0 && (
        <Card className="shadow-sm border border-amber-200 dark:border-amber-800 bg-amber-50/30 dark:bg-amber-900/10">
          <Card.Content className="p-4">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-3">
                <Settings className="w-5 h-5 text-amber-600 dark:text-amber-400" />
                <div>
                  <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">Chart of Accounts</h3>
                  <p className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">No categories configured. Seed the default Irish chart of accounts.</p>
                </div>
              </div>
              {canWrite && (
                <Button variant="outline" size="sm" onPress={onSeedCategories}>
                  Seed Categories
                </Button>
              )}
            </div>
          </Card.Content>
        </Card>
      )}

      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
        <Card.Header>
          <div className="flex w-full flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <div>
              <Card.Title className="text-gray-900 dark:text-gray-100">Opening Balances & Reserves</Card.Title>
              <Card.Description>
                Enter reviewed opening reserves, share capital, creditors, and other balance-sheet balances before finalising.
              </Card.Description>
            </div>
            <Chip color={Math.abs(openingDifference) < 0.01 ? "success" : "warning"} variant="soft" size="sm">
              Difference {formatCurrency(openingDifference)}
            </Chip>
          </div>
        </Card.Header>
        <Card.Content>
          {canWrite && <div className="grid gap-3 md:grid-cols-5">
            <BankFormField label="Account" className="md:col-span-2">
              <select
                value={openingBalanceForm.categoryId}
                onChange={(event) => onOpeningBalanceFormChange({ ...openingBalanceForm, categoryId: event.target.value })}
                className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
              >
                <option value="">Select account...</option>
                {openingBalanceCategories.map((category) => (
                  <option key={category.id} value={category.id}>
                    {category.code} - {category.name}
                  </option>
                ))}
              </select>
            </BankFormField>
            <BankFormField label="Side">
              <select
                value={openingBalanceForm.side}
                onChange={(event) => onOpeningBalanceFormChange({ ...openingBalanceForm, side: event.target.value })}
                className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
              >
                <option value="debit">Debit</option>
                <option value="credit">Credit</option>
              </select>
            </BankFormField>
            <BankFormField label="Amount">
              <input
                type="number"
                step="0.01"
                value={openingBalanceForm.amount}
                onChange={(event) => onOpeningBalanceFormChange({ ...openingBalanceForm, amount: event.target.value })}
                className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
              />
            </BankFormField>
            <BankFormField label="Evidence note">
              <input
                value={openingBalanceForm.sourceNote}
                onChange={(event) => onOpeningBalanceFormChange({ ...openingBalanceForm, sourceNote: event.target.value })}
                className="w-full rounded-md border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 dark:bg-neutral-900 dark:text-gray-100"
                placeholder="Prior accounts / TB"
              />
            </BankFormField>
          </div>}
          {canWrite && <div className="mt-3 flex justify-end">
            <Button variant="primary" size="sm" aria-label="Save Reviewed Balance — opening evidence" onPress={onSaveOpeningBalance} isDisabled={savingOpeningBalance || categories.length === 0}>
              {savingOpeningBalance ? <Spinner size="sm" /> : "Save Reviewed Balance"}
            </Button>
          </div>}

          <div className="mt-4 overflow-hidden rounded-md border border-gray-200 dark:border-neutral-700">
            <div className="hidden grid-cols-12 gap-2 bg-gray-50 px-4 py-2 text-xs font-semibold uppercase text-gray-500 dark:bg-neutral-800 dark:text-gray-400 md:grid">
              <div className="col-span-5">Account</div>
              <div className="col-span-2 text-right">Debit</div>
              <div className="col-span-2 text-right">Credit</div>
              <div className="col-span-2">Evidence</div>
              <div className="col-span-1 text-right">Action</div>
            </div>
            {openingBalances.length === 0 ? (
              <div className="px-4 py-5 text-sm text-gray-500 dark:text-gray-400">
                No reviewed opening balances entered yet. Bank account opening balances are included automatically, but reserves/equity need an explicit balancing entry.
              </div>
            ) : (
              openingBalances.map((balance) => (
                <div key={balance.id} className="grid gap-3 border-t border-gray-100 px-4 py-3 text-sm dark:border-neutral-800 md:grid-cols-12 md:gap-2">
                  <div className="md:col-span-5">
                    <span className="font-mono text-xs text-gray-500">{balance.accountCategory.code}</span>{" "}
                    <span className="font-medium text-gray-900 dark:text-gray-100">{balance.accountCategory.name}</span>
                  </div>
                  <MobileField label="Debit" className="md:col-span-2 md:text-right">
                    <span className="font-mono">{balance.debit ? formatCurrency(balance.debit) : "-"}</span>
                  </MobileField>
                  <MobileField label="Credit" className="md:col-span-2 md:text-right">
                    <span className="font-mono">{balance.credit ? formatCurrency(balance.credit) : "-"}</span>
                  </MobileField>
                  <MobileField label="Evidence" className="md:col-span-2" title={balance.sourceNote ?? ""}>
                    <span className="min-w-0 truncate text-xs text-gray-500">
                      {balance.reviewed ? "Reviewed" : "Unreviewed"}{balance.sourceNote ? ` - ${balance.sourceNote}` : ""}
                    </span>
                  </MobileField>
                  {canWrite && (
                    <MobileField label="Action" className="md:col-span-1 md:text-right">
                      <Button variant="ghost" size="sm" isIconOnly aria-label={`Delete opening balance for ${balance.accountCategory.name}`} onPress={() => requestDestructiveAction({
                        recordLabel: `opening balance for ${balance.accountCategory.name}`,
                        consequence: `This permanently removes the reviewed ${balance.accountCategory.code} opening balance (${balance.debit ? `debit ${formatCurrency(balance.debit)}` : `credit ${formatCurrency(balance.credit ?? 0)}`}) and its source evidence. Final outputs may stop balancing.`,
                        onConfirm: () => onDeleteOpeningBalance(balance.accountCategoryId),
                        successAnnouncement: `Opening balance for ${balance.accountCategory.name} was removed.`,
                      })} isDisabled={deletingOpeningCategoryId === balance.accountCategoryId}>
                        {deletingOpeningCategoryId === balance.accountCategoryId ? <Spinner size="sm" /> : <Trash2 className="h-4 w-4" />}
                      </Button>
                    </MobileField>
                  )}
                </div>
              ))
            )}
          </div>
        </Card.Content>
      </Card>

      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">Import Transactions</Card.Title>
          <Card.Description>Upload bank statements in CSV format (AIB, BOI, Revolut, Stripe)</Card.Description>
        </Card.Header>
        <Card.Content>
          {canWrite ? <><div className="mb-4">
            <label htmlFor="bank-account-select" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">
              Import into bank account
            </label>
            <select
              id="bank-account-select"
              value={selectedBankAccountId}
              onChange={(event) => onSelectBankAccount(event.target.value ? Number(event.target.value) : "")}
              className="w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 shadow-sm transition-colors focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100"
              title="Select bank account"
              aria-label="Select bank account"
            >
              <option value="">Select a bank account...</option>
              {bankAccounts.map((bankAccount) => (
                <option key={bankAccount.id} value={bankAccount.id}>
                  {bankAccount.name}{bankAccount.iban ? ` (${bankAccount.iban})` : ""} - {bankAccount.currency}
                </option>
              ))}
            </select>
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept=".csv"
            className="hidden"
            onChange={handleFileInputChange}
            aria-label="Upload CSV file"
          />

          <div
            className={`border-2 border-dashed rounded-xl p-10 text-center transition-all cursor-pointer ${
              dragOver
                ? "border-emerald-500 bg-emerald-50 dark:bg-emerald-900/20 scale-[1.01]"
                : "border-[var(--control-border)] hover:border-emerald-500"
            }`}
            onClick={() => fileInputRef.current?.click()}
            onDrop={handleDropzoneDrop}
            onDragOver={(event) => { event.preventDefault(); event.stopPropagation(); setDragOver(true); }}
            onDragEnter={(event) => { event.preventDefault(); setDragOver(true); }}
            onDragLeave={(event) => { event.preventDefault(); setDragOver(false); }}
            role="button"
            tabIndex={0}
            aria-label="Upload CSV file by clicking or dragging"
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") fileInputRef.current?.click();
            }}
          >
            {uploading ? (
              <>
                <Spinner size="sm" className="mx-auto mb-3" />
                <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                  Uploading and processing...
                </p>
              </>
            ) : dragOver ? (
              <>
                <Upload className="w-10 h-10 text-emerald-500 mx-auto mb-3" />
                <p className="text-sm font-medium text-emerald-700 dark:text-emerald-400">
                  Drop your CSV file here
                </p>
              </>
            ) : (
              <>
                <Upload className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
                <p className="text-sm font-medium text-gray-700 dark:text-gray-300">
                  Drag and drop a CSV file here, or click to browse
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                  Supports AIB, BOI, Revolut, and Stripe CSV formats
                </p>
              </>
            )}
          </div></> : (
            <p className="text-sm text-gray-500 dark:text-gray-400">
              Imported transaction evidence remains visible elsewhere in the period workspace. Uploading requires Owner or Accountant access.
            </p>
          )}

          {uploadError && (
            <div className="mt-4 rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 animate-fade-in">
              {uploadError}
            </div>
          )}
        </Card.Content>
      </Card>

      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
        <Card.Header>
          <Card.Title className="text-gray-900 dark:text-gray-100">Import Status</Card.Title>
        </Card.Header>
        <Card.Content>
          {uploadResult ? (
            <div className="space-y-3 animate-fade-in">
              <div className="flex items-center gap-3 text-sm text-emerald-700 dark:text-emerald-400">
                <CheckCircle2 className="w-5 h-5 text-emerald-600 dark:text-emerald-500" />
                <span className="font-medium">{uploadResult.warnings.length > 0 ? "Import completed with retained warnings" : "Import completed successfully"}</span>
              </div>
              <div className="grid grid-cols-3 gap-4">
                <ImportMetric tone="emerald" value={uploadResult.rowsImported} label="Rows Imported" />
                <ImportMetric tone="gray" value={uploadResult.duplicateCandidates} label="Needs Duplicate Review" />
                <ImportMetric tone="blue" value={uploadResult.autoCategorised} label="Auto-Categorised" />
              </div>
              <p className="break-words text-xs text-gray-500 dark:text-gray-400">
                {uploadResult.importBatchId ? `Batch #${uploadResult.importBatchId} · ` : ""}{uploadResult.sourceFilename} · {uploadResult.sourceFileBytes.toLocaleString("en-IE")} bytes · SHA-256 <span className="font-mono" title={uploadResult.sourceFileSha256}>{uploadResult.sourceFileSha256.slice(0, 12)}…</span>
              </p>
              {uploadResult.warnings.length > 0 && (
                <div className="rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
                  <p className="font-semibold">{uploadResult.warnings.length} import warning{uploadResult.warnings.length === 1 ? "" : "s"} retained with this batch</p>
                  <ul className="mt-1 list-disc space-y-1 pl-5">
                    {uploadResult.warnings.slice(0, 20).map((warning, index) => <li key={`${index}-${warning}`}>{warning}</li>)}
                  </ul>
                  {uploadResult.warnings.length > 20 && <p className="mt-2 font-medium">Showing the first 20 warnings; {uploadResult.warnings.length - 20} more are retained with the batch evidence.</p>}
                </div>
              )}
            </div>
          ) : (
            <div className="flex items-center gap-3 text-sm text-gray-500 dark:text-gray-400">
              <HelpCircle className="w-5 h-5" />
              <span>No imports have been processed for this period yet.</span>
            </div>
          )}
        </Card.Content>
      </Card>

      <DuplicateReviewPanel
        companyId={companyId}
        periodId={periodId}
        canWrite={canWrite}
        periodLocked={period?.lockedAt != null || period?.status === "Finalised" || period?.status === "Filed"}
        refreshToken={uploadResult}
        onDecisionRecorded={onDuplicateDecisionRecorded}
      />
      {destructiveActionConfirmation}
    </div>
  );
}

function BankFormField({
  label,
  className,
  children,
}: {
  label: string;
  className?: string;
  children: ReactElement<{ id?: string }>;
}) {
  const generatedId = useId();
  const controlId = children.props.id ?? `${generatedId}-control`;

  return (
    <div className={className}>
      <label htmlFor={controlId} className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">{label}</label>
      {cloneElement(children, { id: controlId })}
    </div>
  );
}

function MobileField({
  label,
  className,
  title,
  children,
}: {
  label: string;
  className?: string;
  title?: string;
  children: ReactNode;
}) {
  return (
    <div className={`grid grid-cols-[8rem_minmax(0,1fr)] items-center gap-3 md:block ${className ?? ""}`} title={title}>
      <span className="text-xs font-semibold uppercase text-gray-500 dark:text-gray-400 md:hidden">{label}</span>
      {children}
    </div>
  );
}

function ImportMetric({
  tone,
  value,
  label,
}: {
  tone: "emerald" | "gray" | "blue";
  value: number;
  label: string;
}) {
  const classes = {
    emerald: "bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-400",
    gray: "bg-gray-50 dark:bg-neutral-800 text-gray-700 dark:text-gray-300",
    blue: "bg-blue-50 dark:bg-blue-900/20 text-blue-700 dark:text-blue-400",
  }[tone];

  return (
    <div className={`rounded-lg p-4 text-center ${classes}`}>
      <p className="text-2xl font-bold">{value}</p>
      <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">{label}</p>
    </div>
  );
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-IE", {
    style: "currency",
    currency: "EUR",
  }).format(amount);
}
