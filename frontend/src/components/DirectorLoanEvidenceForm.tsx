"use client";

import { Button, Spinner } from "@heroui/react";
import { Plus, Trash2, X } from "lucide-react";
import { MoneyInput } from "@/components/workbench";
import type { DirectorLoanMovement, DirectorLoanRow } from "@/lib/api";
import type { DirectorOption } from "@/components/DirectorLoansManager";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";

const inputClass = "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 outline-none transition-colors focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";
const labelClass = "mb-1 block text-xs font-medium text-gray-600 dark:text-gray-400";
const sectionClass = "space-y-3 rounded-xl border border-gray-200 p-4 dark:border-neutral-700";

export function deriveDirectorLoanBalances(openingBalance: number, movements: DirectorLoanMovement[]) {
  const ordered = movements
    .map((movement, index) => ({ movement, index }))
    .sort((left, right) => left.movement.movementDate.localeCompare(right.movement.movementDate) || left.index - right.index);
  let balance = openingBalance;
  let maximumBalance = Math.max(0, openingBalance);
  let advances = 0;
  let repayments = 0;
  for (const { movement } of ordered) {
    if (movement.movementType === "Advance") {
      advances += movement.amount;
      balance += movement.amount;
    } else {
      repayments += movement.amount;
      balance -= movement.amount;
    }
    maximumBalance = Math.max(maximumBalance, balance);
  }
  return { advances, repayments, closingBalance: balance, maximumBalance };
}

