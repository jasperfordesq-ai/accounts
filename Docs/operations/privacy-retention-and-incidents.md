# Privacy retention, subject rights, and incident handling

Status: engineering control contract. This document does not replace controller-specific legal
advice, a record of processing activities, or the privacy notice approved for a deployment.

## Governing principles

The platform applies purpose limitation, data minimisation, storage limitation, integrity, and
accountability to personal data. The Irish Data Protection Commission says that identifiable data
must not be retained longer than necessary, that controllers should set erasure or review periods,
and that statutory obligations may justify a longer period. Companies Act 2014 section 285 and
Revenue guidance require applicable accounting and tax records to be retained for at least six
years. An unresolved Revenue inquiry, appeal, legal hold, or professional claim may require a
longer, explicitly reviewed hold.

Primary sources:

- DPC storage-limitation guidance: <https://www.dataprotection.ie/en/faqs/responsibilities-data-controllers/how-long-should-personal-data-be-held-meet-obligations-imposed-gdpr>
- DPC data-protection principles: <https://www.dataprotection.ie/en/organisations/data-protection-basics/principles-data-protection>
- Companies Act 2014, section 285: <https://www.irishstatutebook.ie/eli/2014/act/38/section/285/enacted/>
- Revenue record-keeping guidance: <https://www.revenue.ie/en/starting-a-business/starting-a-business/keeping-records.aspx>

## Enforced schedule

Every production deployment must explicitly retain these defaults or replace them with a named,
approved schedule. Configuration may shorten a non-statutory period but must not silently shorten
the statutory floor.

| Record | Purpose | Default trigger and period | End action |
| --- | --- | --- | --- |
| Login-attempt security event | Lockout investigation and abuse detection | 30 days from attempt | Hard delete. The event must contain only a keyed identifier fingerprint, stable internal user ID when known, fixed outcome/reason codes, and a safe correlation ID. Never retain the submitted email, password, IP address, user agent, form value, or response body. |
| Consumed, revoked, or expired invite/reset token and MFA challenge | One-time identity workflow and abuse investigation | 24 hours after terminal state or expiry | Hard delete the keyed token hash and attempt state. |
| Used MFA recovery-code hash | Prove one-time use and detect replay | 30 days after use | Hard delete. Unused hashes remain only while the credential is active. |
| User lifecycle event | Prove access, role, session-revocation, and offboarding decisions | 12 months after the account is offboarded, unless an event is bound to retained financial/audit evidence | Delete the non-statutory event or record a specific statutory/legal hold. Event details must never contain email, display name, credential, token, recovery code, IP address, or client payload. |
| Financial, filing, approval, and tamper-evident audit evidence | Companies Act/tax records and professional accountability | At least six years after the end of the financial year containing the latest related record; longer while an inquiry, appeal, legal hold, or professional claim is open | Preserve the evidence and its integrity chain. Do not rewrite a hash-chained row. After the hold expires, use an approved chain-preserving retention-epoch operation with a signed closing checkpoint; ad-hoc SQL deletion or mutation is forbidden. |
| Subject-rights request and decision | Accountability for access/erasure/restriction handling | Three years from closure | Delete request metadata after confirming no live complaint, inquiry, or legal hold. Export payloads are streamed to the requester and are not retained by the application; only the artifact SHA-256, byte count, located-record counts, decision, and timestamps remain. |
| Privacy/security incident record and exercise | Notification assessment, containment, evidence, lessons learned | Six years from closure, or longer under a recorded legal hold | Review then securely delete. Evidence must use synthetic identifiers in exercises and must not duplicate affected client data. |

## Subject access and approved erasure

Only an Owner with recent MFA-backed authentication may approve an export or erasure decision.
The subject must belong to the same tenant. The export inventory must search, at minimum: the user
profile, company assignments, lifecycle history, short-lived login security events, audit actor
links, review/approval evidence, filing authority/outcome evidence, and open privacy decisions.
The generated JSON artifact is deterministically ordered and SHA-256 bound.

Approved erasure or anonymisation performs one transaction that:

1. revokes every session and one-time token;
2. removes company assignments and MFA material;
3. deactivates and offboards the account;
4. replaces the active login identifier and display name with non-reversible, request-scoped
   placeholders;
5. deletes expired/non-statutory login security data; and
6. inventories records that cannot lawfully be erased without destroying required financial,
   filing, approval, or audit evidence.

Every retained record class requires a decision naming the legal basis, related company/period,
retention-until date or open-ended hold reason, approving actor, UTC approval time, and subject
request. A partial or refused request must return the reason and complaint/judicial-remedy notice
for human delivery. The platform never interprets an erasure request as authority to destroy a
company's accounting records or break an audit hash chain.

## Privacy incident workflow

The production runbook treats suspected personal-data exposure as a separate decision track within
the general incident process:

1. **Notify and triage** — assign an incident ID, controller/DPO owner, UTC discovery time, affected
   environments, and severity; do not paste client data into chat, tickets, or monitoring tags.
2. **Contain** — revoke affected sessions/keys, isolate the route or workload, and preserve an exact
   candidate/image/database identity before remediation.
3. **Preserve evidence** — retain access, structured-log, audit-checkpoint, deployment, and backup
   references with SHA-256 hashes and a custody log. Export the minimum evidence needed.
4. **Assess notification** — record the named decision-maker, affected categories/count estimates,
   likely consequences, safeguards, DPC/data-subject notification decision, statutory deadline,
   and actual notification references. The software supplies fields; it does not make the legal
   notification decision.
5. **Recover and review** — prove tenant isolation, audit integrity, financial-figure integrity,
   credential rotation, monitoring recovery, and corrective actions; obtain named closure review.

A release exercise is valid only when a synthetic scenario proves notification routing,
containment, evidence preservation, recovery checks, and post-incident review with measured UTC
times. A repository fixture or CI simulation is engineering evidence only; it cannot be presented
as a real incident, provider acknowledgement, DPO decision, or regulator notification.
