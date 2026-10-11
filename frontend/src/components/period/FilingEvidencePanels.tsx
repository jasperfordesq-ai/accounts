"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button, Spinner } from "@heroui/react";
import { ChevronDown } from "lucide-react";
import { toast } from "sonner";
import {
  getOfficers,
  recordAccountantReview,
  recordAuditorReport,
  recordCroSigningPack,
  type FilingReadinessProfile,
  type Officer,
} from "@/lib/api";
import { ReviewPanel, StatusBadge } from "@/components/workbench";
import { ACCOUNTANT_PROFESSIONAL_BODIES, auditReportOutstanding, hasNextAction, isRecentMfaRequired } from "@/lib/filingEvidence";
import {
  Field,
  RecentVerificationPrompt,
  errorMessage,
  inputClass,
  signedAtUtcForDate,
  todayIsoDate,
} from "@/components/period/FilingEvidenceShared";

interface EvidencePanelProps {
  companyId: number;
  periodId: number;
  filingReadinessProfile: FilingReadinessProfile | null;
  canRecord: boolean;
  onChanged?: () => void | Promise<void>;
}

const isActive = (officer: Officer, onDate: string) =>
  officer.id != null && (!officer.resignedDate || officer.resignedDate >= onDate);

function PdfField({ label, onFile }: { label: string; onFile: (file: File | null) => void }) {
  return (
    <Field label={label}>
      <input type="file" accept="application/pdf" className={inputClass} onChange={(event) => onFile(event.target.files?.[0] ?? null)} />
    </Field>
  );
}

function SubmitRow({ busy, label, children }: { busy: boolean; label: string; children?: ReactNode }) {
  return (
    <div className="flex items-center justify-end gap-3">
      {children}
      <Button type="submit" size="sm" variant="primary" aria-label={label} isDisabled={busy}>
        {busy ? <Spinner size="sm" /> : label}
      </Button>
    </div>
  );
}

