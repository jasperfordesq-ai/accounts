"use client";

import { cloneElement, isValidElement, useMemo, useState, type FormEvent, type ReactElement, type ReactNode } from "react";
import {
  croHandoffSnapshotRequestSchema,
  externalFilingAuthorityRequestSchema,
  externalFilingAuthorityRevocationRequestSchema,
  externalFilingOutcomeRequestSchema,
  revenueHandoffSnapshotRequestSchema,
  type CroHandoffSnapshotRequest,
  type ExternalFilingAuthority,
  type ExternalFilingAuthorityRequest,
  type ExternalFilingHandoffWorkspace,
  type ExternalFilingOutcomeRequest,
  type ExternalFilingSnapshot,
  type ExternalFilingWorkflow,
  type RevenueHandoffSnapshotRequest,
} from "@/lib/externalFilingHandoff";
import { ReviewPanel, StatusBadge } from "@/components/workbench";

const inputClass = "mt-1 w-full rounded-md border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm text-[var(--foreground)] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-500/20";
const buttonClass = "inline-flex min-h-10 items-center justify-center rounded-md bg-emerald-700 px-4 py-2 text-sm font-semibold text-white hover:bg-emerald-800 disabled:cursor-not-allowed disabled:opacity-50";
const secondaryButtonClass = "inline-flex min-h-10 items-center justify-center rounded-md border border-[var(--border)] bg-[var(--surface)] px-4 py-2 text-sm font-semibold text-[var(--foreground)] hover:bg-[var(--surface-subtle)] disabled:cursor-not-allowed disabled:opacity-50";

type AddressDraft = {
  line1: string;
  line2: string;
  line3: string;
  line4: string;
  line5: string;
  line6: string;
  line7: string;
};

type OfficerDraft = {
  officerId: string;
  firstName: string;
  lastName: string;
  address: AddressDraft;
  identityType: string;
  identityEvidenceReference: string;
  identityEvidenceSha256: string;
  presenterNotificationEmail: string;
  otherDirectorshipsEvidenceReference: string;
  protectedIdentifierEntryConfirmed: boolean;
};

type ShareholderDraft = {
  memberReference: string;
  name: string;
  address: AddressDraft;
  shareClass: string;
  currency: string;
  openingHolding: string;
  closingHolding: string;
  holdingDisplay: string;
  evidenceReference: string;
};

type AllotmentDraft = {
  allotmentReference: string;
  allotmentDate: string;
  shareClass: string;
  currency: string;
  numberAllotted: string;
  nominalValuePerShare: string;
  consideration: string;
  allotteeMemberReference: string;
  evidenceReference: string;
};

function emptyAddress(line1 = ""): AddressDraft {
  return { line1, line2: "", line3: "", line4: "", line5: "", line6: "", line7: "" };
}

function addressPayload(address: AddressDraft) {
  return Object.fromEntries(
    Object.entries(address).map(([key, value]) => [key, value.trim() || null]),
  ) as Record<keyof AddressDraft, string | null>;
}

function splitName(name: string) {
  const parts = name.trim().split(/\s+/);
  return {
    firstName: parts.shift() ?? "",
    lastName: parts.join(" "),
  };
}

function initialOfficers(workspace: ExternalFilingHandoffWorkspace): OfficerDraft[] {
  const source = workspace.preparation.officers;
  return (source.length > 0 ? source : [{ officerId: 0, name: "", sourceAddress: null }]).map((officer) => ({
    officerId: officer.officerId > 0 ? String(officer.officerId) : "",
    ...splitName(officer.name),
    address: emptyAddress(officer.sourceAddress ?? ""),
    identityType: "PPSN/IPN/RBO protected CORE entry",
    identityEvidenceReference: "",
    identityEvidenceSha256: "",
    presenterNotificationEmail: "",
    otherDirectorshipsEvidenceReference: "",
    protectedIdentifierEntryConfirmed: false,
  }));
}

function emptyShareholder(currency: string): ShareholderDraft {
  return {
    memberReference: "",
    name: "",
    address: emptyAddress(),
    shareClass: "Ordinary",
    currency,
    openingHolding: "0",
    closingHolding: "0",
    holdingDisplay: "",
    evidenceReference: "",
  };
}

function emptyAllotment(currency: string): AllotmentDraft {
  return {
    allotmentReference: "",
    allotmentDate: "",
    shareClass: "Ordinary",
    currency,
    numberAllotted: "",
    nominalValuePerShare: "",
    consideration: "",
    allotteeMemberReference: "",
    evidenceReference: "",
  };
}

async function evidenceFromFile(file: File) {
  if (file.size === 0) throw new Error("The retained evidence file is empty.");
  if (file.size > 10_000_000) throw new Error("Evidence files are limited to 10 MB for this handoff ledger.");
  const bytes = new Uint8Array(await file.arrayBuffer());
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return {
    evidenceArtifact: btoa(binary),
    evidenceSha256: Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join(""),
  };
}