export function DirectorLoanEvidenceForm({
  form,
  directors,
  editing,
  saving,
  onChange,
  onCancel,
  onSubmit,
}: {
  form: DirectorLoanRow;
  directors: DirectorOption[];
  editing: boolean;
  saving: boolean;
  onChange: (next: DirectorLoanRow) => void;
  onCancel?: () => void;
  onSubmit: () => void;
}) {
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();
  const derived = deriveDirectorLoanBalances(form.openingBalance, form.balanceMovements);
  const set = <Key extends keyof DirectorLoanRow>(key: Key, value: DirectorLoanRow[Key]) => onChange({ ...form, [key]: value });
  const setMovement = (index: number, patch: Partial<DirectorLoanMovement>) => set(
    "balanceMovements",
    form.balanceMovements.map((movement, movementIndex) => movementIndex === index ? { ...movement, ...patch } : movement),
  );
  const addMovement = () => set("balanceMovements", [
    ...form.balanceMovements,
    {
      movementDate: form.arrangementDate ?? "",
      movementType: "Advance",
      amount: 0,
      evidenceReference: undefined,
    },
  ]);

  return (
    <div className="space-y-4" aria-label="Director-loan statutory evidence form">
      <fieldset className={sectionClass}>
        <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">1. Arrangement and counterparty</legend>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <SelectField
            label="Counterparty type"
            value={form.counterpartyType}
            onChange={(value) => {
              const counterpartyType = value as DirectorLoanRow["counterpartyType"];
              onChange({
                ...form,
                counterpartyType,
                directorId: counterpartyType === "GroupCompany" ? undefined : form.directorId ?? directors[0]?.id,
                counterpartyName: counterpartyType === "Director" ? undefined : form.counterpartyName,
              });
            }}
            options={[
              ["Director", "Director"],
              ["ConnectedPerson", "Person connected with a director"],
              ["GroupCompany", "Group company (section 243)"],
            ]}
          />
          {form.counterpartyType !== "GroupCompany" && (
            <SelectField
              label="Related director"
              value={form.directorId?.toString() ?? ""}
              onChange={(value) => set("directorId", value ? Number(value) : undefined)}
              options={directors.map((director) => [director.id.toString(), director.name])}
              placeholder="Select director"
            />
          )}
          {form.counterpartyType !== "Director" && (
            <TextField
              label={form.counterpartyType === "GroupCompany" ? "Group-company legal name" : "Connected person's name"}
              value={form.counterpartyName}
              onChange={(value) => set("counterpartyName", value)}
            />
          )}
          <SelectField
            label="Arrangement type"
            value={form.arrangementType}
            onChange={(value) => set("arrangementType", value as DirectorLoanRow["arrangementType"])}
            options={[
              ["Loan", "Loan"],
              ["QuasiLoan", "Quasi-loan"],
              ["CreditTransaction", "Credit transaction"],
              ["GuaranteeOrSecurity", "Guarantee or security"],
            ]}
          />
          <DateField label="Arrangement date" value={form.arrangementDate} onChange={(value) => set("arrangementDate", value)} />
        </div>
      </fieldset>

      <fieldset className={sectionClass}>
        <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">2. Dated balance ledger</legend>
        <p className="text-xs text-[var(--muted-foreground)]">
          Advances and repayments are derived from this dated evidence ledger. The maximum and closing balances cannot be typed over.
        </p>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <MoneyInput label="Opening balance" ariaLabel="Director-loan opening balance" value={form.openingBalance} onValueChange={(value) => set("openingBalance", value)} />
          <DerivedAmount label="Advances" amount={derived.advances} />
          <DerivedAmount label="Repayments" amount={derived.repayments} />
          <DerivedAmount label="Maximum during year" amount={derived.maximumBalance} />
          <DerivedAmount label="Closing balance" amount={derived.closingBalance} />
        </div>
        <div className="space-y-2">
          {form.balanceMovements.map((movement, index) => (
            <div key={`${movement.id ?? "new"}-${index}`} className="grid gap-2 rounded-lg bg-gray-50 p-3 sm:grid-cols-[1fr_1fr_1fr_2fr_auto] sm:items-end dark:bg-neutral-900/60">
              <DateField label="Movement date" value={movement.movementDate || undefined} onChange={(value) => setMovement(index, { movementDate: value ?? "" })} />
              <SelectField
                label="Type"
                value={movement.movementType}
                onChange={(value) => setMovement(index, { movementType: value as DirectorLoanMovement["movementType"] })}
                options={[["Advance", "Advance"], ["Repayment", "Repayment"]]}
              />
              <MoneyInput label="Amount" ariaLabel={`Movement ${index + 1} amount`} value={movement.amount} onValueChange={(value) => setMovement(index, { amount: value })} />
              <TextField label="Evidence reference" value={movement.evidenceReference} onChange={(value) => setMovement(index, { evidenceReference: value })} placeholder="Bank ledger / voucher / file hash" />
              <button type="button" onClick={() => requestDestructiveAction({
                recordLabel: `director-loan movement ${index + 1}`,
                consequence: `This removes the ${movement.movementType.toLowerCase()} of ${new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(movement.amount)} dated ${movement.movementDate || "without a date"} from the current evidence draft. Saving afterward will permanently change the retained balance history.`,
                onConfirm: () => set("balanceMovements", form.balanceMovements.filter((_, movementIndex) => movementIndex !== index)),
                successAnnouncement: `Director-loan movement ${index + 1} was removed from the draft.`,
              })} className="rounded p-2 text-red-500 hover:bg-red-50" aria-label={`Remove movement ${index + 1}`}>
                <Trash2 className="h-4 w-4" />
              </button>
            </div>
          ))}
          <Button variant="ghost" size="sm" onPress={addMovement}><Plus className="mr-1 h-4 w-4" /> Add dated movement</Button>
        </div>
      </fieldset>

      <fieldset className={sectionClass}>
        <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">3. Section 236 terms and section 307 particulars</legend>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <SelectField
            label="Written-terms assessment"
            value={form.termsStatus}
            onChange={(value) => set("termsStatus", value as DirectorLoanRow["termsStatus"])}
            options={[
              ["Unassessed", "Not assessed"],
              ["NotWritten", "Terms not in writing"],
              ["WrittenComplete", "Written and complete"],
              ["WrittenAmbiguousRepayment", "Written; repayment ambiguous"],
              ["WrittenAmbiguousInterest", "Written; interest ambiguous"],
              ["WrittenAmbiguousRepaymentAndInterest", "Written; repayment and interest ambiguous"],
            ]}
          />
          <NumberField label="Interest rate %" value={form.interestRate} onChange={(value) => set("interestRate", value)} />
          <MoneyInput label="Interest charged" ariaLabel="Interest charged" value={form.interestCharged} onValueChange={(value) => set("interestCharged", value)} />
          <MoneyInput label="Allowance made" ariaLabel="Allowance for failure to repay" value={form.allowanceMade} onValueChange={(value) => set("allowanceMade", value)} />
        </div>
        <TextAreaField label="Written terms / other main conditions" value={form.loanTerms} onChange={(value) => set("loanTerms", value)} />
        {(["NotWritten", "WrittenAmbiguousInterest", "WrittenAmbiguousRepaymentAndInterest"] as const).includes(form.termsStatus as never) && (
          <TextField label="Section 236 rebuttal or accounting evidence reference" value={form.section236PresumptionEvidenceReference} onChange={(value) => set("section236PresumptionEvidenceReference", value)} placeholder="Required if recorded interest differs from the time-weighted 5% presumption" />
        )}
      </fieldset>

      <fieldset className={sectionClass}>
        <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">4. Sections 239–245 legal basis</legend>
        <SelectField
          label="Claimed legal basis"
          value={form.complianceBasis}
          onChange={(value) => set("complianceBasis", value as DirectorLoanRow["complianceBasis"])}
          options={[
            ["Unassessed", "Not assessed — blocks final output"],
            ["Section240BelowTenPercent", "Section 240 — strictly below 10% of relevant assets"],
            ["Section242SummaryApprovalProcedure", "Section 242 — Summary Approval Procedure"],
            ["Section243IntraGroup", "Section 243 — intra-group transaction"],
            ["Section244VouchedExpense", "Section 244 — vouched director expense"],
            ["Section245OrdinaryBusiness", "Section 245 — ordinary-course business"],
          ]}
        />

        {form.complianceBasis === "Section240BelowTenPercent" && <Section240Fields form={form} set={set} />}
        {form.complianceBasis === "Section242SummaryApprovalProcedure" && <SapFields form={form} set={set} />}
        {form.complianceBasis === "Section243IntraGroup" && (
          <TextField label="Group-relationship evidence reference" value={form.exceptionEvidenceReference} onChange={(value) => set("exceptionEvidenceReference", value)} />
        )}
        {form.complianceBasis === "Section244VouchedExpense" && (
          <div className="grid gap-3 sm:grid-cols-3">
            <DateField label="Expense incurred" value={form.expenseIncurredDate} onChange={(value) => set("expenseIncurredDate", value)} />
            <DateField label="Liability discharged" value={form.expenseDischargedDate} onChange={(value) => set("expenseDischargedDate", value)} />
            <TextField label="Vouched expense evidence" value={form.exceptionEvidenceReference} onChange={(value) => set("exceptionEvidenceReference", value)} />
          </div>
        )}
        {form.complianceBasis === "Section245OrdinaryBusiness" && (
          <div className="space-y-3">
            <CheckboxField label="Transaction was entered in the ordinary course of the company's business" checked={form.ordinaryCourseConfirmed} onChange={(checked) => set("ordinaryCourseConfirmed", checked)} />
            <CheckboxField label="Value and terms were no more favourable than for an equivalent unconnected person" checked={form.noMoreFavourableTermsConfirmed} onChange={(checked) => set("noMoreFavourableTermsConfirmed", checked)} />
            <TextField label="Ordinary-course and comparator evidence" value={form.exceptionEvidenceReference} onChange={(value) => set("exceptionEvidenceReference", value)} />
          </div>
        )}
      </fieldset>

      <fieldset className={sectionClass}>
        <legend className="px-1 text-sm font-semibold text-gray-900 dark:text-gray-100">5. Arrangement-level professional review</legend>
        <p className="text-xs text-[var(--muted-foreground)]">This decision does not replace the separate release-level qualified-accountant approval.</p>
        <div className="grid gap-3 sm:grid-cols-2">
          <SelectField
            label="Review decision"
            value={form.reviewDecision}
            onChange={(value) => set("reviewDecision", value as DirectorLoanRow["reviewDecision"])}
            options={[["Unreviewed", "Unreviewed"], ["Accepted", "Accepted"], ["RemediationRequired", "Remediation required"]]}
          />
          {form.reviewedBy && (
            <div className="rounded-lg bg-gray-50 p-3 text-xs text-gray-600 dark:bg-neutral-900/60 dark:text-gray-300">
              Last stamped by {form.reviewedBy} ({form.reviewerRole ?? "role not retained"}){form.reviewedAtUtc ? ` at ${new Date(form.reviewedAtUtc).toLocaleString("en-IE")}` : ""}.
            </div>
          )}
        </div>
        <TextAreaField label="Review rationale / required remediation (minimum 20 characters for a decision)" value={form.reviewNote} onChange={(value) => set("reviewNote", value)} />
      </fieldset>

      <div className="flex flex-wrap justify-end gap-2">
        {onCancel && <Button variant="ghost" size="sm" onPress={onCancel} isDisabled={saving}><X className="mr-1 h-4 w-4" /> Cancel</Button>}
        <Button variant="primary" size="sm" onPress={onSubmit} isDisabled={saving}>
          {saving ? <Spinner size="sm" /> : editing ? "Save statutory evidence" : "Add statutory evidence"}
        </Button>
      </div>
      {destructiveActionConfirmation}
    </div>
  );
}