export function CroSigningPackPanel({ companyId, periodId, filingReadinessProfile, canRecord, onChanged }: EvidencePanelProps) {
  const [officers, setOfficers] = useState<Officer[]>([]);
  const [directorId, setDirectorId] = useState("");
  const [secretaryId, setSecretaryId] = useState("");
  const [signedDate, setSignedDate] = useState(todayIsoDate());
  const [pack, setPack] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [needsVerification, setNeedsVerification] = useState(false);
  const requested = hasNextAction(filingReadinessProfile, "record-cro-signing-pack");

  useEffect(() => {
    if (!requested || !canRecord) return;
    let cancelled = false;
    getOfficers(companyId)
      .then((list) => { if (!cancelled) setOfficers(list); })
      .catch((error) => toast.error(errorMessage(error, "Failed to load the company officers")));
    return () => { cancelled = true; };
  }, [companyId, requested, canRecord]);

  const today = todayIsoDate();
  const directors = officers.filter((officer) => officer.role === "Director" && isActive(officer, today));
  const secretaries = officers.filter((officer) => (officer.role === "Secretary" || officer.role === "CompanySecretary") && isActive(officer, today));

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!directorId || !secretaryId || !signedDate || !pack) {
      toast.error("Choose the director and the secretary who signed, the signing date and the executed PDF");
      return;
    }
    setBusy(true);
    try {
      await recordCroSigningPack(companyId, periodId, {
        directorOfficerId: Number(directorId),
        secretaryOfficerId: Number(secretaryId),
        signedAtUtc: signedAtUtcForDate(signedDate),
        signedPack: pack,
      });
      toast.success("Executed CRO signing pack recorded");
      setNeedsVerification(false);
      setPack(null);
      await onChanged?.();
    } catch (error) {
      if (isRecentMfaRequired(error)) setNeedsVerification(true);
      else toast.error(errorMessage(error, "Failed to record the executed CRO signing pack"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ReviewPanel
      title="Executed CRO signing pack"
      description="The CRO accounts and signature page, printed, signed by a director and the secretary, and scanned back in."
      actions={<StatusBadge tone={requested ? "warn" : "default"}>{requested ? "Signed pack requested" : "Not requested yet"}</StatusBadge>}
    >
      {!requested ? (
        <p className="text-sm leading-6 text-[var(--muted-foreground)]">
          The executed signing pack is requested once the CRO accounts PDF and signature page have been generated. If you have already recorded it, nothing more is needed here.
        </p>
      ) : !canRecord ? (
        <p className="text-sm leading-6 text-[var(--muted-foreground)]">An Owner or Reviewer records the executed signing pack.</p>
      ) : (
        <div className="space-y-4">
          <form onSubmit={(event) => void submit(event)} className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Signing director">
                <select value={directorId} onChange={(event) => setDirectorId(event.target.value)} className={inputClass}>
                  <option value="">Select a director</option>
                  {directors.map((officer) => <option key={officer.id} value={officer.id}>{officer.name}</option>)}
                </select>
              </Field>
              <Field label="Signing secretary">
                <select value={secretaryId} onChange={(event) => setSecretaryId(event.target.value)} className={inputClass}>
                  <option value="">Select the secretary</option>
                  {secretaries.map((officer) => <option key={officer.id} value={officer.id}>{officer.name}</option>)}
                </select>
              </Field>
              <Field label="Date signed">
                <input type="date" value={signedDate} max={today} onChange={(event) => setSignedDate(event.target.value)} className={inputClass} />
              </Field>
              <PdfField label="Executed signing pack (PDF)" onFile={setPack} />
            </div>
            <SubmitRow busy={busy} label="Record executed signing pack" />
          </form>
          {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
        </div>
      )}
    </ReviewPanel>
  );
}

export function AuditorReportPanel({ companyId, periodId, filingReadinessProfile, canRecord, onChanged }: EvidencePanelProps) {
  const [firmName, setFirmName] = useState("");
  const [signerName, setSignerName] = useState("");
  const [professionalBody, setProfessionalBody] = useState("");
  const [membershipNumber, setMembershipNumber] = useState("");
  const [reportReference, setReportReference] = useState("");
  const [signedDate, setSignedDate] = useState(todayIsoDate());
  const [accepted, setAccepted] = useState(false);
  const [report, setReport] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [needsVerification, setNeedsVerification] = useState(false);

  if (!auditReportOutstanding(filingReadinessProfile)) return null;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (![firmName, signerName, professionalBody, membershipNumber, reportReference].every((value) => value.trim()) || !signedDate || !report) {
      toast.error("Complete every field and upload the signed auditor's report PDF");
      return;
    }
    if (!accepted) {
      toast.error("Confirm that the directors have accepted the auditor's report");
      return;
    }
    setBusy(true);
    try {
      await recordAuditorReport(companyId, periodId, {
        firmName: firmName.trim(),
        signerName: signerName.trim(),
        professionalBody: professionalBody.trim(),
        membershipNumber: membershipNumber.trim(),
        reportReference: reportReference.trim(),
        signedAtUtc: signedAtUtcForDate(signedDate),
        acceptedByDirectors: accepted,
        report,
      });
      toast.success("Signed auditor's report recorded");
      setNeedsVerification(false);
      setReport(null);
      await onChanged?.();
    } catch (error) {
      if (isRecentMfaRequired(error)) setNeedsVerification(true);
      else toast.error(errorMessage(error, "Failed to record the auditor's report"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ReviewPanel
      title="Statutory audit: signed auditor's report"
      description="A statutory audit applies to this company, so the auditor's signed report has to be on file before filing. The auditor is appointed by the company; this platform does not replace them."
      actions={<StatusBadge tone="warn">Report required</StatusBadge>}
    >
      {!canRecord ? (
        <p className="text-sm leading-6 text-[var(--muted-foreground)]">An Owner or Reviewer records the signed auditor&apos;s report.</p>
      ) : (
        <div className="space-y-4">
          <form onSubmit={(event) => void submit(event)} className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Audit firm">
                <input value={firmName} onChange={(event) => setFirmName(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Signing auditor">
                <input value={signerName} onChange={(event) => setSignerName(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Professional body">
                <input value={professionalBody} onChange={(event) => setProfessionalBody(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Membership number">
                <input value={membershipNumber} onChange={(event) => setMembershipNumber(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Report reference">
                <input value={reportReference} onChange={(event) => setReportReference(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Report signed on">
                <input type="date" value={signedDate} max={todayIsoDate()} onChange={(event) => setSignedDate(event.target.value)} className={inputClass} />
              </Field>
              <PdfField label="Signed auditor's report (PDF)" onFile={setReport} />
            </div>
            <label className="flex items-start gap-2 text-sm text-[var(--foreground)]">
              <input type="checkbox" checked={accepted} onChange={(event) => setAccepted(event.target.checked)} className="workbench-checkbox mt-1" />
              <span>The directors have accepted the auditor&apos;s report</span>
            </label>
            <p className="text-xs leading-5 text-[var(--muted-foreground)]">
              Recording a members&apos; audit notice on the classification page re-determines the filing regime, so the report can then be recorded here.
            </p>
            <SubmitRow busy={busy} label="Record auditor's report" />
          </form>
          {needsVerification && <RecentVerificationPrompt onVerified={() => setNeedsVerification(false)} />}
        </div>
      )}
    </ReviewPanel>
  );
}

export function OptionalAccountantReviewPanel({ companyId, periodId, filingReadinessProfile, canRecord, onChanged }: EvidencePanelProps) {
  const [reviewerName, setReviewerName] = useState("");
  const [professionalBody, setProfessionalBody] = useState("");
  const [membershipNumber, setMembershipNumber] = useState("");
  const [reviewDate, setReviewDate] = useState(todayIsoDate());
  const [letter, setLetter] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (![reviewerName, professionalBody, membershipNumber].every((value) => value.trim()) || !reviewDate) {
      toast.error("Enter the accountant's name, professional body, membership number and review date");
      return;
    }
    setBusy(true);
    try {
      await recordAccountantReview(companyId, periodId, {
        reviewerName: reviewerName.trim(),
        professionalBody: professionalBody.trim(),
        membershipNumber: membershipNumber.trim(),
        reviewDate,
        reviewLetter: letter,
      });
      toast.success("Accountant review recorded");
      setLetter(null);
      await onChanged?.();
    } catch (error) {
      toast.error(errorMessage(error, "Failed to record the accountant review"));
    } finally {
      setBusy(false);
    }
  }

  return (
    <details className="group rounded-md border border-[var(--border)] bg-[var(--surface)]">
      <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-4 py-3 text-sm font-semibold text-[var(--foreground)]">
        <span>Optional: reviewed by an accountant</span>
        <ChevronDown className="h-4 w-4 shrink-0 transition group-open:rotate-180" />
      </summary>
      <div className="space-y-4 border-t border-[var(--border)] p-4">
        <p className="text-sm leading-6 text-[var(--muted-foreground)]">
          Irish law does not require an accountant. Record a review here only if one was done.
        </p>
        {filingReadinessProfile && (
          <p className="text-xs leading-5 text-[var(--muted-foreground)]">Accountant review (optional): {filingReadinessProfile.accountantReviewState}</p>
        )}
        {canRecord && (
          <form onSubmit={(event) => void submit(event)} className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <Field label="Accountant's name">
                <input value={reviewerName} onChange={(event) => setReviewerName(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Accountant's professional body">
                <select value={professionalBody} onChange={(event) => setProfessionalBody(event.target.value)} className={inputClass}>
                  <option value="">Select a professional body</option>
                  {ACCOUNTANT_PROFESSIONAL_BODIES.map((body) => <option key={body} value={body}>{body}</option>)}
                </select>
              </Field>
              <Field label="Accountant's membership number">
                <input value={membershipNumber} onChange={(event) => setMembershipNumber(event.target.value)} className={inputClass} />
              </Field>
              <Field label="Review date">
                <input type="date" value={reviewDate} max={todayIsoDate()} onChange={(event) => setReviewDate(event.target.value)} className={inputClass} />
              </Field>
              <PdfField label="Review letter (PDF, optional)" onFile={setLetter} />
            </div>
            <SubmitRow busy={busy} label="Record accountant review" />
          </form>
        )}
      </div>
    </details>
  );
}