function localUtc(value: FormDataEntryValue | null) {
  if (!value) return null;
  const date = new Date(String(value));
  return Number.isNaN(date.valueOf()) ? null : date.toISOString();
}

function text(data: FormData, name: string) {
  return String(data.get(name) ?? "").trim();
}

function nullableText(data: FormData, name: string) {
  return text(data, name) || null;
}

function issueMessages(issues: ReadonlyArray<{ path: PropertyKey[]; message: string }>) {
  return issues.map((issue) => `${issue.path.length > 0 ? `${issue.path.join(".")}: ` : ""}${issue.message}`);
}

function FormErrors({ errors }: { errors: string[] }) {
  if (errors.length === 0) return null;
  return (
    <div role="alert" className="rounded-md border border-red-300 bg-red-50 p-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/30 dark:text-red-100">
      <p className="font-semibold">Resolve these items before retaining evidence:</p>
      <ul className="mt-2 list-disc space-y-1 pl-5">{errors.map((error) => <li key={error}>{error}</li>)}</ul>
    </div>
  );
}

function Field({ label, name, required = false, children, hint }: {
  label: string;
  name: string;
  required?: boolean;
  children?: ReactNode;
  hint?: string;
}) {
  const hintId = hint ? `${name}-hint` : undefined;
  const control = hintId && isValidElement(children)
    ? cloneElement(children as ReactElement<{ "aria-describedby"?: string }>, { "aria-describedby": hintId })
    : children;
  return (
    <div>
      <label htmlFor={name} className="block text-sm font-medium text-[var(--foreground)]">
        {label}{required && <span aria-hidden="true"> *</span>}
      </label>
      {control}
      {hint && <span id={hintId} className="mt-1 block text-xs font-normal text-[var(--muted-foreground)]">{hint}</span>}
    </div>
  );
}

function AddressFields({ idPrefix, value, onChange }: {
  idPrefix: string;
  value: AddressDraft;
  onChange: (next: AddressDraft) => void;
}) {
  return (
    <fieldset className="grid gap-3 rounded-md border border-[var(--border)] p-3 sm:grid-cols-2">
      <legend className="px-1 text-xs font-semibold uppercase text-[var(--muted-foreground)]">Address (all applicable lines)</legend>
      {(Object.keys(value) as Array<keyof AddressDraft>).map((key, index) => (
        <Field key={key} label={`Address line ${index + 1}`} name={`${idPrefix}-${key}`} required={index === 0}>
          <input
            id={`${idPrefix}-${key}`}
            className={inputClass}
            value={value[key]}
            required={index === 0}
            onChange={(event) => onChange({ ...value, [key]: event.target.value })}
          />
        </Field>
      ))}
    </fieldset>
  );
}

