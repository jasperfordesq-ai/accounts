"use client";

import { useEffect, useMemo, useState } from "react";
import { Button, Spinner } from "@heroui/react";
import {
  AlertTriangle,
  CalendarClock,
  Download,
  ExternalLink,
  FileCheck2,
  Plus,
  ReceiptText,
  Trash2,
} from "lucide-react";

import { HorizontalScrollRegion, StatusBadge } from "@/components/workbench";
import { ConfirmModal } from "@/components/ConfirmModal";
import {
  corporationTaxSupportWorksheetCsvUrl,
  corporationTaxSupportWorksheetJsonUrl,
  type CorporationTaxFilingSupportResponse,
  type CorporationTaxFilingSupportReviewInput,
  type CorporationTaxPaymentInput,
} from "@/lib/api";

interface CorporationTaxFilingSupportPanelProps {
  companyId: number;
  periodId: number;
  response: CorporationTaxFilingSupportResponse | null;
  canWrite?: boolean;
  savingReview?: boolean;
  savingPayment?: boolean;
  deletingPaymentId?: number | null;
  onSaveReview?: (input: CorporationTaxFilingSupportReviewInput) => void | Promise<void>;
  onRecordPayment?: (input: CorporationTaxPaymentInput) => void | Promise<void>;
  onDeletePayment?: (paymentId: number) => void | Promise<void>;
  onDirtyChange?: (dirty: boolean) => void;
}

const EMPTY_REVIEW: CorporationTaxFilingSupportReviewInput = {
  currentPeriodSection239IncomeTax: 0,
  hasInterestLimitationRule: false,
  usesNotionalGroupPaymentAllocation: false,
  hasDirtOrOtherWithholdingCredits: false,
  hasOtherPreliminaryTaxAdjustments: false,
  hasMandatoryElectronicFilingExemption: false,
  evidenceNote: "",
};

const EMPTY_PAYMENT: CorporationTaxPaymentInput = {
  paymentDate: "",
  amount: 0,
  kind: "PreliminarySecondOrSingle",
  evidenceReference: "",
};

const inputClass = "w-full rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 py-2 text-sm text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500";

export function CorporationTaxFilingSupportPanel(props: CorporationTaxFilingSupportPanelProps) {
  const responseRevision = [
    props.response?.review
      ? `review:${props.response.review.id}:${props.response.review.preparedAtUtc}`
      : "review:none",
    ...(props.response?.payments ?? []).map((payment) => `payment:${payment.id}:${payment.recordedAtUtc}`),
  ].join("|");

  return <CorporationTaxFilingSupportPanelState key={responseRevision} {...props} />;
}