function Section240Fields({ form, set }: FormSectionProps) {
  return (
    <div className="space-y-3 rounded-lg bg-gray-50 p-3 dark:bg-neutral-900/60">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <SelectField label="Relevant-assets basis" value={form.relevantAssetsBasis} onChange={(value) => set("relevantAssetsBasis", value as DirectorLoanRow["relevantAssetsBasis"])} options={[
          ["Unassessed", "Not assessed"],
          ["LastLaidEntityFinancialStatements", "Last laid entity financial statements"],
          ["CalledUpShareCapitalNoPriorStatements", "Called-up share capital — no prior statements"],
        ]} />
        <MoneyInput label="Relevant assets" ariaLabel="Relevant assets amount" value={form.relevantAssetsAmount ?? 0} onValueChange={(value) => set("relevantAssetsAmount", value)} />
        <DateField label="Evidence as-of date" value={form.relevantAssetsAsOfDate} onChange={(value) => set("relevantAssetsAsOfDate", value)} />
        <TextField label="Relevant-assets evidence" value={form.relevantAssetsReference} onChange={(value) => set("relevantAssetsReference", value)} />
      </div>
      {form.relevantAssetsBasis === "CalledUpShareCapitalNoPriorStatements" && <CheckboxField label="Confirmed: no entity financial statements had previously been prepared and laid" checked={form.noPriorFinancialStatementsConfirmed} onChange={(checked) => set("noPriorFinancialStatementsConfirmed", checked)} />}
      <SelectField label="Section 241 later asset-fall review" value={form.relevantAssetsFallReview} onChange={(value) => set("relevantAssetsFallReview", value as DirectorLoanRow["relevantAssetsFallReview"])} options={[
        ["Unassessed", "Not assessed"],
        ["NoRelevantFall", "No relevant fall"],
        ["FallRemainedBelowLimit", "Assets fell; exposure remained strictly below limit"],
        ["TermsAmendedWithinTwoMonths", "Terms amended within two months"],
        ["SapArrangementNotCounted", "SAP arrangement — select section 242 instead"],
      ]} />
      {form.relevantAssetsFallReview === "TermsAmendedWithinTwoMonths" && (
        <div className="grid gap-3 sm:grid-cols-3">
          <DateField label="Awareness date" value={form.relevantAssetsReductionAwarenessDate} onChange={(value) => set("relevantAssetsReductionAwarenessDate", value)} />
          <DateField label="Terms amended date" value={form.termsAmendedDate} onChange={(value) => set("termsAmendedDate", value)} />
          <TextField label="Amendment evidence" value={form.termsAmendmentEvidenceReference} onChange={(value) => set("termsAmendmentEvidenceReference", value)} />
        </div>
      )}
    </div>
  );
}