export function AuthorityEvidenceForm({
  workspace,
  busy,
  onSave,
  onRevoke,
  onCancel,
}: {
  workspace: ExternalFilingHandoffWorkspace;
  busy: boolean;
  onSave: (request: ExternalFilingAuthorityRequest) => Promise<void>;
  onRevoke: (authorityId: number, reason: string) => Promise<void>;
  onCancel: () => void;
}) {
  const [workflow, setWorkflow] = useState<ExternalFilingWorkflow>("CroB1");
  const [file, setFile] = useState<File | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const active = workspace.authorities.filter((item) => item.status === "Active");

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setErrors([]);
    try {
      if (!file) throw new Error("Select the exact retained authority evidence file.");
      const data = new FormData(event.currentTarget);
      const evidence = await evidenceFromFile(file);
      const candidate = {
        workflow,
        kind: text(data, "kind"),
        legalName: text(data, "legalName"),
        practiceName: nullableText(data, "practiceName"),
        maskedPresenterOrTain: nullableText(data, "maskedPresenterOrTain"),
        authorityScope: text(data, "authorityScope"),
        engagementReference: text(data, "engagementReference"),
        externalAuthorityReference: text(data, "externalAuthorityReference"),
        effectiveFromUtc: localUtc(data.get("effectiveFromUtc")),
        effectiveUntilUtc: localUtc(data.get("effectiveUntilUtc")),
        ...evidence,
        evidenceMediaType: file.type || "application/octet-stream",
        evidenceFileName: file.name,
      };
      const parsed = externalFilingAuthorityRequestSchema.safeParse(candidate);
      if (!parsed.success) {
        setErrors(issueMessages(parsed.error.issues));
        return;
      }
      await onSave(parsed.data);
    } catch (error) {
      setErrors([error instanceof Error ? error.message : "Authority evidence could not be retained."]);
    }
  }

  return (
    <ReviewPanel title="Presenter / ROS authority evidence" description="Append a reviewed engagement version. Evidence bytes and SHA-256 are retained; identifiers shown here must stay masked.">
      <div className="space-y-5">
        <form className="space-y-4" onSubmit={submit}>
          <FormErrors errors={errors} />
          <div className="grid gap-4 md:grid-cols-2">
            <Field label="Workflow" name="authority-workflow" required>
              <select id="authority-workflow" className={inputClass} value={workflow} onChange={(event) => setWorkflow(event.target.value as ExternalFilingWorkflow)}>
                <option value="CroB1">CRO B1 manual handoff</option>
                <option value="RevenueCt1Support">Revenue CT1 support handoff</option>
              </select>
            </Field>
            <Field label="Authority kind" name="kind" required>
              <select id="kind" name="kind" className={inputClass} key={workflow} defaultValue={workflow === "CroB1" ? "CroElectronicFilingAgent" : "RevenueRosAgent"}>
                {workflow === "CroB1" ? (
                  <><option value="CroElectronicFilingAgent">CRO electronic filing agent</option><option value="CroPresenter">CRO presenter</option></>
                ) : <option value="RevenueRosAgent">Revenue ROS agent</option>}
              </select>
            </Field>
            <Field label="Authorised legal name" name="legalName" required><input id="legalName" name="legalName" className={inputClass} required /></Field>
            <Field label="Practice name" name="practiceName"><input id="practiceName" name="practiceName" className={inputClass} /></Field>
            <Field label="Masked presenter / TAIN" name="maskedPresenterOrTain" hint="Example: TAIN-****42. Never enter an unmasked identifier."><input id="maskedPresenterOrTain" name="maskedPresenterOrTain" className={inputClass} /></Field>
            <Field label="Engagement reference" name="engagementReference" required hint="Opaque reference only; no PPSN, date of birth or email."><input id="engagementReference" name="engagementReference" className={inputClass} required /></Field>
            <Field label="External authority reference" name="externalAuthorityReference" required><input id="externalAuthorityReference" name="externalAuthorityReference" className={inputClass} required /></Field>
            <Field label="Effective from" name="effectiveFromUtc" required><input id="effectiveFromUtc" name="effectiveFromUtc" type="datetime-local" className={inputClass} required /></Field>
            <Field label="Effective until" name="effectiveUntilUtc"><input id="effectiveUntilUtc" name="effectiveUntilUtc" type="datetime-local" className={inputClass} /></Field>
            <div className="md:col-span-2">
              <Field label="Authority scope" name="authorityScope" required><textarea id="authorityScope" name="authorityScope" className={inputClass} rows={3} required /></Field>
            </div>
            <div className="md:col-span-2">
              <Field label="Exact authority evidence" name="authority-evidence" required hint="The API verifies these bytes against the browser-computed SHA-256.">
                <input id="authority-evidence" type="file" className={inputClass} required onChange={(event) => setFile(event.target.files?.[0] ?? null)} />
              </Field>
            </div>
          </div>
          <div className="flex flex-wrap gap-2"><button className={buttonClass} disabled={busy}>Retain authority version</button><button type="button" className={secondaryButtonClass} onClick={onCancel}>Cancel</button></div>
        </form>

        {active.length > 0 && (
          <div className="border-t border-[var(--border)] pt-4">
            <h4 className="font-semibold text-[var(--foreground)]">Revoke current authority</h4>
            <div className="mt-3 space-y-3">
              {active.map((authority) => <AuthorityRevocation key={authority.authorityId} authority={authority} busy={busy} onRevoke={onRevoke} />)}
            </div>
          </div>
        )}
      </div>
    </ReviewPanel>
  );
}

function AuthorityRevocation({ authority, busy, onRevoke }: { authority: ExternalFilingAuthority; busy: boolean; onRevoke: (id: number, reason: string) => Promise<void> }) {
  const [reason, setReason] = useState("");
  const [errors, setErrors] = useState<string[]>([]);
  async function revoke() {
    const parsed = externalFilingAuthorityRevocationRequestSchema.safeParse({ reason });
    if (!parsed.success) {
      setErrors(issueMessages(parsed.error.issues));
      return;
    }
    try {
      await onRevoke(authority.authorityId, parsed.data.reason);
    } catch (error) {
      setErrors([error instanceof Error ? error.message : "Authority could not be revoked."]);
    }
  }
  return (
    <div className="rounded-md border border-[var(--border)] p-3">
      <div className="flex flex-wrap items-center justify-between gap-2"><p className="font-medium">{authority.practiceName || authority.legalName} · {authority.workflow}</p><StatusBadge tone="good">Active v{workspaceAuthorityVersion(authority)}</StatusBadge></div>
      <FormErrors errors={errors} />
      <div className="mt-3 flex flex-col gap-2 sm:flex-row"><input aria-label={`Revocation reason for ${authority.workflow}`} className={inputClass} placeholder="Reason (minimum 10 characters)" value={reason} onChange={(event) => setReason(event.target.value)} /><button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void revoke()}>Append revocation</button></div>
    </div>
  );
}