function CorporationTaxFilingSupportPanelState({
  companyId,
  periodId,
  response,
  canWrite = false,
  savingReview = false,
  savingPayment = false,
  deletingPaymentId = null,
  onSaveReview,
  onRecordPayment,
  onDeletePayment,
  onDirtyChange,
}: CorporationTaxFilingSupportPanelProps) {
  const [reviewDraft, setReviewDraft] = useState<CorporationTaxFilingSupportReviewInput>(() => reviewInput(response));
  const [paymentDraft, setPaymentDraft] = useState<CorporationTaxPaymentInput>(() => ({ ...EMPTY_PAYMENT }));

  const dirty = useMemo(() => {
    const reviewDirty = JSON.stringify(reviewDraft) !== JSON.stringify(reviewInput(response));
    const paymentDirty = paymentDraft.paymentDate !== ""
      || paymentDraft.amount !== 0
      || paymentDraft.kind !== EMPTY_PAYMENT.kind
      || paymentDraft.evidenceReference !== ""
      || Boolean(paymentDraft.externalPaymentReference);
    return reviewDirty || paymentDirty;
  }, [paymentDraft, response, reviewDraft]);

  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  const fieldGroups = useMemo(() => groupFields(response?.worksheet.fields ?? []), [response?.worksheet.fields]);

  return (
    <section aria-labelledby="corporation-tax-filing-support-title" className="space-y-5">
      <div
        role="note"
        className="rounded-md border-2 border-amber-400 bg-amber-50 px-4 py-4 text-amber-950 dark:border-amber-700 dark:bg-amber-950/40 dark:text-amber-100"
      >
        <div className="flex items-start gap-3">
          <AlertTriangle aria-hidden="true" className="mt-0.5 h-5 w-5 shrink-0" />
          <div>
            <h3 id="corporation-tax-filing-support-title" className="font-semibold">
              Support worksheet only — not a CT1 return
            </h3>
            <p className="mt-1 text-sm leading-6">
              Nothing on this screen is submitted to Revenue. A named qualified accountant must complete the live ROS CT1,
              confirm every unsupported panel and approve the retained handoff evidence.
            </p>
          </div>
        </div>
      </div>

      {!response ? (
        <div role="status" className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4 text-sm text-[var(--muted-foreground)]">
          Filing-support worksheet evidence is unavailable. Retry the statements data before relying on any deadline or payment figure.
        </div>
      ) : (
        <>
          <SupportSummary response={response} />

          {(response.filingSupport.blockingReasons.length > 0 || response.filingSupport.warnings.length > 0) && (
            <div className="grid gap-3 lg:grid-cols-2">
              <IssueList
                title="Blocking review points"
                items={response.filingSupport.blockingReasons}
                empty="No machine-detected filing-support blockers. Qualified-accountant review is still mandatory."
                tone="bad"
              />
              <IssueList title="Calculation cautions" items={response.filingSupport.warnings} tone="warn" />
            </div>
          )}

          <DueSchedule response={response} />
          <PaymentLedger
            response={response}
            canDelete={canWrite && Boolean(onDeletePayment)}
            deletingPaymentId={deletingPaymentId}
            onDeletePayment={onDeletePayment}
          />

          <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3">
            <div>
              <p className="text-sm font-semibold text-[var(--foreground)]">Retained handoff exports</p>
              <p className="mt-1 text-xs text-[var(--muted-foreground)]">
                Both exports remain support-only working papers and cannot be uploaded as a CT1 return.
              </p>
            </div>
            <div className="flex flex-wrap gap-2">
              <a
                className="inline-flex min-h-9 items-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] hover:bg-[var(--surface-subtle)]"
                href={corporationTaxSupportWorksheetCsvUrl(companyId, periodId)}
              >
                <Download aria-hidden="true" className="h-4 w-4" /> Support worksheet CSV
              </a>
              <a
                className="inline-flex min-h-9 items-center gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface)] px-3 text-sm font-semibold text-[var(--foreground)] hover:bg-[var(--surface-subtle)]"
                href={corporationTaxSupportWorksheetJsonUrl(companyId, periodId)}
              >
                <Download aria-hidden="true" className="h-4 w-4" /> Evidence JSON
              </a>
            </div>
          </div>

          <WorksheetFields groups={fieldGroups} response={response} />

          {canWrite && onSaveReview && (
            <ReviewEditor
              draft={reviewDraft}
              priorFactsRequired={response.filingSupport.companyClass === "Unresolved"}
              saving={savingReview}
              onChange={setReviewDraft}
              onSave={() => void onSaveReview(reviewDraft)}
            />
          )}
          {canWrite && onRecordPayment && (
            <PaymentEditor
              draft={paymentDraft}
              saving={savingPayment}
              onChange={setPaymentDraft}
              onSave={() => void onRecordPayment(paymentDraft)}
            />
          )}

          <ManualHandoff response={response} />
        </>
      )}
    </section>
  );
}