function SapFields({ form, set }: FormSectionProps) {
  return (
    <div className="space-y-3 rounded-lg bg-gray-50 p-3 dark:bg-neutral-900/60">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <DateField label="Directors' declaration" value={form.sapDeclarationDate} onChange={(value) => set("sapDeclarationDate", value)} />
        <DateField label="Special resolution" value={form.sapResolutionDate} onChange={(value) => set("sapResolutionDate", value)} />
        <DateField label="Activity began" value={form.sapActivityStartDate} onChange={(value) => set("sapActivityStartDate", value)} />
        <DateField label="Declaration filed with CRO" value={form.sapCroFilingDate} onChange={(value) => set("sapCroFilingDate", value)} />
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <TextField label="Declaration evidence" value={form.sapDeclarationReference} onChange={(value) => set("sapDeclarationReference", value)} />
        <TextField label="Resolution evidence" value={form.sapResolutionReference} onChange={(value) => set("sapResolutionReference", value)} />
        <TextField label="CRO filing reference" value={form.sapCroFilingReference} onChange={(value) => set("sapCroFilingReference", value)} />
      </div>
      <CheckboxField label="Declaration covers every section 203 matter and the 12-month solvency opinion" checked={form.sapDeclarationCoversSection203Matters} onChange={(checked) => set("sapDeclarationCoversSection203Matters", checked)} />
    </div>
  );
}