function workspaceAuthorityVersion(authority: ExternalFilingAuthority) {
  return authority.status === "Active" ? "current" : authority.status.toLowerCase();
}

export function CroSnapshotForm({ workspace, predecessor, busy, onSave, onCancel }: {
  workspace: ExternalFilingHandoffWorkspace;
  predecessor?: ExternalFilingSnapshot;
  busy: boolean;
  onSave: (request: CroHandoffSnapshotRequest) => Promise<void>;
  onCancel: () => void;
}) {
  const currency = workspace.preparation.reportingCurrency;
  const [officers, setOfficers] = useState<OfficerDraft[]>(() => initialOfficers(workspace));
  const [shareholders, setShareholders] = useState<ShareholderDraft[]>(() => [emptyShareholder(currency)]);
  const [allotments, setAllotments] = useState<AllotmentDraft[]>([]);
  const [errors, setErrors] = useState<string[]>([]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const candidate = {
      madeUpToDate: text(data, "madeUpToDate"),
      annualReturnDateElection: text(data, "annualReturnDateElection"),
      financialStatementsAnnexed: data.has("financialStatementsAnnexed"),
      auditExemptionClaimed: data.has("auditExemptionClaimed"),
      auditorReference: nullableText(data, "auditorReference"),
      reportingCurrency: text(data, "reportingCurrency").toUpperCase(),
      politicalDonationsOverThreshold: data.has("politicalDonationsOverThreshold"),
      politicalDonationsAmount: Number(text(data, "politicalDonationsAmount")),
      politicalDonationsEvidenceReference: text(data, "politicalDonationsEvidenceReference"),
      officers: officers.map((officer) => ({
        officerId: Number(officer.officerId),
        firstName: officer.firstName.trim(),
        lastName: officer.lastName.trim(),
        address: addressPayload(officer.address),
        identityType: officer.identityType.trim(),
        identityEvidenceReference: officer.identityEvidenceReference.trim(),
        identityEvidenceSha256: officer.identityEvidenceSha256.trim(),
        presenterNotificationEmail: officer.presenterNotificationEmail.trim() || null,
        otherDirectorshipsEvidenceReference: officer.otherDirectorshipsEvidenceReference.trim(),
        protectedIdentifierEntryConfirmed: officer.protectedIdentifierEntryConfirmed,
      })),
      shareholders: shareholders.map((shareholder) => ({
        ...shareholder,
        memberReference: shareholder.memberReference.trim(),
        name: shareholder.name.trim(),
        address: addressPayload(shareholder.address),
        currency: shareholder.currency.trim().toUpperCase(),
        openingHolding: Number(shareholder.openingHolding),
        closingHolding: Number(shareholder.closingHolding),
      })),
      allotments: allotments.map((allotment) => ({
        ...allotment,
        currency: allotment.currency.trim().toUpperCase(),
        numberAllotted: Number(allotment.numberAllotted),
        nominalValuePerShare: Number(allotment.nominalValuePerShare),
        consideration: Number(allotment.consideration),
      })),
      noAllotmentsInReturnPeriodConfirmed: data.has("noAllotmentsInReturnPeriodConfirmed"),
      shareholdersListPdfSha256: nullableText(data, "shareholdersListPdfSha256"),
      supersedesSnapshotId: predecessor?.document.snapshotId ?? null,
      amendmentReason: predecessor ? nullableText(data, "amendmentReason") : null,
    };
    const parsed = croHandoffSnapshotRequestSchema.safeParse(candidate);
    if (!parsed.success) {
      setErrors(issueMessages(parsed.error.issues));
      return;
    }
    try {
      setErrors([]);
      await onSave(parsed.data);
    } catch (error) {
      setErrors([error instanceof Error ? error.message : "The CRO snapshot could not be retained."]);
    }
  }

  return (
    <ReviewPanel title={predecessor ? `Linked CRO amendment after v${predecessor.document.version}` : "CRO B1 manual-handoff snapshot"} description="Review every row. Protected officer identifiers stay in CORE; only opaque evidence references and SHA-256 digests enter this ledger.">
      <form className="space-y-6" onSubmit={submit}>
        <FormErrors errors={errors} />
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <Field label="Made-up-to date" name="madeUpToDate" required><input id="madeUpToDate" name="madeUpToDate" type="date" className={inputClass} defaultValue={workspace.preparation.annualReturnDate ?? workspace.preparation.periodEnd} required /></Field>
          <Field label="ARD election" name="annualReturnDateElection" required><select id="annualReturnDateElection" name="annualReturnDateElection" className={inputClass} defaultValue="RetainExistingAnnualReturnDate"><option value="RetainExistingAnnualReturnDate">Retain existing ARD</option><option value="ElectNewAnnualReturnDate">Elect a new ARD in CORE</option></select></Field>
          <Field label="Reporting currency" name="reportingCurrency" required><input id="reportingCurrency" name="reportingCurrency" className={inputClass} defaultValue={currency} required /></Field>
          <Field label="Auditor / exemption evidence reference" name="auditorReference"><input id="auditorReference" name="auditorReference" className={inputClass} /></Field>
          <Field label="Political donation amount" name="politicalDonationsAmount" required><input id="politicalDonationsAmount" name="politicalDonationsAmount" type="number" min="0" step="0.01" className={inputClass} defaultValue="0" required /></Field>
          <Field label="Political-donation evidence reference" name="politicalDonationsEvidenceReference" required><input id="politicalDonationsEvidenceReference" name="politicalDonationsEvidenceReference" className={inputClass} required /></Field>
          <Field label="Optional shareholder-list PDF SHA-256" name="shareholdersListPdfSha256"><input id="shareholdersListPdfSha256" name="shareholdersListPdfSha256" className={inputClass} maxLength={64} /></Field>
          {predecessor && <Field label="Amendment reason" name="amendmentReason" required><textarea id="amendmentReason" name="amendmentReason" className={inputClass} rows={2} required /></Field>}
        </div>
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Check name="financialStatementsAnnexed" label="Financial statements annexed" defaultChecked />
          <Check name="auditExemptionClaimed" label="Audit exemption claimed" />
          <Check name="politicalDonationsOverThreshold" label="Political donations over threshold" />
          <Check name="noAllotmentsInReturnPeriodConfirmed" label="I confirm there were no allotments" />
        </div>

        <DynamicSection title="Officers and protected identity evidence" description="Officer IDs and names are prefilled from the company register; verify them and retain only protected-reference evidence." onAdd={() => setOfficers((current) => [...current, initialOfficers(workspace)[0]])}>
          {officers.map((officer, index) => (
            <OfficerEditor key={`${officer.officerId}-${index}`} index={index} officer={officer} canRemove={officers.length > 1} onChange={(next) => setOfficers((current) => current.map((item, itemIndex) => itemIndex === index ? next : item))} onRemove={() => setOfficers((current) => current.filter((_, itemIndex) => itemIndex !== index))} />
          ))}
        </DynamicSection>

        <DynamicSection title="Shareholders / members" description="Retain the field-by-field register-of-members facts required for the manual B1 handoff." onAdd={() => setShareholders((current) => [...current, emptyShareholder(currency)])}>
          {shareholders.map((shareholder, index) => (
            <ShareholderEditor key={index} index={index} shareholder={shareholder} canRemove={shareholders.length > 1} onChange={(next) => setShareholders((current) => current.map((item, itemIndex) => itemIndex === index ? next : item))} onRemove={() => setShareholders((current) => current.filter((_, itemIndex) => itemIndex !== index))} />
          ))}
        </DynamicSection>

        <DynamicSection title="Allotments in the return period" description="Add every allotment, or explicitly confirm above that none occurred." onAdd={() => setAllotments((current) => [...current, emptyAllotment(currency)])}>
          {allotments.length === 0 ? <p className="text-sm text-[var(--muted-foreground)]">No allotment rows entered.</p> : allotments.map((allotment, index) => (
            <AllotmentEditor key={index} index={index} allotment={allotment} onChange={(next) => setAllotments((current) => current.map((item, itemIndex) => itemIndex === index ? next : item))} onRemove={() => setAllotments((current) => current.filter((_, itemIndex) => itemIndex !== index))} />
          ))}
        </DynamicSection>

        <div className="flex flex-wrap gap-2"><button className={buttonClass} disabled={busy}>{predecessor ? "Retain linked CRO amendment" : "Retain CRO snapshot"}</button><button type="button" className={secondaryButtonClass} onClick={onCancel}>Cancel</button></div>
      </form>
    </ReviewPanel>
  );
}