function SupportSummary({ response }: { response: CorporationTaxFilingSupportResponse }) {
  const support = response.filingSupport;
  const safeHarbourTone = support.safeHarbourMet === true ? "good" : support.safeHarbourMet === false ? "bad" : "warn";
  const safeHarbourLabel = support.safeHarbourMet === true ? "Met" : support.safeHarbourMet === false ? "Missed" : "Not yet assessed";
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface)] p-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-sm font-semibold text-[var(--foreground)]">Preliminary Tax decision summary</p>
          <p className="mt-1 text-xs text-[var(--muted-foreground)]">{support.companyClassLabel}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <StatusBadge tone={safeHarbourTone}>Safe harbour: {safeHarbourLabel}</StatusBadge>
          <StatusBadge tone={support.filingSupportReady ? "good" : "bad"}>
            {support.filingSupportReady ? "Support checks clear" : `${support.blockingReasons.length} blocker${support.blockingReasons.length === 1 ? "" : "s"}`}
          </StatusBadge>
          <StatusBadge tone="warn">Accountant review required</StatusBadge>
        </div>
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-px overflow-hidden rounded-md border border-[var(--border)] bg-[var(--border)] lg:grid-cols-4">
        <Metric label="Safe-harbour amount" value={eur(support.preliminaryTaxSafeHarbourAmount)} />
        <Metric label="Preliminary Tax paid" value={eur(support.preliminaryTaxPaymentsRecorded)} />
        <Metric label="Indicative late interest" value={eur(support.estimatedLatePaymentInterest)} warning={support.estimatedLatePaymentInterest > 0} />
        <Metric label="Indicative filing surcharge" value={eur(support.lateFiling.estimatedSurcharge)} warning={support.lateFiling.isLate} />
      </dl>
    </div>
  );
}

function Metric({ label, value, warning = false }: { label: string; value: string; warning?: boolean }) {
  return (
    <div className="bg-[var(--surface-subtle)] px-3 py-3">
      <dt className="text-xs font-medium text-[var(--muted-foreground)]">{label}</dt>
      <dd className={`mt-1 text-sm font-semibold ${warning ? "text-red-700 dark:text-red-300" : "text-[var(--foreground)]"}`}>{value}</dd>
    </div>
  );
}

function IssueList({
  title,
  items,
  empty,
  tone,
}: {
  title: string;
  items: string[];
  empty?: string;
  tone: "bad" | "warn";
}) {
  const classes = tone === "bad"
    ? "border-red-200 bg-red-50/70 text-red-950 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100"
    : "border-amber-200 bg-amber-50/70 text-amber-950 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-100";
  return (
    <div className={`rounded-md border p-4 text-sm ${classes}`}>
      <p className="font-semibold">{title}</p>
      {items.length > 0 ? (
        <ul className="mt-2 list-disc space-y-1.5 pl-5 leading-5">
          {items.map((item) => <li key={item}>{item}</li>)}
        </ul>
      ) : (
        <p className="mt-2 text-xs leading-5">{empty}</p>
      )}
    </div>
  );
}