type FormSectionProps = {
  form: DirectorLoanRow;
  set: <Key extends keyof DirectorLoanRow>(key: Key, value: DirectorLoanRow[Key]) => void;
};

function SelectField({ label, value, onChange, options, placeholder }: { label: string; value: string; onChange: (value: string) => void; options: Array<readonly [string, string]>; placeholder?: string }) {
  return <label className="block"><span className={labelClass}>{label}</span><select className={inputClass} value={value} onChange={(event) => onChange(event.target.value)}>{placeholder && <option value="">{placeholder}</option>}{options.map(([optionValue, optionLabel]) => <option key={optionValue} value={optionValue}>{optionLabel}</option>)}</select></label>;
}

function TextField({ label, value, onChange, placeholder }: { label: string; value?: string; onChange: (value?: string) => void; placeholder?: string }) {
  return <label className="block"><span className={labelClass}>{label}</span><input className={inputClass} value={value ?? ""} placeholder={placeholder} onChange={(event) => onChange(event.target.value || undefined)} /></label>;
}

function TextAreaField({ label, value, onChange }: { label: string; value?: string; onChange: (value?: string) => void }) {
  return <label className="block"><span className={labelClass}>{label}</span><textarea className={`${inputClass} min-h-20`} value={value ?? ""} onChange={(event) => onChange(event.target.value || undefined)} /></label>;
}

function DateField({ label, value, onChange }: { label: string; value?: string; onChange: (value?: string) => void }) {
  return <label className="block"><span className={labelClass}>{label}</span><input type="date" className={inputClass} value={value ?? ""} onChange={(event) => onChange(event.target.value || undefined)} /></label>;
}

function NumberField({ label, value, onChange }: { label: string; value: number; onChange: (value: number) => void }) {
  return <label className="block"><span className={labelClass}>{label}</span><input type="number" min="0" step="0.01" className={inputClass} value={value || ""} onChange={(event) => onChange(Number(event.target.value))} /></label>;
}

function CheckboxField({ label, checked, onChange }: { label: string; checked: boolean; onChange: (checked: boolean) => void }) {
  return <label className="flex items-start gap-2 text-sm text-gray-700 dark:text-gray-300"><input type="checkbox" className="workbench-checkbox mt-0.5" checked={checked} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span></label>;
}

function DerivedAmount({ label, amount }: { label: string; amount: number }) {
  return <div className="rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 dark:border-neutral-700 dark:bg-neutral-900/60"><p className="text-xs text-[var(--muted-foreground)]">{label}</p><p className="text-sm font-semibold text-gray-900 dark:text-gray-100">{new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR" }).format(amount)}</p></div>;
}