function DynamicSection({ title, description, onAdd, children }: { title: string; description: string; onAdd: () => void; children: ReactNode }) {
  return (
    <fieldset className="space-y-3 rounded-lg border border-[var(--border)] p-4">
      <legend className="px-1 font-semibold text-[var(--foreground)]">{title}</legend>
      <div className="flex flex-wrap items-start justify-between gap-3"><p className="max-w-3xl text-sm text-[var(--muted-foreground)]">{description}</p><button type="button" className={secondaryButtonClass} onClick={onAdd}>Add row</button></div>
      {children}
    </fieldset>
  );
}

function OfficerEditor({ index, officer, canRemove, onChange, onRemove }: { index: number; officer: OfficerDraft; canRemove: boolean; onChange: (next: OfficerDraft) => void; onRemove: () => void }) {
  const prefix = `officer-${index}`;
  return (
    <div className="space-y-4 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
      <div className="flex justify-between"><h5 className="font-semibold">Officer {index + 1}</h5>{canRemove && <button type="button" className={secondaryButtonClass} onClick={onRemove}>Remove</button>}</div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        <Controlled label="Company officer ID" id={`${prefix}-id`} value={officer.officerId} required onChange={(value) => onChange({ ...officer, officerId: value })} type="number" />
        <Controlled label="First name" id={`${prefix}-first`} value={officer.firstName} required onChange={(value) => onChange({ ...officer, firstName: value })} />
        <Controlled label="Last name" id={`${prefix}-last`} value={officer.lastName} required onChange={(value) => onChange({ ...officer, lastName: value })} />
        <Controlled label="Identity evidence type" id={`${prefix}-identity-type`} value={officer.identityType} required onChange={(value) => onChange({ ...officer, identityType: value })} />
        <Controlled label="Opaque identity evidence reference" id={`${prefix}-identity-ref`} value={officer.identityEvidenceReference} required onChange={(value) => onChange({ ...officer, identityEvidenceReference: value })} />
        <Controlled label="Identity evidence SHA-256" id={`${prefix}-identity-sha`} value={officer.identityEvidenceSha256} required onChange={(value) => onChange({ ...officer, identityEvidenceSha256: value })} />
        <Controlled label="Presenter notification email" id={`${prefix}-email`} value={officer.presenterNotificationEmail} onChange={(value) => onChange({ ...officer, presenterNotificationEmail: value })} type="email" />
        <Controlled label="Other-directorship evidence reference" id={`${prefix}-directorships`} value={officer.otherDirectorshipsEvidenceReference} required onChange={(value) => onChange({ ...officer, otherDirectorshipsEvidenceReference: value })} />
      </div>
      <AddressFields idPrefix={`${prefix}-address`} value={officer.address} onChange={(address) => onChange({ ...officer, address })} />
      <label className="flex items-start gap-2 text-sm font-medium"><input type="checkbox" className="mt-1" checked={officer.protectedIdentifierEntryConfirmed} onChange={(event) => onChange({ ...officer, protectedIdentifierEntryConfirmed: event.target.checked })} /><span>I confirm the protected PPSN/IPN/RBO step was completed in CORE and no raw identifier was copied into this platform.</span></label>
    </div>
  );
}