function DueSchedule({ response }: { response: CorporationTaxFilingSupportResponse }) {
  const support = response.filingSupport;
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h4 className="text-sm font-semibold text-[var(--foreground)]">Due dates and cumulative payment cover</h4>
          <p className="mt-1 text-xs text-[var(--muted-foreground)]">
            Return and balance due {formatDate(support.returnAndBalanceDueDate)}. Electronic ROS dates use the earlier statutory calendar anchor or day 23.
          </p>
        </div>
        <StatusBadge tone={support.lateFiling.isLate ? "bad" : "info"}>
          {support.lateFiling.isLate ? `${support.lateFiling.daysLate} days late` : "No late-return exposure yet"}
        </StatusBadge>
      </div>
      <HorizontalScrollRegion label="Corporation Tax due schedule">
        <table className="workbench-table min-w-[820px] text-sm">
          <thead>
            <tr>
              <th scope="col">Obligation</th>
              <th scope="col">Due date</th>
              <th scope="col">Status</th>
              <th scope="col" className="text-right">Cumulative required</th>
              <th scope="col" className="text-right">Paid by due date</th>
              <th scope="col" className="text-right">Shortfall</th>
              <th scope="col">Basis</th>
            </tr>
          </thead>
          <tbody>
            {support.dueItems.map((item) => {
              const dueReached = item.dueDate <= response.worksheet.generatedAsOf;
              const overdueShortfall = dueReached && item.shortfallAtDueDate > 0;
              return (
                <tr key={item.code}>
                  <th scope="row">{item.label}</th>
                  <td>{formatDate(item.dueDate)}</td>
                  <td><StatusBadge tone={overdueShortfall ? "bad" : item.shortfallAtDueDate > 0 ? "info" : "good"}>{overdueShortfall ? "Shortfall at due date" : item.shortfallAtDueDate > 0 ? "Upcoming balance" : "Covered"}</StatusBadge></td>
                  <td className="text-right tabular-nums">{eur(item.cumulativeTaxRequired)}</td>
                  <td className="text-right tabular-nums">{eur(item.paidByDueDate)}</td>
                  <td className={`text-right tabular-nums ${overdueShortfall ? "font-semibold text-red-700 dark:text-red-300" : ""}`}>{eur(item.shortfallAtDueDate)}</td>
                  <td className="max-w-sm text-xs text-[var(--muted-foreground)]">{item.basis}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </HorizontalScrollRegion>
    </div>
  );
}

function PaymentLedger({
  response,
  canDelete,
  deletingPaymentId,
  onDeletePayment,
}: {
  response: CorporationTaxFilingSupportResponse;
  canDelete: boolean;
  deletingPaymentId: number | null;
  onDeletePayment?: (paymentId: number) => void | Promise<void>;
}) {
  const [pendingDeleteId, setPendingDeleteId] = useState<number | null>(null);
  const pendingPayment = response.payments.find((payment) => payment.id === pendingDeleteId);
  return (
    <div className="space-y-3">
      <div>
        <h4 className="text-sm font-semibold text-[var(--foreground)]">Dated payment evidence</h4>
        <p className="mt-1 text-xs text-[var(--muted-foreground)]">Recorded confirmations only. This tracker never initiates or changes a ROS payment.</p>
      </div>
      {response.payments.length === 0 ? (
        <div className="rounded-md border border-dashed border-[var(--border)] p-4 text-sm text-[var(--muted-foreground)]">No retained Corporation Tax payment confirmations.</div>
      ) : (
        <HorizontalScrollRegion label="Corporation Tax payment evidence">
          <table className="workbench-table min-w-[760px] text-sm">
            <thead><tr><th scope="col">Payment date</th><th scope="col">Purpose</th><th scope="col" className="text-right">Amount</th><th scope="col">Evidence</th><th scope="col">Recorded by</th>{canDelete && <th scope="col">Correction</th>}</tr></thead>
            <tbody>
              {response.payments.map((payment) => (
                <tr key={payment.id}>
                  <th scope="row">{formatDate(payment.paymentDate)}</th>
                  <td>{paymentKindLabel(payment.kind)}</td>
                  <td className="text-right font-semibold tabular-nums">{eur(payment.amount)}</td>
                  <td className="max-w-sm"><span className="font-medium text-[var(--foreground)]">{payment.evidenceReference}</span>{payment.externalPaymentReference && <span className="mt-1 block text-xs text-[var(--muted-foreground)]">External ref: {payment.externalPaymentReference}</span>}</td>
                  <td className="text-xs text-[var(--muted-foreground)]">{payment.recordedBy}<span className="block">{formatDateTime(payment.recordedAtUtc)}</span></td>
                  {canDelete && (
                    <td>
                      <Button
                        size="sm"
                        variant="ghost"
                        aria-label={`Remove payment evidence dated ${payment.paymentDate}`}
                        isDisabled={deletingPaymentId !== null}
                        onPress={() => setPendingDeleteId(payment.id)}
                      >
                        {deletingPaymentId === payment.id ? <Spinner size="sm" /> : <Trash2 aria-hidden="true" className="h-4 w-4" />}
                        Remove incorrect row
                      </Button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </HorizontalScrollRegion>
      )}
      <ConfirmModal
        open={pendingPayment !== undefined}
        title="Remove incorrect payment evidence?"
        description="This removes only the selected tracker row. It does not change or reverse any ROS payment, and the correction remains audit logged."
        confirmLabel="Remove evidence row"
        variant="danger"
        dialogRole="alertdialog"
        loading={pendingDeleteId !== null && deletingPaymentId === pendingDeleteId}
        onCancel={() => setPendingDeleteId(null)}
        onConfirm={() => {
          if (pendingDeleteId === null) return;
          void Promise.resolve(onDeletePayment?.(pendingDeleteId)).finally(() => setPendingDeleteId(null));
        }}
      >
        {pendingPayment && (
          <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm text-[var(--foreground)]">
            <p className="font-semibold">{formatDate(pendingPayment.paymentDate)} — {eur(pendingPayment.amount)}</p>
            <p className="mt-1 text-xs text-[var(--muted-foreground)]">{pendingPayment.evidenceReference}</p>
          </div>
        )}
      </ConfirmModal>
    </div>
  );
}

function WorksheetFields({
  groups,
  response,
}: {
  groups: Array<[string, CorporationTaxFilingSupportResponse["worksheet"]["fields"]]>;
  response: CorporationTaxFilingSupportResponse;
}) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h4 className="text-sm font-semibold text-[var(--foreground)]">Scoped CT1 handoff fields</h4>
          <p className="mt-1 text-xs text-[var(--muted-foreground)]">Mapping {response.worksheet.mappingVersion}. Panel-only or orientation rows are not exact ROS field identifiers.</p>
        </div>
        <StatusBadge tone={response.worksheet.yearSpecificMappingAvailable ? "info" : "bad"}>
          {response.worksheet.yearSpecificMappingAvailable ? "Year-specific guide pinned" : "Latest-guide orientation only"}
        </StatusBadge>
      </div>
      <div className="space-y-2">
        {groups.map(([panelTitle, fields], index) => (
          <details key={panelTitle} open={index === 0 ? true : undefined} className="group rounded-md border border-[var(--border)] bg-[var(--surface)]">
            <summary className="flex min-h-11 cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold text-[var(--foreground)]">
              <span>{panelTitle}</span>
              <span className="text-xs font-normal text-[var(--muted-foreground)]">{fields.length} mapped {fields.length === 1 ? "row" : "rows"}</span>
            </summary>
            <div className="border-t border-[var(--border)]">
              <HorizontalScrollRegion label={`${panelTitle} support fields`} className="p-3">
                <table className="workbench-table min-w-[820px] text-sm">
                  <thead><tr><th scope="col">Published label</th><th scope="col">Mapping</th><th scope="col">Support value</th><th scope="col">Source</th><th scope="col">Review note</th></tr></thead>
                  <tbody>
                    {fields.map((field) => (
                      <tr key={field.code}>
                        <th scope="row"><span className="block">{field.publishedFieldLabel}</span>{field.publishedPanelNumber && <span className="mt-1 block text-xs font-normal text-[var(--muted-foreground)]">Panel {field.publishedPanelNumber}</span>}</th>
                        <td><StatusBadge tone={mappingTone(field.mappingStatus)}>{mappingLabel(field.mappingStatus)}</StatusBadge></td>
                        <td className="font-semibold tabular-nums">{field.valueType === "money" ? eur(field.numericValue ?? 0) : field.textValue}</td>
                        <td className="text-xs text-[var(--muted-foreground)]">{field.source}</td>
                        <td className="max-w-sm text-xs text-[var(--muted-foreground)]">{field.note}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </HorizontalScrollRegion>
            </div>
          </details>
        ))}
      </div>
      <div className="grid gap-2 md:grid-cols-2">
        {response.worksheet.reconciliations.map((item) => (
          <div key={item.code} className="flex items-start justify-between gap-3 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm">
            <div><p className="font-semibold text-[var(--foreground)]">{item.detail}</p><p className="mt-1 text-xs text-[var(--muted-foreground)]">Difference {eur(item.difference)}</p></div>
            <StatusBadge tone={item.reconciles ? "good" : "bad"}>{item.reconciles ? "Agrees" : "Mismatch"}</StatusBadge>
          </div>
        ))}
      </div>
    </div>
  );
}

function ReviewEditor({
  draft,
  priorFactsRequired,
  saving,
  onChange,
  onSave,
}: {
  draft: CorporationTaxFilingSupportReviewInput;
  priorFactsRequired: boolean;
  saving: boolean;
  onChange: (draft: CorporationTaxFilingSupportReviewInput) => void;
  onSave: () => void;
}) {
  const hasPriorFacts = Boolean(draft.priorPeriodStart)
    || Boolean(draft.priorPeriodEnd)
    || draft.priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 !== undefined
    || draft.priorPeriodSection239IncomeTax !== undefined
    || Boolean(draft.priorLiabilityEvidenceReference);
  const priorFactsComplete = (!priorFactsRequired && !hasPriorFacts) || (Boolean(draft.priorPeriodStart)
    && Boolean(draft.priorPeriodEnd)
    && draft.priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239 !== undefined
    && draft.priorPeriodSection239IncomeTax !== undefined
    && (draft.priorLiabilityEvidenceReference?.trim().length ?? 0) >= 20);
  const complete = draft.evidenceNote.trim().length >= 20 && priorFactsComplete;
  return (
    <details className="rounded-md border border-[var(--border)] bg-[var(--surface)]">
      <summary className="flex min-h-12 cursor-pointer list-none items-center gap-2 px-4 py-3 text-sm font-semibold text-[var(--foreground)]">
        <FileCheck2 aria-hidden="true" className="h-4 w-4" /> Maintain preliminary-tax basis review
      </summary>
      <div className="space-y-4 border-t border-[var(--border)] p-4">
        {priorFactsRequired && (
          <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-xs leading-5 text-amber-950 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-100">
            This is not marked as the company&apos;s first accounting period. Exact preceding-period dates, CT, section 239 amount (enter zero if none), and retained liability evidence are required.
          </div>
        )}
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Preceding accounting-period start"><input className={inputClass} type="date" value={draft.priorPeriodStart ?? ""} onChange={(event) => onChange({ ...draft, priorPeriodStart: event.target.value || undefined })} /></Field>
          <Field label="Preceding accounting-period end"><input className={inputClass} type="date" value={draft.priorPeriodEnd ?? ""} onChange={(event) => onChange({ ...draft, priorPeriodEnd: event.target.value || undefined })} /></Field>
        </div>
        <div className="grid gap-4 md:grid-cols-3">
          <MoneyField label="Prior-period CT (exclude surcharge and s.239)" value={draft.priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239} onChange={(value) => onChange({ ...draft, priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239: value })} />
          <MoneyField label="Prior-period section 239 Income Tax" value={draft.priorPeriodSection239IncomeTax} onChange={(value) => onChange({ ...draft, priorPeriodSection239IncomeTax: value })} />
          <MoneyField label="Current-period section 239 Income Tax" value={draft.currentPeriodSection239IncomeTax} onChange={(value) => onChange({ ...draft, currentPeriodSection239IncomeTax: value ?? 0 })} required />
        </div>
        <Field label="Prior signed CT1 / Revenue liability evidence reference">
          <input className={inputClass} value={draft.priorLiabilityEvidenceReference ?? ""} maxLength={1000} onChange={(event) => onChange({ ...draft, priorLiabilityEvidenceReference: event.target.value || undefined })} placeholder="Retained signed CT1, ROS computation or Revenue reference (minimum 20 characters)" />
        </Field>
        <div className="grid gap-2 md:grid-cols-2">
          <Flag label="Interest Limitation Rule applies" checked={draft.hasInterestLimitationRule} onChange={(value) => onChange({ ...draft, hasInterestLimitationRule: value })} />
          <Flag label="Notional group payment allocation used" checked={draft.usesNotionalGroupPaymentAllocation} onChange={(value) => onChange({ ...draft, usesNotionalGroupPaymentAllocation: value })} />
          <Flag label="DIRT or other withholding credits present" checked={draft.hasDirtOrOtherWithholdingCredits} onChange={(value) => onChange({ ...draft, hasDirtOrOtherWithholdingCredits: value })} />
          <Flag label="Other preliminary-tax adjustment or special rule" checked={draft.hasOtherPreliminaryTaxAdjustments} onChange={(value) => onChange({ ...draft, hasOtherPreliminaryTaxAdjustments: value })} />
          <Flag label="Mandatory electronic filing exemption applies" checked={draft.hasMandatoryElectronicFilingExemption} onChange={(value) => onChange({ ...draft, hasMandatoryElectronicFilingExemption: value })} />
        </div>
        <Field label="Preparation evidence note">
          <textarea className={`${inputClass} min-h-24 resize-y`} value={draft.evidenceNote} maxLength={2000} onChange={(event) => onChange({ ...draft, evidenceNote: event.target.value })} placeholder="Identify the source evidence, checks performed and any unresolved treatment (minimum 20 characters)." />
        </Field>
        <div className="flex justify-end"><Button size="sm" variant="primary" aria-label="Save preliminary-tax review" isDisabled={saving || !complete} onPress={onSave}>{saving ? <Spinner size="sm" /> : "Save preliminary-tax review"}</Button></div>
      </div>
    </details>
  );
}

function PaymentEditor({
  draft,
  saving,
  onChange,
  onSave,
}: {
  draft: CorporationTaxPaymentInput;
  saving: boolean;
  onChange: (draft: CorporationTaxPaymentInput) => void;
  onSave: () => void;
}) {
  const complete = Boolean(draft.paymentDate) && draft.amount > 0 && draft.evidenceReference.trim().length >= 20 && draft.kind !== "Other";
  return (
    <details className="rounded-md border border-[var(--border)] bg-[var(--surface)]">
      <summary className="flex min-h-12 cursor-pointer list-none items-center gap-2 px-4 py-3 text-sm font-semibold text-[var(--foreground)]"><ReceiptText aria-hidden="true" className="h-4 w-4" /> Record retained payment evidence</summary>
      <div className="space-y-4 border-t border-[var(--border)] p-4">
        <div className="rounded-md border border-sky-200 bg-sky-50 px-3 py-2 text-xs leading-5 text-sky-950 dark:border-sky-800 dark:bg-sky-950/30 dark:text-sky-100">Record a payment only after retaining the ROS/bank confirmation. This action does not make a payment.</div>
        <div className="grid gap-4 md:grid-cols-3">
          <Field label="Payment date"><input className={inputClass} type="date" value={draft.paymentDate} onChange={(event) => onChange({ ...draft, paymentDate: event.target.value })} /></Field>
          <MoneyField label="Amount" value={draft.amount || undefined} onChange={(value) => onChange({ ...draft, amount: value ?? 0 })} required />
          <Field label="Payment purpose"><select className={inputClass} value={draft.kind} onChange={(event) => onChange({ ...draft, kind: event.target.value as CorporationTaxPaymentInput["kind"] })}><option value="PreliminaryFirst">First preliminary instalment (PTL1)</option><option value="PreliminarySecondOrSingle">Second / single preliminary instalment (PTL2)</option><option value="Balance">Return balance</option><option value="InterestOrSurcharge">Interest or surcharge</option><option value="Other">Other / unresolved — blocked</option></select></Field>
        </div>
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Retained payment evidence reference"><input className={inputClass} value={draft.evidenceReference} maxLength={1000} onChange={(event) => onChange({ ...draft, evidenceReference: event.target.value })} placeholder="ROS receipt / bank confirmation reference (minimum 20 characters)" /></Field>
          <Field label="External payment reference (optional)"><input className={inputClass} value={draft.externalPaymentReference ?? ""} maxLength={200} onChange={(event) => onChange({ ...draft, externalPaymentReference: event.target.value || undefined })} placeholder="ROS payment or bank reference" /></Field>
        </div>
        <div className="flex justify-end"><Button size="sm" variant="primary" aria-label="Record payment evidence" isDisabled={saving || !complete} onPress={onSave}>{saving ? <Spinner size="sm" /> : <><Plus aria-hidden="true" className="h-4 w-4" /> Record payment evidence</>}</Button></div>
      </div>
    </details>
  );
}

function ManualHandoff({ response }: { response: CorporationTaxFilingSupportResponse }) {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
        <p className="flex items-center gap-2 text-sm font-semibold text-[var(--foreground)]"><CalendarClock aria-hidden="true" className="h-4 w-4" /> Mandatory manual completion</p>
        <ol className="mt-3 list-decimal space-y-2 pl-5 text-xs leading-5 text-[var(--muted-foreground)]">{response.worksheet.manualCompletionItems.map((item) => <li key={item}>{item}</li>)}</ol>
      </div>
      <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
        <p className="text-sm font-semibold text-[var(--foreground)]">Official source trail</p>
        <ul className="mt-3 space-y-2 text-xs">{response.worksheet.sources.map((source) => <li key={source.code}><a className="inline-flex items-start gap-1 font-medium text-sky-700 underline-offset-2 hover:underline dark:text-sky-300" href={source.url} target="_blank" rel="noreferrer">{source.title}<ExternalLink aria-hidden="true" className="mt-0.5 h-3 w-3 shrink-0" /></a></li>)}</ul>
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block text-sm font-medium text-[var(--foreground)]"><span className="mb-1.5 block">{label}</span>{children}</label>;
}

function MoneyField({ label, value, required = false, onChange }: { label: string; value?: number; required?: boolean; onChange: (value?: number) => void }) {
  return <Field label={`${label}${required ? " *" : ""}`}><input className={inputClass} type="number" min="0" step="0.01" value={value ?? ""} onChange={(event) => onChange(event.target.value === "" ? undefined : Number(event.target.value))} /></Field>;
}

function Flag({ label, checked, onChange }: { label: string; checked: boolean; onChange: (value: boolean) => void }) {
  return <label className="flex min-h-11 items-start gap-2 rounded-md border border-[var(--control-border)] bg-[var(--surface-subtle)] px-3 py-2 text-sm text-[var(--foreground)]"><input className="mt-0.5 h-4 w-4" type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} /><span>{label}</span></label>;
}

function groupFields(fields: CorporationTaxFilingSupportResponse["worksheet"]["fields"]) {
  const groups = new Map<string, typeof fields>();
  for (const field of fields) {
    const existing = groups.get(field.panelTitle) ?? [];
    groups.set(field.panelTitle, [...existing, field]);
  }
  return [...groups.entries()];
}

function reviewInput(response: CorporationTaxFilingSupportResponse | null): CorporationTaxFilingSupportReviewInput {
  if (!response?.review) return EMPTY_REVIEW;
  return {
    priorPeriodStart: response.review.priorPeriodStart,
    priorPeriodEnd: response.review.priorPeriodEnd,
    priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239:
      response.review.priorPeriodCorporationTaxLiabilityExcludingSurchargeAndS239,
    priorPeriodSection239IncomeTax: response.review.priorPeriodSection239IncomeTax,
    currentPeriodSection239IncomeTax: response.review.currentPeriodSection239IncomeTax,
    priorLiabilityEvidenceReference: response.review.priorLiabilityEvidenceReference,
    hasInterestLimitationRule: response.review.hasInterestLimitationRule,
    usesNotionalGroupPaymentAllocation: response.review.usesNotionalGroupPaymentAllocation,
    hasDirtOrOtherWithholdingCredits: response.review.hasDirtOrOtherWithholdingCredits,
    hasOtherPreliminaryTaxAdjustments: response.review.hasOtherPreliminaryTaxAdjustments,
    hasMandatoryElectronicFilingExemption: response.review.hasMandatoryElectronicFilingExemption,
    evidenceNote: response.review.evidenceNote,
  };
}

function mappingTone(status: CorporationTaxFilingSupportResponse["worksheet"]["fields"][number]["mappingStatus"]) {
  if (status === "published-exact-field-label") return "good" as const;
  if (status === "internal-support-only") return "default" as const;
  if (status.includes("orientation") || status === "published-panel-only") return "warn" as const;
  return "info" as const;
}

function mappingLabel(status: CorporationTaxFilingSupportResponse["worksheet"]["fields"][number]["mappingStatus"]) {
  return ({
    "published-panel": "Published panel",
    "published-exact-field-label": "Exact published label",
    "published-panel-only": "Panel only",
    "internal-support-only": "Internal support",
    "latest-published-panel-orientation": "Latest-guide orientation",
    "latest-published-field-orientation": "Latest-field orientation",
  } as const)[status];
}

function paymentKindLabel(kind: CorporationTaxFilingSupportResponse["payments"][number]["kind"]) {
  return ({
    PreliminaryFirst: "First preliminary instalment",
    PreliminarySecondOrSingle: "Second / single preliminary instalment",
    Balance: "Return balance",
    InterestOrSurcharge: "Interest or surcharge",
    Other: "Other / unresolved",
  } as const)[kind];
}

function eur(value: number) {
  return new Intl.NumberFormat("en-IE", { style: "currency", currency: "EUR", minimumFractionDigits: 2 }).format(value);
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-IE", { day: "2-digit", month: "short", year: "numeric", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat("en-IE", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}