function ShareholderEditor({ index, shareholder, canRemove, onChange, onRemove }: { index: number; shareholder: ShareholderDraft; canRemove: boolean; onChange: (next: ShareholderDraft) => void; onRemove: () => void }) {
  const prefix = `shareholder-${index}`;
  return (
    <div className="space-y-4 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
      <div className="flex justify-between"><h5 className="font-semibold">Member {index + 1}</h5>{canRemove && <button type="button" className={secondaryButtonClass} onClick={onRemove}>Remove</button>}</div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        <Controlled label="Opaque member reference" id={`${prefix}-ref`} value={shareholder.memberReference} required onChange={(value) => onChange({ ...shareholder, memberReference: value })} />
        <Controlled label="Member name" id={`${prefix}-name`} value={shareholder.name} required onChange={(value) => onChange({ ...shareholder, name: value })} />
        <Controlled label="Share class" id={`${prefix}-class`} value={shareholder.shareClass} required onChange={(value) => onChange({ ...shareholder, shareClass: value })} />
        <Controlled label="Currency" id={`${prefix}-currency`} value={shareholder.currency} required onChange={(value) => onChange({ ...shareholder, currency: value })} />
        <Controlled label="Opening holding" id={`${prefix}-opening`} value={shareholder.openingHolding} required onChange={(value) => onChange({ ...shareholder, openingHolding: value })} type="number" />
        <Controlled label="Closing holding" id={`${prefix}-closing`} value={shareholder.closingHolding} required onChange={(value) => onChange({ ...shareholder, closingHolding: value })} type="number" />
        <Controlled label="Holding display" id={`${prefix}-display`} value={shareholder.holdingDisplay} required onChange={(value) => onChange({ ...shareholder, holdingDisplay: value })} />
        <Controlled label="Register evidence reference" id={`${prefix}-evidence`} value={shareholder.evidenceReference} required onChange={(value) => onChange({ ...shareholder, evidenceReference: value })} />
      </div>
      <AddressFields idPrefix={`${prefix}-address`} value={shareholder.address} onChange={(address) => onChange({ ...shareholder, address })} />
    </div>
  );
}

function AllotmentEditor({ index, allotment, onChange, onRemove }: { index: number; allotment: AllotmentDraft; onChange: (next: AllotmentDraft) => void; onRemove: () => void }) {
  const prefix = `allotment-${index}`;
  const fields: Array<[string, keyof AllotmentDraft, string, string?]> = [
    ["Allotment reference", "allotmentReference", "text"], ["Allotment date", "allotmentDate", "date"], ["Share class", "shareClass", "text"],
    ["Currency", "currency", "text"], ["Number allotted", "numberAllotted", "number"], ["Nominal value per share", "nominalValuePerShare", "number"],
    ["Consideration", "consideration", "number"], ["Allottee member reference", "allotteeMemberReference", "text"], ["Evidence reference", "evidenceReference", "text"],
  ];
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-4">
      <div className="flex justify-between"><h5 className="font-semibold">Allotment {index + 1}</h5><button type="button" className={secondaryButtonClass} onClick={onRemove}>Remove</button></div>
      <div className="mt-3 grid gap-3 md:grid-cols-2 xl:grid-cols-3">{fields.map(([label, key, type]) => <Controlled key={key} label={label} id={`${prefix}-${key}`} value={allotment[key]} required type={type} onChange={(value) => onChange({ ...allotment, [key]: value })} />)}</div>
    </div>
  );
}

function Controlled({ label, id, value, onChange, required = false, type = "text" }: { label: string; id: string; value: string; onChange: (value: string) => void; required?: boolean; type?: string }) {
  return <Field label={label} name={id} required={required}><input id={id} className={inputClass} type={type} value={value} required={required} min={type === "number" ? "0" : undefined} step={type === "number" ? "any" : undefined} onChange={(event) => onChange(event.target.value)} /></Field>;
}

function Check({ name, label, defaultChecked = false }: { name: string; label: string; defaultChecked?: boolean }) {
  return <label className="flex items-start gap-2 rounded-md border border-[var(--border)] bg-[var(--surface-subtle)] p-3 text-sm font-medium"><input name={name} type="checkbox" className="mt-1" defaultChecked={defaultChecked} /><span>{label}</span></label>;
}

export function RevenueSnapshotForm({ workspace, predecessor, busy, onSave, onCancel }: {
  workspace: ExternalFilingHandoffWorkspace;
  predecessor?: ExternalFilingSnapshot;
  busy: boolean;
  onSave: (request: RevenueHandoffSnapshotRequest) => Promise<void>;
  onCancel: () => void;
}) {
  const [errors, setErrors] = useState<string[]>([]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const candidate = {
      asOfDate: nullableText(data, "asOfDate"),
      unsupportedSectionsReviewed: data.has("unsupportedSectionsReviewed"),
      manualCt1CompletionItems: text(data, "manualCt1CompletionItems").split(/\r?\n/).map((item) => item.trim()).filter(Boolean),
      supersedesSnapshotId: predecessor?.document.snapshotId ?? null,
      amendmentReason: predecessor ? nullableText(data, "amendmentReason") : null,
    };
    const parsed = revenueHandoffSnapshotRequestSchema.safeParse(candidate);
    if (!parsed.success) { setErrors(issueMessages(parsed.error.issues)); return; }
    try { setErrors([]); await onSave(parsed.data); } catch (error) { setErrors([error instanceof Error ? error.message : "The Revenue support snapshot could not be retained."]); }
  }
  return (
    <ReviewPanel title={predecessor ? `Linked Revenue amendment after v${predecessor.document.version}` : "Revenue CT1 support handoff snapshot"} description="This is bounded calculation and iXBRL support only. It never represents a complete CT1 and provides no ROS submission control.">
      <form className="space-y-4" onSubmit={submit}>
        <FormErrors errors={errors} />
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Support calculation as-of date" name="asOfDate"><input id="asOfDate" name="asOfDate" type="date" className={inputClass} defaultValue={workspace.preparation.periodEnd} /></Field>
          {predecessor && <Field label="Amendment reason" name="amendmentReason" required><textarea id="amendmentReason" name="amendmentReason" className={inputClass} rows={2} required /></Field>}
          <div className="md:col-span-2"><Field label="Outstanding manual CT1 completion items" name="manualCt1CompletionItems" hint="One live-ROS panel or follow-up per line. Leave empty only after the named reviewer has checked every unsupported section."><textarea id="manualCt1CompletionItems" name="manualCt1CompletionItems" className={inputClass} rows={6} /></Field></div>
        </div>
        <Check name="unsupportedSectionsReviewed" label="A qualified reviewer checked every unsupported CT1 section against the live ROS return." />
        <div className="flex flex-wrap gap-2"><button className={buttonClass} disabled={busy}>{predecessor ? "Retain linked Revenue amendment" : "Retain Revenue support snapshot"}</button><button type="button" className={secondaryButtonClass} onClick={onCancel}>Cancel</button></div>
      </form>
    </ReviewPanel>
  );
}

export function OutcomeEvidenceForm({ snapshot, workspace, busy, onSave, onCancel }: {
  snapshot: ExternalFilingSnapshot;
  workspace: ExternalFilingHandoffWorkspace;
  busy: boolean;
  onSave: (request: ExternalFilingOutcomeRequest) => Promise<void>;
  onCancel: () => void;
}) {
  const [outcome, setOutcome] = useState<ExternalFilingOutcomeRequest["outcome"]>("ReadyForManualHandoff");
  const [file, setFile] = useState<File | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const external = !["ReadyForManualHandoff", "SupersededByAmendment"].includes(outcome);
  const successors = useMemo(() => workspace.snapshots.filter((item) => item.document.workflow === snapshot.document.workflow && item.document.supersedesSnapshotId === snapshot.document.snapshotId), [snapshot, workspace.snapshots]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      const data = new FormData(event.currentTarget);
      const evidence = external
        ? file ? await evidenceFromFile(file) : (() => { throw new Error("Select the exact external acknowledgement or notice."); })()
        : { evidenceArtifact: null, evidenceSha256: null };
      const candidate = {
        outcome,
        externalReference: external ? nullableText(data, "externalReference") : null,
        externalOccurredAtUtc: external ? localUtc(data.get("externalOccurredAtUtc")) : null,
        reason: outcome === "CorrectionRequired" || outcome === "ExternallyRejected" ? nullableText(data, "reason") : null,
        correctionDeadlineUtc: outcome === "CorrectionRequired" ? localUtc(data.get("correctionDeadlineUtc")) : null,
        evidenceReference: external ? nullableText(data, "evidenceReference") : null,
        ...evidence,
        supersedingSnapshotId: outcome === "SupersededByAmendment" ? nullableText(data, "supersedingSnapshotId") : null,
      };
      const parsed = externalFilingOutcomeRequestSchema.safeParse(candidate);
      if (!parsed.success) { setErrors(issueMessages(parsed.error.issues)); return; }
      setErrors([]);
      await onSave(parsed.data);
    } catch (error) {
      setErrors([error instanceof Error ? error.message : "The chronology event could not be retained."]);
    }
  }

  return (
    <ReviewPanel title={`Append outcome for ${snapshot.document.workflow} v${snapshot.document.version}`} description={`Every event is bound to snapshot ${snapshot.artifactSha256.slice(0, 12)}…; external states require genuine retained evidence.`}>
      <form className="space-y-4" onSubmit={submit}>
        <FormErrors errors={errors} />
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="Outcome" name="outcome" required><select id="outcome" className={inputClass} value={outcome} onChange={(event) => { setOutcome(event.target.value as ExternalFilingOutcomeRequest["outcome"]); setFile(null); }}><option value="ReadyForManualHandoff">Ready for manual handoff (internal)</option><option value="ExternallySubmittedRecorded">External submission recorded</option><option value="CorrectionRequired">Correction / send-back required</option><option value="ExternallyRejected">Externally rejected</option><option value="ExternallyAcceptedRecorded">External acceptance recorded</option><option value="SupersededByAmendment">Superseded by linked amendment (internal)</option></select></Field>
          {external && <Field label="External reference" name="externalReference" required><input id="externalReference" name="externalReference" className={inputClass} required /></Field>}
          {external && <Field label="External event time" name="externalOccurredAtUtc" required><input id="externalOccurredAtUtc" name="externalOccurredAtUtc" type="datetime-local" className={inputClass} required /></Field>}
          {(outcome === "CorrectionRequired" || outcome === "ExternallyRejected") && <Field label="Reason" name="reason" required><textarea id="reason" name="reason" className={inputClass} rows={2} required /></Field>}
          {outcome === "CorrectionRequired" && <Field label="Correction deadline" name="correctionDeadlineUtc" required><input id="correctionDeadlineUtc" name="correctionDeadlineUtc" type="datetime-local" className={inputClass} required /></Field>}
          {external && <Field label="Opaque evidence reference" name="evidenceReference" required><input id="evidenceReference" name="evidenceReference" className={inputClass} required /></Field>}
          {external && <Field label="Exact external evidence file" name="outcome-evidence" required><input id="outcome-evidence" type="file" className={inputClass} required onChange={(event) => setFile(event.target.files?.[0] ?? null)} /></Field>}
          {outcome === "SupersededByAmendment" && <Field label="Linked successor snapshot" name="supersedingSnapshotId" required><select id="supersedingSnapshotId" name="supersedingSnapshotId" className={inputClass} required><option value="">Select exact successor…</option>{successors.map((item) => <option key={item.document.snapshotId} value={item.document.snapshotId}>v{item.document.version} · {item.artifactSha256.slice(0, 12)}…</option>)}</select></Field>}
        </div>
        {outcome === "ReadyForManualHandoff" && <p className="rounded-md border border-blue-300 bg-blue-50 p-3 text-sm text-blue-900 dark:border-blue-900 dark:bg-blue-950/30 dark:text-blue-100">This is an internal readiness event. It will not claim an external reference, submission, or acceptance.</p>}
        <div className="flex flex-wrap gap-2"><button className={buttonClass} disabled={busy}>Append chronology event</button><button type="button" className={secondaryButtonClass} onClick={onCancel}>Cancel</button></div>
      </form>
    </ReviewPanel>
  );
}
