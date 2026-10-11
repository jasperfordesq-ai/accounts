# Directors' s.324 Approval Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the board's Companies Act 2014 s.324 approval, signed by two directors (or the sole director), the approval gate for every supported company's CRO, Revenue and Charity filing, with accountant review reduced to an optional attestation and statutory audit still required where the company is not audit-exempt.

**Architecture:** Two new append-only, hash-chained tables (`directors_approval_pages`, `directors_approval_events`) in the ARD-evidence style, owned through the accounting period for RLS. A `DirectorsApprovalService` validates officers and dates, generates the s.324 page and records approvals; `FilingReleaseGate` binds every workflow's approval from the current directors' approval instead of qualified-accountant evidence; `FilingReadinessProfileService` reports `directors-approval` evidence and `approvalAuthority`. Public endpoints are added for the two existing trusted-evidence methods that had none (executed CRO signing pack, signed auditor's report) so the flow can complete on a live installation.

**Tech Stack:** ASP.NET Core 10 minimal API, EF Core 10 + Npgsql, PostgreSQL 16 RLS, QuestPDF, xUnit; Next.js 16 + HeroUI v3, zod, vitest, node:test.

**Spec:** `Docs/superpowers/specs/2026-10-11-directors-s324-approval-design.md`

## Global Constraints

- Work on branch `feat/directors-s324-approval`. `main` requires signed commits: local commits are unsigned and land through a PR that is merged by GitHub squash or replayed with GraphQL `createCommitOnBranch`. Do not push to `main`.
- Windows WDAC: build and test with `-p:ArtifactsPath=$env:TEMP/accts-art` (bash: `-p:ArtifactsPath="$TEMP/accts-art"`). Postgres tests need `ACCOUNTS_POSTGRES_TEST_CONNECTION="Host=localhost;Port=5433;Database=accounts;Username=accounts;Password=accounts_dev"` (Development Docker DB).
- Regenerate the OpenAPI document with `-p:OpenApiGenerateDocumentsOnBuild=true` and then `npm run generate:api-contract` in `frontend/`; never hand-edit `OpenApi/accounts-api-v1.json` or `src/lib/generated/accounts-api-v1.ts`.
- Leave unchanged: `external-ros-validation-reference`, `source-law-change-review-note`, the Revenue iXBRL generation policy, the CRO B1 certification page (director + secretary), and `auditRequired` semantics.
- No electronic-signature verification; uploads are retained and SHA-256-bound.
- All money/legal wording in UI copy uses "directors' approval (s.324)", "Statutory audit", "Accountant review (optional)". Never "accountant approval required".
- Every new evidence row is append-only: the DbContext rejects `Modified`/`Deleted` and recomputes the integrity hash on `Added`.
- Approving directors are chosen by `CompanyOfficer` id; names are always copied from officer rows, never accepted from the request body.

## Review Focus

- **The live company has a director who is also secretary (two officer rows for one person).** Selecting the same human twice through two different officer rows must be rejected: compare officer `Name` (trimmed, case-insensitive) as well as id. Test in Task 3.
- **An officer's appointment/resignation is edited after a page is generated.** Approval must re-check officer eligibility on the approval date at record time, not trust the page snapshot. Test in Task 3.
- **The accounts PDF is regenerated between "generate page" and "record approval".** Recording must fail with a clear "regenerate the approval page" message rather than binding a stale page. Test in Task 3.
- **Uploading the unsigned generated page (or the minute) as the signed page.** Rejected: signed page bytes must differ from the generated page, and the two uploads must differ from each other. Test in Task 3.
- **An s.334 / s.1218 member notice is recorded after approval.** The audit becomes required: readiness must again block on the auditor's report even though the directors' approval stays current. Test in Task 7.

---

## File Structure

| File | Responsibility |
|---|---|
| Create `backend/Accounts.Api/Entities/DirectorsApproval.cs` | `DirectorsApprovalPage`, `DirectorsApprovalEvent`, `DirectorsApprovalEventKind` |
| Create `backend/Accounts.Api/Services/DirectorsApprovalIntegrity.cs` | Canonical SHA-256 for pages and events |
| Create `backend/Accounts.Api/Services/DirectorsApprovalService.cs` | Officer eligibility, page generation, approval recording, current-approval lookup, revocation |
| Create `backend/Accounts.Api/Endpoints/DirectorsApprovalEndpoints.cs` | page, approval, CRO signing pack, auditor report, optional accountant review endpoints |
| Modify `backend/Accounts.Api/Data/AccountsDbContext.cs` | DbSets, mapping, append-only validation |
| Modify `backend/Accounts.Api/Data/TenantIsolationPolicyCatalog.cs` | RLS entries |
| Modify `backend/Accounts.Api/Data/PersistenceOwnershipInvariantValidator.cs` | period ownership checks |
| Modify `backend/Accounts.Api/Entities/CroFilingPackage.cs` | `AccountsPdfGeneratedAtUtc` |
| Create migration `backend/Accounts.Api/Data/Migrations/<ts>_AddDirectorsApproval.cs` | two tables, one column |
| Modify `backend/Accounts.Api/Services/DocumentGeneratorService.cs` | `GenerateDirectorsApprovalPageAsync` |
| Modify `backend/Accounts.Api/Services/FilingReleaseGate.cs` | bind/verify directors' approval; manifests; revoke on regeneration |
| Modify `backend/Accounts.Api/Services/FilingReadinessProfileService.cs` | evidence/blocker/packet/`ApprovalAuthority` |
| Modify `backend/Accounts.Api/Services/FilingWorkflowService.cs` | approval transitions call the new binding |
| Modify `backend/Accounts.Api/Services/IrishStatutoryRuleSources.cs` | s.324 and Charities Act watch sources |
| Modify `backend/Accounts.Api/Services/RoleAuthorizationService.cs`, `Middleware/RecentAuthenticationMiddleware.cs` | route permissions, re-authentication |
| Modify `backend/Accounts.Api/Services/ProductionReadinessReleaseCatalog.cs` | accountant sign-off non-blocking |
| Modify `backend/Accounts.Api/Services/AuditEventCodes.cs` | new audit codes |
| Create `backend/Accounts.Tests/DirectorsApprovalTests.cs` | service + integrity + gate tests (InMemory) |
| Create `backend/Accounts.Tests/DirectorsApprovalPostgresTests.cs` | RLS + migration |
| Modify existing tests listed in Task 9 | intentional semantic changes |
| Modify `frontend/src/lib/apiContracts.ts`, `frontend/src/lib/api.ts` | contracts + client calls |
| Create `frontend/src/components/period/DirectorsApprovalPanel.tsx` | s.324 two-step panel |
| Create `frontend/src/components/period/FilingEvidencePanels.tsx` | CRO signing pack, auditor report, optional accountant review |
| Modify `frontend/src/components/period/PeriodFilingWorkspace.tsx`, `FilingReviewCentre.tsx`, `PeriodWorkbenchOverview.tsx` | wiring and labels |
| Create `frontend/tests/render/directors-approval-panel.test.tsx` | render tests |

---

### Task 1: Statutory sources and audit codes

**Files:**
- Modify: `backend/Accounts.Api/Services/IrishStatutoryRuleSources.cs`
- Modify: `backend/Accounts.Api/Services/AuditEventCodes.cs`
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs` (create)

**Interfaces:**
- Produces: `IrishStatutoryRuleSources.CompaniesActSection324`, `IrishStatutoryRuleSources.CharitiesActSection50CompanyExclusion`; `AuditEventCodes.DirectorsApprovalPageGenerated`, `DirectorsApprovalRecorded`, `DirectorsApprovalRevoked`, `CroSigningPackRecorded`, `AuditorReportRecorded`, `AccountantReviewAttestationRecorded`.

- [ ] **Step 1: Write the failing test**

```csharp
// backend/Accounts.Tests/DirectorsApprovalTests.cs
using Accounts.Api.Services;
using Xunit;

namespace Accounts.Tests;

public sealed partial class DirectorsApprovalTests
{
    [Fact]
    public void StatutorySources_CiteSection324AndTheCharitiesActCompanyExclusion()
    {
        Assert.Equal("companies-act-2014-s324", IrishStatutoryRuleSources.CompaniesActSection324.Code);
        Assert.StartsWith("https://www.irishstatutebook.ie/", IrishStatutoryRuleSources.CompaniesActSection324.Url);
        Assert.Equal("charities-act-2009-s50-company-exclusion", IrishStatutoryRuleSources.CharitiesActSection50CompanyExclusion.Code);
        Assert.Equal("DirectorsApprovalRecorded", AuditEventCodes.DirectorsApprovalRecorded);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd backend && dotnet test Accounts.Tests/Accounts.Tests.csproj -c Release -p:ArtifactsPath="$TEMP/accts-art" --filter "FullyQualifiedName~DirectorsApprovalTests"`
Expected: build FAIL — `CompaniesActSection324` not defined.

- [ ] **Step 3: Implement**

Check the `LegalSourceReference` constructor shape at `IrishStatutoryRuleSources.cs:41` (code, title, reviewed date, url) and the property names it exposes (`Code`, `Url`); add next to `CroFinancialStatementsRequirements`:

```csharp
    public static readonly LegalSourceReference CompaniesActSection324 = new(
        "companies-act-2014-s324",
        "Companies Act 2014, section 324 — approval and signing of statutory financial statements by the board",
        new DateOnly(2026, 10, 11),
        "https://www.irishstatutebook.ie/eli/2014/act/38/section/324/enacted/en/html");

    // Watch item: s.50(13)(a) excludes company charities from the Charities Act audit/independent
    // examination requirement. The Charities (Amendment) Act 2024 deletes it but was not commenced
    // when reviewed; commencement must trigger the source-law change review.
    public static readonly LegalSourceReference CharitiesActSection50CompanyExclusion = new(
        "charities-act-2009-s50-company-exclusion",
        "Charities Act 2009, section 50(13)(a) — company charities excluded (2024 amendment not commenced)",
        new DateOnly(2026, 10, 11),
        "https://revisedacts.lawreform.ie/eli/2009/act/6/section/50/revised/en/html");
```

In `AuditEventCodes.cs` add beside `AnnualReturnDateRecorded`:

```csharp
    public const string DirectorsApprovalPageGenerated = "DirectorsApprovalPageGenerated";
    public const string DirectorsApprovalRecorded = "DirectorsApprovalRecorded";
    public const string DirectorsApprovalRevoked = "DirectorsApprovalRevoked";
    public const string CroSigningPackRecorded = "CroSigningPackRecorded";
    public const string AuditorReportRecorded = "AuditorReportRecorded";
    public const string AccountantReviewAttestationRecorded = "AccountantReviewAttestationRecorded";
```

If `DomainOperationAuditCoverageTests` enumerates `AuditEventCodes` and requires every code to be emitted somewhere, leave this test failing until Tasks 3, 5 and 8 emit them, and note it in the commit message.

- [ ] **Step 4: Run test to verify it passes** (same command) — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/Accounts.Api/Services/IrishStatutoryRuleSources.cs backend/Accounts.Api/Services/AuditEventCodes.cs backend/Accounts.Tests/DirectorsApprovalTests.cs
git commit -m "feat: s.324 statutory source and directors' approval audit codes"
```

---

### Task 2: Append-only entities, integrity hash, persistence and migration

**Files:**
- Create: `backend/Accounts.Api/Entities/DirectorsApproval.cs`
- Create: `backend/Accounts.Api/Services/DirectorsApprovalIntegrity.cs`
- Modify: `backend/Accounts.Api/Entities/CroFilingPackage.cs` (add one property)
- Modify: `backend/Accounts.Api/Entities/AccountingPeriod.cs` (navigation collections)
- Modify: `backend/Accounts.Api/Data/AccountsDbContext.cs`
- Modify: `backend/Accounts.Api/Data/TenantIsolationPolicyCatalog.cs`
- Modify: `backend/Accounts.Api/Data/PersistenceOwnershipInvariantValidator.cs`
- Create: migration via `dotnet ef`
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`

**Interfaces:**
- Produces:
  - `enum DirectorsApprovalEventKind { Approved, Revoked }`
  - `class DirectorsApprovalPage` (properties exactly as below)
  - `class DirectorsApprovalEvent` (properties exactly as below)
  - `static class DirectorsApprovalIntegrity { string ComputePageHash(DirectorsApprovalPage); string ComputeEventHash(DirectorsApprovalEvent); bool IsValid(DirectorsApprovalPage); bool IsValid(DirectorsApprovalEvent); }`
  - `AccountsDbContext.DirectorsApprovalPages`, `AccountsDbContext.DirectorsApprovalEvents`
  - `CroFilingPackage.AccountsPdfGeneratedAtUtc : DateTime?`

- [ ] **Step 1: Write the failing tests** (append to `DirectorsApprovalTests.cs`)

```csharp
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

public sealed partial class DirectorsApprovalTests
{
    internal static AccountsDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AccountsDbContext>().UseInMemoryDatabase($"s324-{Guid.NewGuid():N}").Options);

    internal static DirectorsApprovalPage SamplePage(int periodId, int firstId = 1, int? secondId = 2)
    {
        var page = new DirectorsApprovalPage
        {
            PeriodId = periodId,
            ApprovalDate = new DateOnly(2026, 11, 20),
            FirstDirectorOfficerId = firstId,
            SecondDirectorOfficerId = secondId,
            FirstDirectorName = "Director One",
            SecondDirectorName = secondId is null ? null : "Director Two",
            DirectorsInOfficeCount = secondId is null ? 1 : 3,
            ResolutionReference = "WR-2026-11-20-accounts",
            AccountsPdfSha256 = new string('a', 64),
            ArtifactSourceFingerprintSha256 = new string('b', 64),
            PageArtifact = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x01],
            PageSha256 = new string('c', 64),
            GeneratedByUserId = "user:1",
            GeneratedByDisplayName = "Owner",
            GeneratedAtUtc = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc),
            RecordSha256 = ""
        };
        page.RecordSha256 = DirectorsApprovalIntegrity.ComputePageHash(page);
        return page;
    }

    [Fact]
    public void PageHash_ChangesWhenAnyBoundFieldChanges()
    {
        var page = SamplePage(9);
        Assert.True(DirectorsApprovalIntegrity.IsValid(page));
        page.ResolutionReference = "WR-tampered";
        Assert.False(DirectorsApprovalIntegrity.IsValid(page));
    }

    [Fact]
    public async Task Pages_AreAppendOnly()
    {
        await using var db = CreateDb();
        var period = await SeedPeriodAsync(db);
        db.DirectorsApprovalPages.Add(SamplePage(period.Id, period.FirstDirectorId, period.SecondDirectorId));
        await db.SaveChangesAsync();

        var stored = await db.DirectorsApprovalPages.SingleAsync();
        stored.ResolutionReference = "changed";
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", error.Message);
    }

    [Fact]
    public async Task Pages_WithABadHash_AreRejectedOnInsert()
    {
        await using var db = CreateDb();
        var period = await SeedPeriodAsync(db);
        var page = SamplePage(period.Id, period.FirstDirectorId, period.SecondDirectorId);
        page.RecordSha256 = new string('0', 64);
        db.DirectorsApprovalPages.Add(page);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }
}
```

Also add the shared seed helper used by every later test (same file):

```csharp
    internal sealed record SeededPeriod(int CompanyId, int Id, int FirstDirectorId, int SecondDirectorId, int ThirdDirectorId, int SecretaryId);

    internal static async Task<SeededPeriod> SeedPeriodAsync(AccountsDbContext db, bool auditExempt = true, ElectedRegime regime = ElectedRegime.Micro)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Example Charity CLG",
            CroNumber = "600001",
            CompanyType = CompanyType.CompanyLimitedByGuarantee,
            IncorporationDate = new DateOnly(2017, 7, 20),
            AnnualReturnDate = new DateOnly(2026, 1, 20),
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Cork",
            RegisteredOfficeCounty = "Cork"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        var d1 = new CompanyOfficer { CompanyId = company.Id, Name = "Director One", Role = OfficerRole.Director, AppointedDate = new DateOnly(2017, 7, 20) };
        var d2 = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Director, AppointedDate = new DateOnly(2017, 7, 20) };
        var d3 = new CompanyOfficer { CompanyId = company.Id, Name = "Director Three", Role = OfficerRole.Director, AppointedDate = new DateOnly(2019, 1, 1) };
        var sec = new CompanyOfficer { CompanyId = company.Id, Name = "Director Two", Role = OfficerRole.Secretary, AppointedDate = new DateOnly(2017, 7, 20) };
        db.CompanyOfficers.AddRange(d1, d2, d3, sec);
        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 8, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = regime,
            CanUseMicro = regime == ElectedRegime.Micro,
            CanFileAbridged = true,
            AuditExempt = auditExempt
        });
        db.CroFilingPackages.Add(new CroFilingPackage
        {
            PeriodId = period.Id,
            AccountsPdfGenerated = true,
            AccountsPdfArtifact = [0x25, 0x50, 0x44, 0x46, 0x2d, 0x31],
            AccountsPdfSha256 = new string('a', 64),
            AccountsPdfGeneratedAtUtc = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
            ArtifactSourceFingerprintSha256 = new string('b', 64)
        });
        await db.SaveChangesAsync();
        return new SeededPeriod(company.Id, period.Id, d1.Id, d2.Id, d3.Id, sec.Id);
    }
```

Before relying on it, confirm `FilingRegime` property names (`ElectedRegime`, `CanUseMicro`, `CanFileAbridged`, `AuditExempt`) in `Entities/FilingRegime.cs` and adjust the helper if they differ.

- [ ] **Step 2: Run tests to verify they fail** — Expected: build FAIL (types not defined).

- [ ] **Step 3: Implement entities**

```csharp
// backend/Accounts.Api/Entities/DirectorsApproval.cs
using System.Text.Json.Serialization;

namespace Accounts.Api.Entities;

public enum DirectorsApprovalEventKind
{
    Approved,
    Revoked
}

/// <summary>
/// A generated Companies Act 2014 s.324 approval page. Immutable: correcting a detail generates a
/// new page. The page binds the exact accounts PDF it approves by SHA-256.
/// </summary>
public sealed class DirectorsApprovalPage
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DateOnly ApprovalDate { get; set; }
    public int FirstDirectorOfficerId { get; set; }
    public int? SecondDirectorOfficerId { get; set; }
    public required string FirstDirectorName { get; set; }
    public string? SecondDirectorName { get; set; }
    public int DirectorsInOfficeCount { get; set; }
    public required string ResolutionReference { get; set; }
    public required string AccountsPdfSha256 { get; set; }
    public required string ArtifactSourceFingerprintSha256 { get; set; }
    [JsonIgnore]
    public byte[] PageArtifact { get; set; } = [];
    public required string PageSha256 { get; set; }
    public required string GeneratedByUserId { get; set; }
    public required string GeneratedByDisplayName { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public required string RecordSha256 { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
}

/// <summary>
/// Append-only approval / revocation events. Each event chains the previous event's hash for the
/// period so a removed or reordered event is detectable.
/// </summary>
public sealed class DirectorsApprovalEvent
{
    public int Id { get; set; }
    public int PeriodId { get; set; }
    public DirectorsApprovalEventKind Kind { get; set; }
    public int ApprovalPageId { get; set; }
    public required string ApprovalPageSha256 { get; set; }
    [JsonIgnore]
    public byte[]? SignedPageArtifact { get; set; }
    public string? SignedPageSha256 { get; set; }
    [JsonIgnore]
    public byte[]? ResolutionArtifact { get; set; }
    public string? ResolutionSha256 { get; set; }
    public string? RevocationReason { get; set; }
    public required string RecordedByUserId { get; set; }
    public required string RecordedByDisplayName { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public string? PreviousRecordSha256 { get; set; }
    public required string RecordSha256 { get; set; }

    [JsonIgnore]
    public AccountingPeriod Period { get; set; } = null!;
    [JsonIgnore]
    public DirectorsApprovalPage ApprovalPage { get; set; } = null!;
}
```

In `CroFilingPackage.cs` after `AccountsPdfSha256` add:

```csharp
    /// <summary>Set only when the accounts PDF bytes change (GeneratedAt also moves for the signature page).</summary>
    public DateTime? AccountsPdfGeneratedAtUtc { get; set; }
```

In `AccountingPeriod.cs` navigation section add:

```csharp
    [JsonIgnore]
    public List<DirectorsApprovalPage> DirectorsApprovalPages { get; set; } = [];
    [JsonIgnore]
    public List<DirectorsApprovalEvent> DirectorsApprovalEvents { get; set; } = [];
```

- [ ] **Step 4: Implement integrity**

```csharp
// backend/Accounts.Api/Services/DirectorsApprovalIntegrity.cs
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Entities;

namespace Accounts.Api.Services;

public static class DirectorsApprovalIntegrity
{
    public static bool IsValid(DirectorsApprovalPage page) =>
        string.Equals(ComputePageHash(page), page.RecordSha256, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(DirectorsApprovalEvent approvalEvent) =>
        string.Equals(ComputeEventHash(approvalEvent), approvalEvent.RecordSha256, StringComparison.OrdinalIgnoreCase);

    public static string ComputePageHash(DirectorsApprovalPage page) => Hash(
        "directors-approval-page-v1",
        page.PeriodId.ToString(CultureInfo.InvariantCulture),
        page.ApprovalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        page.FirstDirectorOfficerId.ToString(CultureInfo.InvariantCulture),
        page.SecondDirectorOfficerId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        page.FirstDirectorName,
        page.SecondDirectorName ?? string.Empty,
        page.DirectorsInOfficeCount.ToString(CultureInfo.InvariantCulture),
        page.ResolutionReference,
        page.AccountsPdfSha256,
        page.ArtifactSourceFingerprintSha256,
        page.PageSha256,
        page.GeneratedByUserId,
        page.GeneratedByDisplayName,
        page.GeneratedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    public static string ComputeEventHash(DirectorsApprovalEvent e) => Hash(
        "directors-approval-event-v1",
        e.PeriodId.ToString(CultureInfo.InvariantCulture),
        e.Kind.ToString(),
        e.ApprovalPageId.ToString(CultureInfo.InvariantCulture),
        e.ApprovalPageSha256,
        e.SignedPageSha256 ?? string.Empty,
        e.ResolutionSha256 ?? string.Empty,
        e.RevocationReason ?? string.Empty,
        e.RecordedByUserId,
        e.RecordedByDisplayName,
        e.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        e.PreviousRecordSha256 ?? string.Empty);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static string Hash(params string[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));
}
```

Note the page hash binds `PeriodId` and officer ids, which are assigned before insert, so the hash is computed before `SaveChanges` (ids of the page itself are not part of the hash).

- [ ] **Step 5: Map in the DbContext**

In `AccountsDbContext.cs`: add DbSets beside `AnnualReturnDateRecords`:

```csharp
    public DbSet<DirectorsApprovalPage> DirectorsApprovalPages => Set<DirectorsApprovalPage>();
    public DbSet<DirectorsApprovalEvent> DirectorsApprovalEvents => Set<DirectorsApprovalEvent>();
```

In `OnModelCreating` after the `AnnualReturnDateRecord` block:

```csharp
        modelBuilder.Entity<DirectorsApprovalPage>(e =>
        {
            e.ToTable("directors_approval_pages", table =>
            {
                table.HasCheckConstraint(
                    "CK_directors_approval_pages_sha256",
                    "length(\"RecordSha256\") = 64 AND length(\"PageSha256\") = 64 AND length(\"AccountsPdfSha256\") = 64 AND length(\"ArtifactSourceFingerprintSha256\") = 64");
                table.HasCheckConstraint(
                    "CK_directors_approval_pages_directors",
                    "(\"SecondDirectorOfficerId\" IS NULL AND \"DirectorsInOfficeCount\" = 1) OR (\"SecondDirectorOfficerId\" IS NOT NULL AND \"SecondDirectorOfficerId\" <> \"FirstDirectorOfficerId\" AND \"DirectorsInOfficeCount\" >= 2)");
            });
            e.HasKey(page => page.Id);
            e.HasQueryFilter(page => CurrentTenantId == null || page.Period.Company.TenantId == CurrentTenantId);
            e.Property(page => page.FirstDirectorName).HasMaxLength(200).IsRequired();
            e.Property(page => page.SecondDirectorName).HasMaxLength(200);
            e.Property(page => page.ResolutionReference).HasMaxLength(200).IsRequired();
            e.Property(page => page.AccountsPdfSha256).HasMaxLength(64).IsRequired();
            e.Property(page => page.ArtifactSourceFingerprintSha256).HasMaxLength(64).IsRequired();
            e.Property(page => page.PageSha256).HasMaxLength(64).IsRequired();
            e.Property(page => page.GeneratedByUserId).HasMaxLength(320).IsRequired();
            e.Property(page => page.GeneratedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(page => page.RecordSha256).HasMaxLength(64).IsRequired();
            e.HasOne(page => page.Period).WithMany(period => period.DirectorsApprovalPages)
                .HasForeignKey(page => page.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CompanyOfficer>().WithMany().HasForeignKey(page => page.FirstDirectorOfficerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CompanyOfficer>().WithMany().HasForeignKey(page => page.SecondDirectorOfficerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(page => new { page.PeriodId, page.GeneratedAtUtc });
        });

        modelBuilder.Entity<DirectorsApprovalEvent>(e =>
        {
            e.ToTable("directors_approval_events", table =>
            {
                table.HasCheckConstraint(
                    "CK_directors_approval_events_kind",
                    "(\"Kind\" = 'Approved' AND \"SignedPageSha256\" IS NOT NULL AND \"ResolutionSha256\" IS NOT NULL AND \"RevocationReason\" IS NULL) OR (\"Kind\" = 'Revoked' AND \"SignedPageSha256\" IS NULL AND \"ResolutionSha256\" IS NULL AND \"RevocationReason\" IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_directors_approval_events_sha256",
                    "length(\"RecordSha256\") = 64 AND length(\"ApprovalPageSha256\") = 64 AND (\"SignedPageSha256\" IS NULL OR length(\"SignedPageSha256\") = 64) AND (\"ResolutionSha256\" IS NULL OR length(\"ResolutionSha256\") = 64)");
            });
            e.HasKey(item => item.Id);
            e.HasQueryFilter(item => CurrentTenantId == null || item.Period.Company.TenantId == CurrentTenantId);
            e.Property(item => item.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(item => item.ApprovalPageSha256).HasMaxLength(64).IsRequired();
            e.Property(item => item.SignedPageSha256).HasMaxLength(64);
            e.Property(item => item.ResolutionSha256).HasMaxLength(64);
            e.Property(item => item.RevocationReason).HasMaxLength(100);
            e.Property(item => item.RecordedByUserId).HasMaxLength(320).IsRequired();
            e.Property(item => item.RecordedByDisplayName).HasMaxLength(200).IsRequired();
            e.Property(item => item.PreviousRecordSha256).HasMaxLength(64);
            e.Property(item => item.RecordSha256).HasMaxLength(64).IsRequired();
            e.HasOne(item => item.Period).WithMany(period => period.DirectorsApprovalEvents)
                .HasForeignKey(item => item.PeriodId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(item => item.ApprovalPage).WithMany()
                .HasForeignKey(item => item.ApprovalPageId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(item => new { item.PeriodId, item.RecordedAtUtc });
            e.HasIndex(item => item.PreviousRecordSha256).IsUnique().HasFilter("\"PreviousRecordSha256\" IS NOT NULL");
        });
```

Add the validation method and call it from **both** `SaveChanges` overrides right after `ValidateAnnualReturnDateEvidenceChanges();`:

```csharp
    private void ValidateDirectorsApprovalChanges()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<DirectorsApprovalPage>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("Directors' approval pages are append-only and cannot be changed or deleted.");
            if (entry.State == EntityState.Added && !DirectorsApprovalIntegrity.IsValid(entry.Entity))
                throw new BusinessRuleException("A directors' approval page failed its SHA-256 integrity check.");
        }
        foreach (var entry in ChangeTracker.Entries<DirectorsApprovalEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("Directors' approval events are append-only and cannot be changed or deleted.");
            if (entry.State == EntityState.Added && !DirectorsApprovalIntegrity.IsValid(entry.Entity))
                throw new BusinessRuleException("A directors' approval event failed its SHA-256 integrity check.");
        }
    }
```

Note: when the parent `AccountingPeriod` is deleted, EF cascades deletes into these entries; `ValidateDirectorsApprovalChanges` then throws, which is the intended protection (a period with an approval cannot be hard-deleted).

In `TenantIsolationPolicyCatalog.Policies` add after `cro_filing_packages`:

```csharp
        new("directors_approval_pages", TenantOwnershipPath.Period),
        new("directors_approval_events", TenantOwnershipPath.Period),
```

In `PersistenceOwnershipInvariantValidator.cs` beside the `CroFilingPackage` loop (line ~112):

```csharp
        foreach (var page in Changed<DirectorsApprovalPage>(db))
            await ValidatePeriodOwnedAsync(page.PeriodId, page.Period, nameof(DirectorsApprovalPage), resolver, currentTenantId);
        foreach (var approvalEvent in Changed<DirectorsApprovalEvent>(db))
            await ValidatePeriodOwnedAsync(approvalEvent.PeriodId, approvalEvent.Period, nameof(DirectorsApprovalEvent), resolver, currentTenantId);
```

- [ ] **Step 6: Run tests** — Expected: the three new tests PASS.

- [ ] **Step 7: Create the migration**

Run (from `backend/Accounts.Api`): `dotnet ef migrations add AddDirectorsApproval --output-dir Data/Migrations`
If WDAC blocks the design-time build, build first with `-p:ArtifactsPath="$TEMP/accts-art"` and retry; if it is still blocked, write the migration by hand following `20260829180901_AddCharitableTaxExemption.cs` (CreateTable ×2 with the check constraints and indexes above, `AddColumn<DateTime>("AccountsPdfGeneratedAtUtc", "cro_filing_packages", nullable: true, type: "timestamp with time zone")`) and update `AccountsDbContextModelSnapshot.cs`. Append to `Up`:

```csharp
            migrationBuilder.Sql("""
                UPDATE cro_filing_packages
                SET "AccountsPdfGeneratedAtUtc" = "GeneratedAt"
                WHERE "AccountsPdfGenerated" = TRUE AND "AccountsPdfGeneratedAtUtc" IS NULL;
                """);
```

Run: `cd backend && dotnet test Accounts.Tests/Accounts.Tests.csproj -c Release -p:ArtifactsPath="$TEMP/accts-art" --filter "FullyQualifiedName~MigrationUpgrade|FullyQualifiedName~DatabaseTenantIsolationSql|FullyQualifiedName~PendingModel|FullyQualifiedName~DirectorsApprovalTests"` with `ACCOUNTS_POSTGRES_TEST_CONNECTION` set.
Expected: PASS (the catalog completeness test proves both tables are classified).

- [ ] **Step 8: Commit**

```bash
git add backend/Accounts.Api/Entities backend/Accounts.Api/Services/DirectorsApprovalIntegrity.cs backend/Accounts.Api/Data backend/Accounts.Tests/DirectorsApprovalTests.cs
git commit -m "feat: append-only directors' approval pages and events"
```

---

### Task 3: DirectorsApprovalService (eligibility, page generation, approval, current approval, revocation)

**Files:**
- Create: `backend/Accounts.Api/Services/DirectorsApprovalService.cs`
- Modify: `backend/Accounts.Api/Services/DocumentGeneratorService.cs` (add `GenerateDirectorsApprovalPageAsync`)
- Modify: `backend/Accounts.Api/Program.cs` (register scoped service)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`

**Interfaces:**
- Consumes: Task 2 types; `DocumentGeneratorService`; `AuditService`; `AuthenticatedUser` / `AuthenticatedIdentity`.
- Produces:
  - `sealed record DirectorsApprovalPageInput(DateOnly ApprovalDate, int FirstDirectorOfficerId, int? SecondDirectorOfficerId, string ResolutionReference)`
  - `sealed record DirectorsApprovalRecordInput(int ApprovalPageId, byte[] SignedPageArtifact, string SignedPageSha256, byte[] ResolutionArtifact, string ResolutionSha256)`
  - `sealed record CurrentDirectorsApproval(DirectorsApprovalPage Page, DirectorsApprovalEvent Approval)`
  - `interface IDirectorsApprovalPageRenderer { Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content); }` (implemented by `DocumentGeneratorService`; tests use `FakePageRenderer`)
  - `class DirectorsApprovalService(AccountsDbContext db, IDirectorsApprovalPageRenderer renderer, AuditService audit, TimeProvider? clock = null)` with:
    - `Task<IReadOnlyList<CompanyOfficer>> DirectorsInOfficeAsync(int companyId, DateOnly onDate)`
    - `Task<DirectorsApprovalPage> GeneratePageAsync(int companyId, int periodId, DirectorsApprovalPageInput input, AuthenticatedUser actor)`
    - `Task<DirectorsApprovalEvent> RecordApprovalAsync(int companyId, int periodId, DirectorsApprovalRecordInput input, AuthenticatedUser actor)`
    - `Task<CurrentDirectorsApproval?> GetCurrentApprovalAsync(int periodId)`
    - `static CurrentDirectorsApproval? ResolveCurrent(IReadOnlyList<DirectorsApprovalEvent> events, IReadOnlyList<DirectorsApprovalPage> pages, CroFilingPackage? cro)` (pure; used by readiness and the gate)
    - `void AppendRevocationIfCurrent(AccountingPeriod period, CroFilingPackage cro, string reason)` (adds a `Revoked` event to the change tracker without saving)
  - `sealed record DirectorsApprovalPageContent(DateOnly ApprovalDate, string FirstDirectorName, string? SecondDirectorName, string ResolutionReference, string AccountsPdfSha256)`

- [ ] **Step 1: Write the failing tests**

```csharp
using Accounts.Api.Services;
using Microsoft.Extensions.Time.Testing; // if unavailable, use the FixedTimeProvider pattern from DashboardDeadlineBatchTests

public sealed partial class DirectorsApprovalTests
{
    private static readonly AuthenticatedUser Owner = new(
        UserId: 1, TenantId: 1, TenantName: "Firm", Email: "owner@example.ie",
        DisplayName: "Firm Owner", Role: "Owner", AllowedCompanyIds: new HashSet<int>());

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    internal static DirectorsApprovalService Service(AccountsDbContext db) =>
        new(db, new FakePageRenderer(), new AuditService(db), new FixedClock(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero)));

    private static byte[] Pdf(string marker) => [.. "%PDF-1.7\n"u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(marker)];

    [Fact]
    public async Task ThreeDirectorsInOffice_RequireTwoDistinctDirectors()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, null, "WR-2026-11-20"), Owner));
        Assert.Contains("two directors must sign", error.Message);
    }

    [Fact]
    public async Task TheSamePersonThroughTwoOfficerRows_IsRejected()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        // SecretaryId is "Director Two" in another role; a secretary row is not a director anyway,
        // so add a second Director row for the same person to prove the name check.
        var duplicate = new CompanyOfficer { CompanyId = p.CompanyId, Name = " director two ", Role = OfficerRole.Director, AppointedDate = new DateOnly(2018, 1, 1) };
        db.CompanyOfficers.Add(duplicate);
        await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.SecondDirectorId, duplicate.Id, "WR-2026-11-20"), Owner));
        Assert.Contains("two different directors", error.Message);
    }

    [Fact]
    public async Task SecretaryOnlyOfficer_CannotSign()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecretaryId, "WR-2026-11-20"), Owner));
        Assert.Contains("was not a director in office on 2026-11-20", error.Message);
    }

    [Fact]
    public async Task SoleDirector_SignsAlone()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        foreach (var officer in db.CompanyOfficers.Where(o => o.Id != p.FirstDirectorId && o.Role == OfficerRole.Director))
            officer.ResignedDate = new DateOnly(2026, 1, 1);
        await db.SaveChangesAsync();

        var page = await Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, null, "Board minute 2026-11-20"), Owner);

        Assert.Equal(1, page.DirectorsInOfficeCount);
        Assert.Null(page.SecondDirectorOfficerId);
        Assert.Equal("Director One", page.FirstDirectorName);
    }

    [Theory]
    [InlineData("2026-07-30", "on or after the period end")]
    [InlineData("2026-11-22", "cannot be in the future")]
    [InlineData("2026-10-31", "before the accounts PDF it approves was generated")]
    public async Task ApprovalDate_Bounds(string date, string expected)
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => Service(db).GeneratePageAsync(
            p.CompanyId, p.Id, new(DateOnly.Parse(date), p.FirstDirectorId, p.SecondDirectorId, "WR-2026"), Owner));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task RecordApproval_HappyPath_BecomesCurrentAndSetsApprovalDate()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR-2026-11-20"), Owner);
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var approval = await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        Assert.Equal(DirectorsApprovalEventKind.Approved, approval.Kind);
        var current = await service.GetCurrentApprovalAsync(p.Id);
        Assert.NotNull(current);
        Assert.Equal(new DateOnly(2026, 11, 20), (await db.AccountingPeriods.FindAsync(p.Id))!.ApprovalDate);
        Assert.Single(db.AuditLogs.Where(a => a.Action == AuditEventCodes.DirectorsApprovalRecorded));
    }

    [Fact]
    public async Task RecordApproval_RejectsUnsignedPageDigestMismatchNonPdfAndIdenticalUploads()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var minute = Pdf("minute");

        var unsigned = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, page.PageArtifact, page.PageSha256, minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("identical to the unsigned page", unsigned.Message);

        var signed = Pdf("signed");
        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, new string('0', 64), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("SHA-256", mismatch.Message);

        var notPdf = "hello"u8.ToArray();
        var nonPdf = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, notPdf, DirectorsApprovalIntegrity.Sha256(notPdf), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("PDF", nonPdf.Message);

        var same = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), signed, DirectorsApprovalIntegrity.Sha256(signed)), Owner));
        Assert.Contains("must be different documents", same.Message);
    }

    [Fact]
    public async Task RecordApproval_RejectsAPageForAccountsThatHaveSinceBeenRegenerated()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        cro.AccountsPdfSha256 = new string('d', 64);
        await db.SaveChangesAsync();
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("generate a new approval page", error.Message);
    }

    [Fact]
    public async Task RecordApproval_RechecksOfficersOnTheApprovalDate()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var second = await db.CompanyOfficers.FindAsync(p.SecondDirectorId);
        second!.ResignedDate = new DateOnly(2026, 10, 1);
        await db.SaveChangesAsync();
        var signed = Pdf("signed");
        var minute = Pdf("minute");

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner));
        Assert.Contains("was not a director in office", error.Message);
    }

    [Fact]
    public async Task Revocation_MakesTheApprovalNotCurrentAndChainsHashes()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var signed = Pdf("signed");
        var minute = Pdf("minute");
        var approved = await service.RecordApprovalAsync(p.CompanyId, p.Id,
            new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var period = await db.AccountingPeriods.Include(x => x.CroFilingPackage).SingleAsync(x => x.Id == p.Id);
        service.AppendRevocationIfCurrent(period, period.CroFilingPackage!, "accounts-regenerated");
        await db.SaveChangesAsync();

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        var revoked = await db.DirectorsApprovalEvents.SingleAsync(e => e.Kind == DirectorsApprovalEventKind.Revoked);
        Assert.Equal(approved.RecordSha256, revoked.PreviousRecordSha256);
    }
}
```

Add the fake renderer (same file) so tests do not run QuestPDF:

```csharp
    internal sealed class FakePageRenderer : IDirectorsApprovalPageRenderer
    {
        public Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content) =>
            Task.FromResult<byte[]>([.. "%PDF-1.7\nunsigned "u8.ToArray(), .. System.Text.Encoding.UTF8.GetBytes(content.ResolutionReference)]);
    }
```

The service depends on `IDirectorsApprovalPageRenderer` (see Interfaces), so these tests never load QuestPDF. `AuditService`'s constructor is `AuditService(AccountsDbContext db, IHttpContextAccessor? httpContextAccessor = null)`.

- [ ] **Step 2: Run to verify failure** — Expected: build FAIL (service not defined).

- [ ] **Step 3: Implement the service**

```csharp
// backend/Accounts.Api/Services/DirectorsApprovalService.cs
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public sealed record DirectorsApprovalPageInput(DateOnly ApprovalDate, int FirstDirectorOfficerId, int? SecondDirectorOfficerId, string ResolutionReference);
public sealed record DirectorsApprovalRecordInput(int ApprovalPageId, byte[] SignedPageArtifact, string SignedPageSha256, byte[] ResolutionArtifact, string ResolutionSha256);
public sealed record DirectorsApprovalPageContent(DateOnly ApprovalDate, string FirstDirectorName, string? SecondDirectorName, string ResolutionReference, string AccountsPdfSha256);
public sealed record CurrentDirectorsApproval(DirectorsApprovalPage Page, DirectorsApprovalEvent Approval);

public interface IDirectorsApprovalPageRenderer
{
    Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content);
}

/// <summary>
/// Companies Act 2014 s.324: the board approves the statutory financial statements and two
/// directors (or the sole director) sign on its behalf. This service records that approval as
/// append-only, hash-chained evidence bound to the exact accounts PDF it approves.
/// </summary>
public sealed class DirectorsApprovalService(
    AccountsDbContext db,
    IDirectorsApprovalPageRenderer renderer,
    AuditService audit,
    TimeProvider? clock = null)
{
    private static readonly TimeZoneInfo Ireland = TimeZoneInfo.FindSystemTimeZoneById("Europe/Dublin");
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    public async Task<IReadOnlyList<CompanyOfficer>> DirectorsInOfficeAsync(int companyId, DateOnly onDate) =>
        (await db.CompanyOfficers.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.Role == OfficerRole.Director)
            .ToListAsync())
        .Where(o => InOffice(o, onDate))
        .OrderBy(o => o.Name)
        .ToList();

    public async Task<DirectorsApprovalPage> GeneratePageAsync(int companyId, int periodId, DirectorsApprovalPageInput input, AuthenticatedUser actor)
    {
        var period = await LoadPeriodAsync(companyId, periodId);
        var cro = RequireAccounts(period);
        var reference = (input.ResolutionReference ?? string.Empty).Trim();
        if (reference.Length is < 3 or > 200)
            throw new BusinessRuleException("Enter the minute or written-resolution reference (3 to 200 characters).");
        ValidateApprovalDate(period, cro, input.ApprovalDate);
        var (first, second, inOffice) = await ResolveSignatoriesAsync(companyId, input.ApprovalDate, input.FirstDirectorOfficerId, input.SecondDirectorOfficerId);

        var bytes = await renderer.RenderAsync(companyId, periodId, new DirectorsApprovalPageContent(
            input.ApprovalDate, first.Name.Trim(), second?.Name.Trim(), reference, cro.AccountsPdfSha256!));
        var page = new DirectorsApprovalPage
        {
            PeriodId = periodId,
            ApprovalDate = input.ApprovalDate,
            FirstDirectorOfficerId = first.Id,
            SecondDirectorOfficerId = second?.Id,
            FirstDirectorName = first.Name.Trim(),
            SecondDirectorName = second?.Name.Trim(),
            DirectorsInOfficeCount = inOffice,
            ResolutionReference = reference,
            AccountsPdfSha256 = cro.AccountsPdfSha256!,
            ArtifactSourceFingerprintSha256 = cro.ArtifactSourceFingerprintSha256!,
            PageArtifact = bytes,
            PageSha256 = DirectorsApprovalIntegrity.Sha256(bytes),
            GeneratedByUserId = AuthenticatedIdentity.AuditUserId(actor),
            GeneratedByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor),
            GeneratedAtUtc = Truncate(time.GetUtcNow().UtcDateTime),
            RecordSha256 = string.Empty
        };
        page.RecordSha256 = DirectorsApprovalIntegrity.ComputePageHash(page);
        db.DirectorsApprovalPages.Add(page);
        await db.SaveChangesAsync();
        await audit.LogAsync(companyId, periodId, nameof(DirectorsApprovalPage), page.Id,
            AuditEventCodes.DirectorsApprovalPageGenerated, null,
            new { page.ApprovalDate, page.FirstDirectorOfficerId, page.SecondDirectorOfficerId, page.ResolutionReference, page.PageSha256, page.AccountsPdfSha256 },
            AuthenticatedIdentity.AuditUserId(actor));
        return page;
    }

    public async Task<DirectorsApprovalEvent> RecordApprovalAsync(int companyId, int periodId, DirectorsApprovalRecordInput input, AuthenticatedUser actor)
    {
        if (actor.Role is not ("Owner" or "Reviewer"))
            throw new BusinessRuleException("Only an Owner or Reviewer can record the directors' approval.");
        var period = await LoadPeriodAsync(companyId, periodId);
        var cro = RequireAccounts(period);
        var page = await db.DirectorsApprovalPages.SingleOrDefaultAsync(p => p.Id == input.ApprovalPageId && p.PeriodId == periodId)
            ?? throw new ResourceNotFoundException("Approval page not found for this period.");
        if (!DirectorsApprovalIntegrity.IsValid(page)
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(page.PageArtifact), page.PageSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The approval page failed integrity verification; generate a new approval page.");
        if (!string.Equals(page.AccountsPdfSha256, cro.AccountsPdfSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.ArtifactSourceFingerprintSha256, cro.ArtifactSourceFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("The accounts have been regenerated since this page was produced; generate a new approval page for the current accounts.");
        ValidateApprovalDate(period, cro, page.ApprovalDate);
        await ResolveSignatoriesAsync(companyId, page.ApprovalDate, page.FirstDirectorOfficerId, page.SecondDirectorOfficerId);

        RequirePdf(input.SignedPageArtifact, input.SignedPageSha256, "signed approval page");
        RequirePdf(input.ResolutionArtifact, input.ResolutionSha256, "minute or written resolution");
        if (input.SignedPageArtifact.AsSpan().SequenceEqual(page.PageArtifact))
            throw new BusinessRuleException("The uploaded signed page is identical to the unsigned page; upload the copy signed by the directors.");
        if (input.SignedPageArtifact.AsSpan().SequenceEqual(input.ResolutionArtifact))
            throw new BusinessRuleException("The signed page and the minute/resolution must be different documents.");

        var events = await db.DirectorsApprovalEvents.Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var pages = await db.DirectorsApprovalPages.Where(p => p.PeriodId == periodId).ToListAsync();
        if (ResolveCurrent(events, pages, cro) is not null)
            throw new BusinessRuleException("A current directors' approval is already recorded for this period.");

        var approval = new DirectorsApprovalEvent
        {
            PeriodId = periodId,
            Kind = DirectorsApprovalEventKind.Approved,
            ApprovalPageId = page.Id,
            ApprovalPageSha256 = page.PageSha256,
            SignedPageArtifact = input.SignedPageArtifact.ToArray(),
            SignedPageSha256 = input.SignedPageSha256.ToLowerInvariant(),
            ResolutionArtifact = input.ResolutionArtifact.ToArray(),
            ResolutionSha256 = input.ResolutionSha256.ToLowerInvariant(),
            RecordedByUserId = AuthenticatedIdentity.AuditUserId(actor),
            RecordedByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor),
            RecordedAtUtc = Truncate(time.GetUtcNow().UtcDateTime),
            PreviousRecordSha256 = events.LastOrDefault()?.RecordSha256,
            RecordSha256 = string.Empty
        };
        approval.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(approval);
        db.DirectorsApprovalEvents.Add(approval);
        period.ApprovalDate ??= page.ApprovalDate;
        await db.SaveChangesAsync();
        await audit.LogAsync(companyId, periodId, nameof(DirectorsApprovalEvent), approval.Id,
            AuditEventCodes.DirectorsApprovalRecorded, null,
            new { page.ApprovalDate, page.FirstDirectorOfficerId, page.SecondDirectorOfficerId, page.ResolutionReference, approval.ApprovalPageSha256, approval.SignedPageSha256, approval.ResolutionSha256, approval.RecordSha256 },
            AuthenticatedIdentity.AuditUserId(actor));
        return approval;
    }

    public async Task<CurrentDirectorsApproval?> GetCurrentApprovalAsync(int periodId)
    {
        var cro = await db.CroFilingPackages.AsNoTracking().SingleOrDefaultAsync(c => c.PeriodId == periodId);
        var events = await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var pages = await db.DirectorsApprovalPages.AsNoTracking().Where(p => p.PeriodId == periodId).ToListAsync();
        return ResolveCurrent(events, pages, cro);
    }

    /// <summary>The latest Approved event, not followed by a revocation, whose page and event hashes verify and whose page binds the current accounts.</summary>
    public static CurrentDirectorsApproval? ResolveCurrent(
        IReadOnlyList<DirectorsApprovalEvent> events,
        IReadOnlyList<DirectorsApprovalPage> pages,
        CroFilingPackage? cro)
    {
        if (cro?.AccountsPdfSha256 is null || events.Count == 0)
            return null;
        string? previous = null;
        foreach (var e in events.OrderBy(e => e.Id))
        {
            if (!DirectorsApprovalIntegrity.IsValid(e) || e.PreviousRecordSha256 != previous)
                return null; // a broken chain invalidates every later approval
            previous = e.RecordSha256;
        }
        var last = events.OrderBy(e => e.Id).Last();
        if (last.Kind != DirectorsApprovalEventKind.Approved)
            return null;
        var page = pages.SingleOrDefault(p => p.Id == last.ApprovalPageId);
        if (page is null
            || !DirectorsApprovalIntegrity.IsValid(page)
            || !string.Equals(page.PageSha256, last.ApprovalPageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.AccountsPdfSha256, cro.AccountsPdfSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.ArtifactSourceFingerprintSha256, cro.ArtifactSourceFingerprintSha256, StringComparison.OrdinalIgnoreCase)
            || last.SignedPageArtifact is null || last.ResolutionArtifact is null
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(last.SignedPageArtifact), last.SignedPageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(last.ResolutionArtifact), last.ResolutionSha256, StringComparison.OrdinalIgnoreCase))
            return null;
        return new CurrentDirectorsApproval(page, last);
    }

    /// <summary>Adds a Revoked event for a current approval; the caller saves in its own unit of work.</summary>
    public void AppendRevocationIfCurrent(AccountingPeriod period, CroFilingPackage cro, string reason)
    {
        var events = db.DirectorsApprovalEvents.Where(e => e.PeriodId == period.Id).OrderBy(e => e.Id).ToList();
        if (events.Count == 0 || events.Last().Kind != DirectorsApprovalEventKind.Approved)
            return;
        var last = events.Last();
        var revoked = new DirectorsApprovalEvent
        {
            PeriodId = period.Id,
            Kind = DirectorsApprovalEventKind.Revoked,
            ApprovalPageId = last.ApprovalPageId,
            ApprovalPageSha256 = last.ApprovalPageSha256,
            RevocationReason = reason,
            RecordedByUserId = "system",
            RecordedByDisplayName = "FilingBridge",
            RecordedAtUtc = Truncate(time.GetUtcNow().UtcDateTime),
            PreviousRecordSha256 = last.RecordSha256,
            RecordSha256 = string.Empty
        };
        revoked.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(revoked);
        db.DirectorsApprovalEvents.Add(revoked);
    }

    private async Task<AccountingPeriod> LoadPeriodAsync(int companyId, int periodId) =>
        await db.AccountingPeriods.Include(p => p.CroFilingPackage)
            .SingleOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
        ?? throw new ResourceNotFoundException($"Period {periodId} not found");

    private static CroFilingPackage RequireAccounts(AccountingPeriod period)
    {
        var cro = period.CroFilingPackage;
        if (cro is null || !cro.AccountsPdfGenerated || cro.AccountsPdfSha256 is not { Length: 64 } || cro.ArtifactSourceFingerprintSha256 is not { Length: 64 })
            throw new BusinessRuleException("Generate the CRO accounts PDF before preparing the directors' approval.");
        return cro;
    }

    private void ValidateApprovalDate(AccountingPeriod period, CroFilingPackage cro, DateOnly date)
    {
        if (date < period.PeriodEnd)
            throw new BusinessRuleException($"The board approval date must be on or after the period end ({period.PeriodEnd:yyyy-MM-dd}).");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, Ireland));
        if (date > today)
            throw new BusinessRuleException("The board approval date cannot be in the future.");
        var generated = cro.AccountsPdfGeneratedAtUtc ?? cro.GeneratedAt;
        if (date < DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(generated, DateTimeKind.Utc), Ireland)))
            throw new BusinessRuleException("The board approval date is before the accounts PDF it approves was generated.");
    }

    private async Task<(CompanyOfficer First, CompanyOfficer? Second, int InOffice)> ResolveSignatoriesAsync(
        int companyId, DateOnly onDate, int firstId, int? secondId)
    {
        var inOffice = await DirectorsInOfficeAsync(companyId, onDate);
        var officers = await db.CompanyOfficers.AsNoTracking().Where(o => o.CompanyId == companyId).ToListAsync();
        CompanyOfficer Require(int id)
        {
            var officer = officers.SingleOrDefault(o => o.Id == id)
                ?? throw new ResourceNotFoundException("Officer not found for this company.");
            if (!inOffice.Any(d => d.Id == id))
                throw new BusinessRuleException($"{officer.Name.Trim()} was not a director in office on {onDate:yyyy-MM-dd}.");
            return officer;
        }
        if (inOffice.Count == 0)
            throw new BusinessRuleException($"No director was in office on {onDate:yyyy-MM-dd}.");
        var first = Require(firstId);
        if (inOffice.Count == 1)
        {
            if (secondId is not null)
                throw new BusinessRuleException("The company had a sole director on that date, so only that director signs (s.324(2)).");
            return (first, null, 1);
        }
        if (secondId is null)
            throw new BusinessRuleException($"The company had {inOffice.Count} directors in office on {onDate:yyyy-MM-dd}, so two directors must sign.");
        var second = Require(secondId.Value);
        if (first.Id == second.Id
            || string.Equals(first.Name.Trim(), second.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Choose two different directors.");
        return (first, second, inOffice.Count);
    }

    private static bool InOffice(CompanyOfficer officer, DateOnly date) =>
        (officer.AppointedDate is null || officer.AppointedDate <= date)
        && (officer.ResignedDate is null || officer.ResignedDate >= date);

    private static void RequirePdf(byte[]? content, string? declaredSha, string label)
    {
        if (content is not { Length: > 5 } || !content.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new BusinessRuleException($"Upload the {label} as a PDF.");
        if (declaredSha is not { Length: 64 }
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(content), declaredSha, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException($"The {label} does not match its declared SHA-256 digest.");
    }

    private static DateTime Truncate(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % (TimeSpan.TicksPerMillisecond / 1000), DateTimeKind.Utc);
}
```

Confirm `AuthenticatedIdentity.AuditUserId` / `ReviewerDisplayName` signatures (`Services/AuthContext.cs` or `AuthenticatedIdentity.cs`) and `CompanyOfficer.AppointedDate` / `ResignedDate` nullability, adjusting `InOffice` if `AppointedDate` is non-nullable. The error text "Choose two different directors." must contain "two different directors" (asserted above).

- [ ] **Step 4: Implement the renderer in DocumentGeneratorService**

Make `DocumentGeneratorService` implement `IDirectorsApprovalPageRenderer`, following `GenerateSignaturePageAsync` (line ~1037) for header/company loading:

```csharp
    public async Task<byte[]> RenderAsync(int companyId, int periodId, DirectorsApprovalPageContent content)
    {
        var period = await db.AccountingPeriods.Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var company = period.Company;
        var signatories = content.SecondDirectorName is null
            ? new[] { content.FirstDirectorName }
            : new[] { content.FirstDirectorName, content.SecondDirectorName };
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(11));
            page.Header().Element(c => ComposeHeader(c, company));
            page.Content().Column(col =>
            {
                col.Spacing(10);
                col.Item().Text("Approval of the financial statements — Companies Act 2014, section 324").Bold().FontSize(14);
                col.Item().Text($"{company.LegalName} (CRO {company.CroNumber}) — financial year {period.PeriodStart:dd MMMM yyyy} to {period.PeriodEnd:dd MMMM yyyy}");
                col.Item().Text($"The financial statements for the financial year were approved by the board of directors on {content.ApprovalDate:dd MMMM yyyy} ({content.ResolutionReference}) and are signed on its behalf by:");
                if (content.SecondDirectorName is null)
                    col.Item().Text("The company has a sole director, who approves and signs the financial statements (s.324(2)).").Italic();
                foreach (var name in signatories)
                {
                    col.Item().PaddingTop(30).LineHorizontal(0.5f);
                    col.Item().Text($"{name}, Director").SemiBold();
                    col.Item().Text($"Date: {content.ApprovalDate:dd MMMM yyyy}");
                }
                col.Item().PaddingTop(20).Text($"Approved accounts PDF SHA-256: {content.AccountsPdfSha256}").FontSize(8);
            });
        })).GeneratePdf();
    }
```

Register in `Program.cs` beside the other scoped services:

```csharp
builder.Services.AddScoped<IDirectorsApprovalPageRenderer>(sp => sp.GetRequiredService<DocumentGeneratorService>());
builder.Services.AddScoped<DirectorsApprovalService>();
```

- [ ] **Step 5: Run tests** — `--filter "FullyQualifiedName~DirectorsApprovalTests"` — Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add backend/Accounts.Api/Services/DirectorsApprovalService.cs backend/Accounts.Api/Services/DocumentGeneratorService.cs backend/Accounts.Api/Program.cs backend/Accounts.Tests/DirectorsApprovalTests.cs
git commit -m "feat: directors' s.324 approval service and approval page"
```

---

### Task 4: Revoke on accounts regeneration and record the accounts generation time

**Files:**
- Modify: `backend/Accounts.Api/Services/FilingReleaseGate.cs` (`RecordCroArtifactAsync`, ~line 144–227)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`

**Interfaces:**
- Consumes: `DirectorsApprovalService.AppendRevocationIfCurrent`
- Produces: `CroFilingPackage.AccountsPdfGeneratedAtUtc` maintained by the gate.

- [ ] **Step 1: Failing test**

```csharp
    [Fact]
    public async Task RegeneratingTheAccountsPdf_RevokesTheDirectorsApproval()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var signed = Pdf("signed"); var minute = Pdf("minute");
        await service.RecordApprovalAsync(p.CompanyId, p.Id, new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        await FilingReleaseGateHarness.RecordChangedAccountsPdfAsync(db, p.CompanyId, p.Id, Pdf("accounts v2"));

        Assert.Null(await service.GetCurrentApprovalAsync(p.Id));
        var cro = await db.CroFilingPackages.SingleAsync(c => c.PeriodId == p.Id);
        Assert.True(cro.AccountsPdfGeneratedAtUtc > new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc));
    }
```

`FilingReleaseGateHarness.RecordChangedAccountsPdfAsync` is a small test helper: construct `FilingReleaseGate` exactly as `FilingReleaseGateTests` does (look at its `CreateGate`/constructor usage) and call `RecordCroArtifactAsync(companyId, periodId, FilingReleaseArtifact.CroAccountsPdf, bytes, ...)` with the argument list that method takes. If `RecordCroArtifactAsync` requires readiness fixtures that the seed lacks, assert the revocation through a direct call to the extracted helper in Step 3 instead (`FilingReleaseGate.OnCroAccountsChanged(period, package, directorsApproval)`), keeping the test's intent: a changed accounts hash revokes.

- [ ] **Step 2: Run** — Expected: FAIL (approval still current).

- [ ] **Step 3: Implement**

Give `FilingReleaseGate` a constructor dependency on `DirectorsApprovalService` (follow how its existing dependencies are injected; update every `new FilingReleaseGate(...)` in tests via a shared test factory). In `RecordCroArtifactAsync`, in the branch where the accounts PDF hash changes (the code that sets `package.AccountsPdfSha256 = hash`):

```csharp
            package.AccountsPdfGeneratedAtUtc = DateTime.UtcNow;
            directorsApproval.AppendRevocationIfCurrent(period, package, "accounts-regenerated");
```

so the revocation saves in the same `SaveChangesAsync` as the artifact. Do the same where `ClearCroSignatureEvidence`/`RevokeCroApproval` is called because the source fingerprint changed (search for other `RevokeCroApproval(` call sites and add the revocation with reason `accounts-source-changed`).

- [ ] **Step 4: Run** — Expected: PASS. Also run `--filter "FullyQualifiedName~FilingReleaseGateTests"` — Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: regenerating the accounts revokes the directors' approval"
```

---

### Task 5: Endpoints — approval page, approval, CRO signing pack, auditor report; permissions and re-authentication

**Files:**
- Create: `backend/Accounts.Api/Endpoints/DirectorsApprovalEndpoints.cs`
- Modify: `backend/Accounts.Api/Program.cs` (map endpoints)
- Modify: `backend/Accounts.Api/Services/RoleAuthorizationService.cs` (`IsReviewerWrite`)
- Modify: `backend/Accounts.Api/Middleware/RecentAuthenticationMiddleware.cs` (list at ~line 57)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`, `backend/Accounts.Tests/AccountsWorkflowTests.Policy.cs`

**Interfaces:**
- Consumes: `DirectorsApprovalService`; `FilingReleaseGate.RecordVerifiedCroSignatureAsync(int, int, CroSignatureEvidence, string?)`; `FilingReleaseGate.RecordVerifiedAuditorReportAsync(...)` (read its signature at line 382).
- Produces routes (group `/api/companies/{companyId:int}/periods/{periodId:int}/filing`):
  - `GET  /directors-approval` → `DirectorsApprovalStateResponse(IReadOnlyList<DirectorOption> EligibleDirectors, DirectorsApprovalPageSummary? LatestPage, DirectorsApprovalSummary? Current)` — query `?onDate=yyyy-MM-dd` selects the eligibility date (default: period end)
  - `POST /directors-approval-page` body `DirectorsApprovalPageInput` → `DirectorsApprovalPageSummary`
  - `GET  /directors-approval-page/{pageId:int}` → `application/pdf` (the unsigned page)
  - `POST /directors-approval` body `DirectorsApprovalRecordInput` → `DirectorsApprovalSummary`
  - `POST /cro-signing-pack` body `CroSigningPackInput(int DirectorOfficerId, int SecretaryOfficerId, DateTime SignedAtUtc, byte[] SignedArtifact, string SignedArtifactSha256)` → 200
  - `POST /auditor-report` body: the fields `RecordVerifiedAuditorReportAsync` requires (firm, signer, body, membership, reference, signedAtUtc, artifact, sha256) → 200
  - DTOs: `DirectorOption(int OfficerId, string Name)`, `DirectorsApprovalPageSummary(int Id, DateOnly ApprovalDate, string FirstDirectorName, string? SecondDirectorName, int DirectorsInOfficeCount, string ResolutionReference, string PageSha256, string AccountsPdfSha256, string RecordSha256, DateTime GeneratedAtUtc)`, `DirectorsApprovalSummary(int EventId, DirectorsApprovalPageSummary Page, string SignedPageSha256, string ResolutionSha256, string RecordedByDisplayName, DateTime RecordedAtUtc, string RecordSha256)`. Every nullable member carries `[property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]` (WhenWritingNull is global).

- [ ] **Step 1: Failing policy test** (append to `AccountsWorkflowTests.Policy.cs`)

```csharp
    [Theory]
    [InlineData("Reviewer", "POST", "directors-approval", true)]
    [InlineData("Reviewer", "POST", "directors-approval-page", true)]
    [InlineData("Reviewer", "POST", "cro-signing-pack", true)]
    [InlineData("Reviewer", "POST", "auditor-report", true)]
    [InlineData("Accountant", "POST", "directors-approval", false)]
    [InlineData("Accountant", "POST", "directors-approval-page", true)]
    [InlineData("Client", "POST", "directors-approval-page", false)]
    public void DirectorsApprovalRoutes_HaveTheIntendedRolePermissions(string role, string method, string action, bool allowed)
    {
        var decision = RoleAuthorizationService.Authorize(role, new PathString($"/api/companies/1/periods/9/filing/{action}"), method);
        Assert.Equal(allowed, decision.Allowed);
    }

    [Theory]
    [InlineData("/api/companies/1/periods/9/filing/directors-approval")]
    [InlineData("/api/companies/1/periods/9/filing/cro-signing-pack")]
    [InlineData("/api/companies/1/periods/9/filing/auditor-report")]
    public void EvidenceRecordingRoutes_RequireRecentAuthentication(string path) =>
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentAuthentication(new PathString(path), "POST"));
```

Check the real public names: `RoleAuthorizationService`'s entry point (line ~40) and the static helper in `RecentAuthenticationMiddleware` (line ~50); adapt the calls to their exact signatures.

- [ ] **Step 2: Run** — Expected: FAIL.

- [ ] **Step 3: Implement routing**

In `IsReviewerWrite`, extend the POST branch:

```csharp
        if (HttpMethods.IsPost(method)
            && (IsPeriodFilingAction(segments, "cro-payment")
                || IsPeriodFilingAction(segments, "revenue-external-validation")
                || IsPeriodFilingAction(segments, "directors-approval")
                || IsPeriodFilingAction(segments, "directors-approval-page")
                || IsPeriodFilingAction(segments, "cro-signing-pack")
                || IsPeriodFilingAction(segments, "auditor-report")
                || IsPeriodFilingAction(segments, "accountant-review")
                || IsPeriodAction(segments, "mark-filed")))
        {
            return true;
        }
```

Accountants are denied reviewer writes, but generating the page and recording the optional accountant review are Accountant-permitted: add, before the Accountant denial in `Authorize`,

```csharp
            if (HttpMethods.IsPost(method)
                && (IsPeriodFilingAction(SplitPath(path), "directors-approval-page") || IsPeriodFilingAction(SplitPath(path), "accountant-review")))
                return RoleAuthorizationDecision.Allowed();
```

In `RecentAuthenticationMiddleware`'s predicate add:

```csharp
            || value.EndsWith("/filing/directors-approval", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/cro-signing-pack", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/filing/auditor-report", StringComparison.OrdinalIgnoreCase)
```

- [ ] **Step 4: Implement endpoints** following `FilingWorkflowEndpoints.cs` (access check → `EndpointRequestAuthorization.AuthorizeCurrentRequest` → service), e.g.:

```csharp
        group.MapPost("/directors-approval", async (int companyId, int periodId, DirectorsApprovalRecordInput input,
            DirectorsApprovalService service, AccountsDbContext db, HttpContext context, ApiAccessService apiAccess) =>
        {
            if (!await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(context, db, companyId, periodId))
                return Results.NotFound();
            if (EndpointRequestAuthorization.AuthorizeCurrentRequest(context, apiAccess) is { } denied)
                return denied;
            var approval = await service.RecordApprovalAsync(companyId, periodId, input, AuthContext.RequireUser(context));
            var current = await service.GetCurrentApprovalAsync(periodId)
                ?? throw new BusinessRuleException("The approval was recorded but did not verify; contact support.");
            return Results.Ok(DirectorsApprovalSummary.From(current));
        }).Produces<DirectorsApprovalSummary>();
```

The supported-path rule lives in the endpoints (the service stays independent of readiness so its tests do not need a full filing fixture). `POST /directors-approval-page` and `POST /directors-approval` both start, after the access checks, with:

```csharp
            var profile = await readiness.GetProfileAsync(companyId, periodId);
            if (!profile.SupportedPath)
                throw new BusinessRuleException("This company is outside the supported filing path.");
```

(`readiness` is the injected `FilingReadinessProfileService`; `SupportedPath` is the existing record member.) The Step 5 endpoint test also covers this: a period whose profile reports `SupportedPath == false` (e.g. a group/holding company flag, as `FilingReadinessProfileTests` sets up) gets 400 with that message and no page row.

`/cro-signing-pack` validates that `DirectorOfficerId` is a Director in office on `SignedAtUtc`'s date and `SecretaryOfficerId` is a Secretary/CompanySecretary in office, copies their names into `CroSignatureEvidence`, and calls `RecordVerifiedCroSignatureAsync`; audit `CroSigningPackRecorded`. `/auditor-report` calls `RecordVerifiedAuditorReportAsync` and audits `AuditorReportRecorded`. Map with `app.MapDirectorsApprovalEndpoints();` in `Program.cs` next to `MapFilingWorkflowEndpoints`.

- [ ] **Step 5: Endpoint test** (append to `DirectorsApprovalTests.cs`) calling the endpoint delegate through `DirectorsApprovalEndpoints.RecordApprovalEndpointAsync` (extract each lambda to a `public static async Task<IResult> …EndpointAsync` method, the pattern used by `ClassificationEndpoints`), asserting 200 and that a `Client` context gets 403 before any row is written.

- [ ] **Step 6: Run** `--filter "FullyQualifiedName~DirectorsApproval|FullyQualifiedName~Policy"` — Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/Accounts.Api backend/Accounts.Tests
git commit -m "feat: endpoints for directors' approval, executed CRO signing pack and auditor report"
```

---

### Task 6: Release gate binds the directors' approval for CRO, Revenue and Charity

**Files:**
- Modify: `backend/Accounts.Api/Services/FilingReleaseGate.cs` (`BindApprovalAsync` ~424, `EnsureCurrentCroApprovalAsync` ~1010, `EnsureCurrentRevenueApproval` ~1039, `EnsureCurrentCharityApproval` ~1067, `CroManifest` ~1353, `RevenueManifest`, `CharityManifest`, `EnsureAuditorEvidence` message ~920)
- Modify: `backend/Accounts.Api/Services/FilingWorkflowService.cs` (approve branches ~196, ~494, ~643)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`, `backend/Accounts.Tests/FilingReleaseGateTests.cs`

**Interfaces:**
- Consumes: `DirectorsApprovalService.GetCurrentApprovalAsync`, `ResolveCurrent`.
- Produces: `const string FilingReleaseGate.BoardApprovalCapacity = "board-of-directors-s324"`; `BindApprovalAsync(int companyId, int periodId, FilingReleaseWorkflow workflow, string? auditUserId)` (the `approvedBy` parameter is removed: the approvers come from the record).

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public async Task CroApproval_BindsTheCurrentDirectorsApproval()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(); // builds a period whose CRO artifacts, signing pack and readiness are complete (reuse FilingReleaseGateTests' fixture builder)
        await harness.RecordDirectorsApprovalAsync();

        await harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1");

        var cro = await harness.Db.CroFilingPackages.SingleAsync(c => c.PeriodId == harness.PeriodId);
        Assert.Equal(FilingReleaseGate.BoardApprovalCapacity, cro.ApprovalCapacity);
        Assert.Equal("Director One and Director Two", cro.ApprovedBy);
        Assert.Equal(64, cro.ApprovedArtifactManifestSha256!.Length);
    }

    [Fact]
    public async Task CroApproval_WithoutADirectorsApproval_IsBlocked()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync();
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1"));
        Assert.Contains("directors' approval (s.324)", error.Message);
    }

    [Fact]
    public async Task AuditedCompany_StillRequiresTheAuditorsReport()
    {
        var harness = await FilingGateFixture.ReadyForApprovalAsync(auditExempt: false, withAuditorReport: false);
        await harness.RecordDirectorsApprovalAsync();
        var error = await Assert.ThrowsAsync<FilingReleaseBlockedException>(() =>
            harness.Gate.BindApprovalAsync(harness.CompanyId, harness.PeriodId, FilingReleaseWorkflow.Cro, "user:1"));
        Assert.Contains("auditor", error.Message, StringComparison.OrdinalIgnoreCase);
    }
```

`FilingGateFixture` is new test infrastructure in `backend/Accounts.Tests/FilingGateFixture.cs`: lift the period/artifact/signature setup that `FilingReleaseGateTests` already uses for its successful `BindVerifiedApprovalAsync` test into a reusable builder, add `RecordDirectorsApprovalAsync()` that runs Task 3's page + approval flow with two directors, and expose `Gate`, `Db`, `CompanyId`, `PeriodId`.

- [ ] **Step 2: Run** — Expected: FAIL.

- [ ] **Step 3: Implement**

Replace `BindApprovalAsync`'s body:

```csharp
    public const string BoardApprovalCapacity = "board-of-directors-s324";

    public Task BindApprovalAsync(int companyId, int periodId, FilingReleaseWorkflow workflow, string? auditUserId = null) =>
        GuardAsync(companyId, periodId, workflow, "Approved", auditUserId, async period =>
        {
            if (workflow == FilingReleaseWorkflow.Revenue)
                RevenueIxbrlGenerationPolicy.AssertFilingReadyGenerationEnabled();
            await EnsureBaseReadinessAsync(period);
            var approval = await RequireDirectorsApprovalAsync(period);
            var approvers = approval.Page.SecondDirectorName is null
                ? approval.Page.FirstDirectorName
                : $"{approval.Page.FirstDirectorName} and {approval.Page.SecondDirectorName}";
            var candidate = CurrentReleaseCandidate;
            switch (workflow)
            {
                case FilingReleaseWorkflow.Cro:
                {
                    var package = period.CroFilingPackage ?? Block<CroFilingPackage>("Generate the CRO accounts and signature artifacts before approval.");
                    await EnsureCroArtifactSourceCurrentAsync(period, package);
                    EnsureCroArtifacts(period, package, candidate);
                    EnsureCroSignatures(package);
                    EnsureAuditorEvidence(period);
                    ApplyBoardApproval(package, approval, approvers, RequireTenantId(period.Company), "cro-final-filing");
                    package.ApprovedArtifactManifestSha256 = CroManifest(period, package, candidate, approval);
                    package.ApprovedReleaseCandidate = candidate;
                    package.ApprovedAt = DateTime.UtcNow;
                    break;
                }
                // Revenue and Charity: same shape as the existing BindVerifiedApprovalAsync branches,
                // replacing ApplyApprovalEvidence(package, evidence) with
                // ApplyBoardApproval(package, approval, approvers, tenantId, RequiredApprovalScope(workflow))
                // and passing `approval` to RevenueManifest / CharityManifest.
                default:
                    throw new ArgumentOutOfRangeException(nameof(workflow));
            }
            await _db.SaveChangesAsync();
        });

    private async Task<CurrentDirectorsApproval> RequireDirectorsApprovalAsync(AccountingPeriod period) =>
        await directorsApproval.GetCurrentApprovalAsync(period.Id)
        ?? Block<CurrentDirectorsApproval>("Record the board's directors' approval (s.324), signed by two directors or the sole director, before approving the filing.");
```

`ApplyBoardApproval` sets on the package: `ApprovedBy = approvers`, `ApproverTenantId = tenantId`, `ApprovalScope = scope`, `ApprovalCapacity = BoardApprovalCapacity`, `ApprovalDecision = "approved"`, and clears the professional-body fields (`ApproverProfessionalBody`, `ApproverMembershipNumber`, `ApproverVerificationReference`, `ApproverVerificationArtifact`, `ApproverVerificationArtifactSha256`, `ApproverVerifiedAt`, `ApproverCredentialValidUntil` = null). Write it once as a generic over the three package types if they share those property names; otherwise three small overloads.

Manifests: add a `CurrentDirectorsApproval approval` parameter and replace the two accountant entries with:

```csharp
            ("directors-approval-page", approval.Page.PageSha256),
            ("directors-signed-page", approval.Approval.SignedPageSha256!),
            ("board-resolution", approval.Approval.ResolutionSha256!),
            ("directors-approval-record", approval.Approval.RecordSha256),
```

The `EnsureCurrent*Approval` methods replace `EnsureStoredQualifiedAccountantEvidence(...)` with:

```csharp
        var approval = await RequireDirectorsApprovalAsync(period);
        if (!string.Equals(package.ApprovalCapacity, BoardApprovalCapacity, StringComparison.Ordinal))
            Block("The filing approval was not bound to the directors' approval (s.324); approve it again.");
```

and compute the expected manifest with `approval`. (`EnsureCurrentRevenueApproval` / `EnsureCurrentCharityApproval` become `async Task`; update their callers at lines ~544, ~557, ~598, ~609, ~1280.) Change the `EnsureAuditorEvidence` and `HasCompleteAuditorReportEvidence` messages from "accepted qualified-accountant review" to "accepted by the directors"; keep the checks themselves.

`BindVerifiedApprovalAsync` and `EnsureQualifiedAccountantEvidence` are no longer used by any workflow: delete `BindVerifiedApprovalAsync` and move the credential checks into Task 8's optional attestation.

In `FilingWorkflowService`, the three approve branches call `ReleaseGate.BindApprovalAsync(companyId, periodId, workflow, auditUserId ?? by)` (drop `RequireNamedReviewer` for approval — the approvers are the recorded directors; keep the role check).

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~DirectorsApproval|FullyQualifiedName~FilingReleaseGate|FullyQualifiedName~FilingWorkflow|FullyQualifiedName~ExternalFilingHandoff"`. Expected: new tests PASS; existing tests that call `BindVerifiedApprovalAsync` fail — they are updated in Task 9.

- [ ] **Step 5: Commit**

```bash
git commit -am "feat: filing approvals bind the directors' s.324 approval"
```

---

### Task 7: Readiness profile reports directors' approval and the approval authority

**Files:**
- Modify: `backend/Accounts.Api/Services/FilingReadinessProfileService.cs` (records at top; lines ~255–310; `BuildSignOffPacket` ~620; `DetermineSignOffState` ~666; step `accountant-approval` ~760; `AssertCanApproveCroPackAsync` ~487; `DetermineAllowedNextActions` ~500)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`

**Interfaces:**
- Produces: `FilingReadinessProfile.ApprovalAuthority : string` (always `"board-of-directors-s324"`), `AccountantReviewRequired` always `false`; `FilingReadinessSignOffPacket.ReadyForDirectorsApproval` (renamed from `ReadyForAccountantApproval`); evidence code `directors-approval`; blocker `directors-approval-required`; action `record-directors-approval`; sign-off step `directors-approval`; sign-off states `ready-for-directors-approval` (was `ready-for-accountant-review`), `approved-external-evidence-open` label "Directors approved, external evidence open", blocked label "Blocked before directors' approval".

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public async Task ReadinessProfile_RequiresDirectorsApprovalNotAnAccountant()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.False(profile.AccountantReviewRequired);
        Assert.Equal("board-of-directors-s324", profile.ApprovalAuthority);
        Assert.Contains(profile.BlockingIssues, i => i.Code == "directors-approval-required");
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code == "accountant-review-required");
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "directors-approval" && !e.Satisfied);
        Assert.Contains(profile.SourceReferences, s => s.Code == "companies-act-2014-s324");
    }

    [Fact]
    public async Task ReadinessProfile_DirectorsApprovalSatisfiesTheEvidence()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var service = Service(db);
        var page = await service.GeneratePageAsync(p.CompanyId, p.Id, new(new DateOnly(2026, 11, 20), p.FirstDirectorId, p.SecondDirectorId, "WR"), Owner);
        var signed = Pdf("signed"); var minute = Pdf("minute");
        await service.RecordApprovalAsync(p.CompanyId, p.Id, new(page.Id, signed, DirectorsApprovalIntegrity.Sha256(signed), minute, DirectorsApprovalIntegrity.Sha256(minute)), Owner);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Contains(profile.RequiredEvidence, e => e.Code == "directors-approval" && e.Satisfied);
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code == "directors-approval-required");
    }

    [Fact]
    public async Task MemberAuditNoticeAfterApproval_BringsBackTheAuditRequirement()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        var period = await db.AccountingPeriods.FindAsync(p.Id);
        period!.MemberAuditNoticeReceived = true;
        await db.SaveChangesAsync();

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Contains(profile.BlockingIssues, i => i.Code == "auditor-handoff-required");
    }
```

- [ ] **Step 2: Run** — Expected: FAIL.

- [ ] **Step 3: Implement**

- Records: add `string ApprovalAuthority` to `FilingReadinessProfile` immediately after `AccountantReviewState`; rename `ReadyForAccountantApproval` → `ReadyForDirectorsApproval` in `FilingReadinessSignOffPacket`.
- `auditRequired` becomes live (member notice included):

```csharp
        var auditRequired = period.FilingRegime?.AuditExempt == false
            || period.MemberAuditNoticeReceived
            || sizeClass is CompanySizeClass.Medium or CompanySizeClass.Large;
```

- In the `audit-report` evidence (line ~277) change the unsatisfied text to "Retained signed auditor-report PDF, firm/signer identity and the directors' acceptance of the report are required." The `auditor-handoff-required` blocker (line ~283) and its `manualHandoff = true` stay exactly as they are: an audited company remains outside automatic approval until the signed report is recorded through `POST /filing/auditor-report`.
- Replace the `qualifiedApprovalRetained` block (lines ~293–306) with:

```csharp
        var directorsEvents = await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var directorsPages = await db.DirectorsApprovalPages.AsNoTracking().Where(p => p.PeriodId == periodId).ToListAsync();
        var currentApproval = DirectorsApprovalService.ResolveCurrent(directorsEvents, directorsPages, cro);
        var integrityFailure = directorsEvents.Count > 0 && currentApproval is null
            && directorsEvents.Any(e => !DirectorsApprovalIntegrity.IsValid(e));
        RequireEvidence(
            "directors-approval",
            "Board approval of the financial statements, s.324",
            currentApproval is not null,
            currentApproval is not null
                ? $"Approved by the board on {currentApproval.Page.ApprovalDate:yyyy-MM-dd} ({currentApproval.Page.ResolutionReference}); signed by {currentApproval.Page.FirstDirectorName}{(currentApproval.Page.SecondDirectorName is null ? " (sole director)" : $" and {currentApproval.Page.SecondDirectorName}")}; record {currentApproval.Approval.RecordSha256[..12]}."
                : integrityFailure
                    ? "Directors' approval record failed integrity verification."
                    : "Generate the s.324 approval page, have the directors sign it, then upload it with the minute or written resolution.",
            IrishStatutoryRuleSources.CompaniesActSection324);
        if (currentApproval is null)
            Block("directors-approval-required", "The board must approve the financial statements and two directors (or the sole director) must sign on its behalf (Companies Act 2014, s.324).", IrishStatutoryRuleSources.CompaniesActSection324);
        sourceReferences.Add(IrishStatutoryRuleSources.CompaniesActSection324);
        var reviewState = OptionalAccountantReviewState(cro); // Task 8; until then: "Not required by law — not recorded"
```

- Constructor call: `AccountantReviewRequired: false, reviewState, ApprovalAuthority: "board-of-directors-s324"`.
- `BuildSignOffPacket`: `directorsApproved = currentApproval is not null && cro?.ApprovalCapacity == FilingReleaseGate.BoardApprovalCapacity && cro.ApprovedAt is not null` replaces `accountantApproved`; `readyForDirectorsApproval = currentApproval is not null && cro?.FilingStatus != FilingStatus.Approved && supportedPath && !manualHandoff && blockers.All(i => IsRevenueOnlyBlocker(i.Code))`. Pass `currentApproval` into `BuildSignOffPacket`/`BuildSignOffSteps`.
- Step `accountant-approval` becomes:

```csharp
            new FilingReadinessSignOffStep(
                "directors-approval",
                "Directors' approval (s.324)",
                currentApproval is not null ? "complete" : "pending",
                currentApproval is not null
                    ? $"Approved by the board on {currentApproval.Page.ApprovalDate:yyyy-MM-dd}; signed by {currentApproval.Page.FirstDirectorName}{(currentApproval.Page.SecondDirectorName is null ? "" : $" and {currentApproval.Page.SecondDirectorName}")}."
                    : "Record the board's approval: generate the s.324 page, sign it, upload it with the minute or resolution.",
                [IrishStatutoryRuleSources.CompaniesActSection324])
```

- `DetermineSignOffState`: rename states/labels as listed in Interfaces.
- `AssertCanApproveCroPackAsync`: filter nothing for the directors' blocker (approval requires it satisfied) — the filter becomes `.Where(i => !IsRevenueOnlyBlocker(i.Code))`; the manual-handoff message becomes "This company is outside the supported filing path.".
- `DetermineAllowedNextActions`: add `if (docsReady && currentApproval is null) actions.Add("record-directors-approval");` and `approve-cro-pack` when `docsReady && currentApproval is not null && blockers.All(i => IsRevenueOnlyBlocker(i.Code)) && cro?.FilingStatus != FilingStatus.Approved`. Add `record-cro-signing-pack` when both CRO artifacts are generated and `cro.SignedPdfSha256 is null`.
- Remove `HasCurrentQualifiedAccountantApprovalEvidence`, `IsRecognisedProfessionalBody`, `IsHttpsReference` from this file only if Task 8 does not reuse them here (Task 8 moves them).

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~DirectorsApproval|FullyQualifiedName~FilingReadinessProfile"` — new tests PASS; legacy assertions are updated in Task 9.

- [ ] **Step 5: Commit** — `git commit -am "feat: readiness profile requires the directors' s.324 approval, not an accountant"`

---

### Task 8: Optional accountant review attestation and release catalog

**Files:**
- Modify: `backend/Accounts.Api/Endpoints/DirectorsApprovalEndpoints.cs` (`POST /accountant-review`)
- Modify: `backend/Accounts.Api/Services/FilingReadinessProfileService.cs` (`OptionalAccountantReviewState`)
- Modify: `backend/Accounts.Api/Services/FilingReleaseGate.cs` (clear attestation on accounts regeneration)
- Modify: `backend/Accounts.Api/Services/ProductionReadinessReleaseCatalog.cs` (lines ~40–50, ~309, ~318–329, ~453–462, ~561)
- Test: `backend/Accounts.Tests/DirectorsApprovalTests.cs`, `backend/Accounts.Tests/ProductionReadinessReportTests.cs`

**Interfaces:**
- Produces: `AccountantReviewAttestationInput(string ReviewerName, string ProfessionalBody, string MembershipNumber, DateOnly ReviewDate, byte[]? ReviewLetter, string? ReviewLetterSha256)`; readiness evidence `accountant-review-optional` (Required = false); `AccountantReviewState` = "Not required by law — not recorded" | `$"Optional review recorded: {name} ({body}, {membership})"`.

- [ ] **Step 1: Failing tests**

```csharp
    [Fact]
    public async Task OptionalAccountantReview_IsRecordedButNeverBlocks()
    {
        await using var db = CreateDb();
        var p = await SeedPeriodAsync(db);
        await DirectorsApprovalEndpoints.RecordAccountantReviewAsync(db, p.CompanyId, p.Id,
            new AccountantReviewAttestationInput("A. Accountant", "Chartered Accountants Ireland", "12345", new DateOnly(2026, 11, 18), null, null), Owner);

        var profile = await new FilingReadinessProfileService(db).GetProfileAsync(p.CompanyId, p.Id);

        Assert.Equal("Optional review recorded: A. Accountant (Chartered Accountants Ireland, 12345)", profile.AccountantReviewState);
        Assert.Contains(profile.RequiredEvidence, e => e.Code == "accountant-review-optional" && !e.Required && e.Satisfied);
        Assert.DoesNotContain(profile.BlockingIssues, i => i.Code.Contains("accountant"));
    }
```

and in `ProductionReadinessReportTests` change the assertion at ~line 76 / ~488 that `named-accountant-approval-record` blocks release to:

```csharp
        var accountant = report.ReleaseChecklist.Single(item => item.EvidenceCode == "named-accountant-approval-record");
        Assert.False(accountant.BlocksRelease);
        Assert.Equal("optional", accountant.Status);
```

(match the real property names in that test).

- [ ] **Step 2: Run** — Expected: FAIL.

- [ ] **Step 3: Implement**

Store the attestation on `CroFilingPackage`'s existing fields with `ApprovalCapacity` untouched: use `ApproverProfessionalBody`, `ApproverMembershipNumber`, `ApproverVerifiedAt` (= review date at 00:00 UTC), `ApproverVerificationArtifact`/`Sha256` (optional letter, PDF-checked if supplied), and add one new nullable column `OptionalReviewerName` (`varchar(200)`) to the Task 2 migration rather than overloading `ApprovedBy`. `OptionalAccountantReviewState(cro)` reads those. `ApplyBoardApproval` (Task 6) must **not** clear `OptionalReviewerName`; regenerating the accounts PDF clears all optional-review fields. Validate `ProfessionalBody` against the existing recognised-bodies list moved from `FilingReadinessProfileService`.

Release catalog: set `qualified-accountant-signoff` status `"optional"`; checklist item `accountant-final-signoff` → status `"optional"` so `BlocksRelease = status != "complete"` must become `BlocksRelease = status is not ("complete" or "optional")`; remove `qualified-accountant-final-signoff` from `release-artifact-pack`'s required manifests. Do not touch `external-ros-validation-reference` or `source-law-change-review-note`.

- [ ] **Step 4: Run** `--filter "FullyQualifiedName~DirectorsApproval|FullyQualifiedName~ProductionReadiness"` — Expected: PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat: optional accountant review attestation; accountant sign-off no longer blocks release"`

---

### Task 9: Update the existing tests that encoded "accountant review is mandatory"

**Files (each change is an intentional semantic update; keep the rest of each test intact):**
- `backend/Accounts.Tests/FilingReadinessProfileTests.cs` (`ReadinessProfile_ForCorePrivateCompany_IsSourceBackedAndRequiresAccountantReview` :14–29, `ReadinessProfile_BuildsAccountantSignOffPacketFromEvidenceAndWorkflowState` :53–63)
- `backend/Accounts.Tests/FilingGoldenCorpusScenarioTests.cs` (:93, :105, :107–110, :149, :157, :198, :200)
- `backend/Accounts.Tests/GoldenCorpusPostgresReleaseTests.cs` (:82)
- `backend/Accounts.Tests/FilingReleaseGateTests.cs` (`AccountantApproval_RejectsExpiredUnqualified…` :237, `PublicFreeFormApproval_FailsClosedWithoutPersistingApprovedStatus` :472, every `BindVerifiedApprovalAsync` call)
- `backend/Accounts.Tests/AccountsWorkflowTests.cs` (:796) and any test that calls `BindVerifiedApprovalAsync`
- `backend/Accounts.Tests/ProductionReadinessReportTests.cs` (:76, :488 — done in Task 8)

- [ ] **Step 1:** Run the full backend suite with Postgres and list failures:
`cd backend && ACCOUNTS_POSTGRES_TEST_CONNECTION=... dotnet test Accounts.Tests/Accounts.Tests.csproj -c Release -p:ArtifactsPath="$TEMP/accts-art" 2>&1 | grep -E "\[FAIL\]"`
Expected: failures limited to the files above plus the three known Windows byte-pinned fixture tests (`ReleaseEvidenceTemplates_…`, `IndependentFixture_IsBytePinned…`, `ImmutableReconciledFixture_…`), which are environment-only.

- [ ] **Step 2:** For each failing test, apply the rule:
  - "requires accountant review" → assert `AccountantReviewRequired == false`, `ApprovalAuthority == "board-of-directors-s324"`, blocker `directors-approval-required` present until a directors' approval exists;
  - `allowedNextActions` contains `approve-cro-pack` → now only after a directors' approval (use `FilingGateFixture.RecordDirectorsApprovalAsync()`); otherwise expect `record-directors-approval`;
  - `BindVerifiedApprovalAsync(...)` success paths → `RecordDirectorsApprovalAsync()` then `BindApprovalAsync(companyId, periodId, workflow, auditUserId)`;
  - `PublicFreeFormApproval_FailsClosed…` → rename to `Approval_WithoutDirectorsApproval_FailsClosedWithoutPersistingApprovedStatus` and assert the s.324 message;
  - credential-expiry test → move to the optional attestation (expired credential rejected by `/accountant-review` when a letter or verification is supplied), or delete if Task 8 does not validate expiry, noting the deletion in the commit.
  - Golden-corpus micro/DAC/CLG expectations: replace `"accountant-review"` evidence expectations with `"directors-approval"`.

- [ ] **Step 3:** Re-run the full suite. Expected: only the three environment-only fixture failures.

- [ ] **Step 4: Commit** — `git commit -am "test: filing tests assert the directors' approval gate"`

---

### Task 10: Postgres tests — RLS isolation and migration

**Files:**
- Create: `backend/Accounts.Tests/DirectorsApprovalPostgresTests.cs`

- [ ] **Step 1: Write the test** using the harness pattern from `DatabaseTenantIsolationPostgresTests.cs` (create role + schema, migrate, provision, then connect as the application role with `TenantRlsConnectionInterceptor`):

```csharp
    [PostgresFact]
    public async Task DirectorsApprovalRows_AreInvisibleToAnotherTenant()
    {
        // setup as admin: two tenants, a company + period each, one DirectorsApprovalPage for tenant A's period
        // (compute RecordSha256 with DirectorsApprovalIntegrity.ComputePageHash)
        // act as application role with tenant B's context:
        Assert.Equal(0, await appDb.DirectorsApprovalPages.CountAsync());
        // and with tenant A's context:
        Assert.Equal(1, await appDbA.DirectorsApprovalPages.CountAsync());
    }

    [PostgresFact]
    public async Task DirectorsApprovalCheckConstraints_RejectAMalformedRow()
    {
        // raw SQL insert as admin of an Approved event with NULL "SignedPageSha256" → PostgresException 23514 (check_violation)
    }
```

Fill in the setup by copying the exact statements from `DefectiveRawAndIgnoreFilterQueriesCannotCrossTenantOrForgeContext` (`DatabaseTenantIsolationPostgresTests.cs:205–294`), substituting the new entity.

- [ ] **Step 2: Run** with `ACCOUNTS_POSTGRES_TEST_CONNECTION` set — Expected: PASS.

- [ ] **Step 3: Commit** — `git commit -am "test: RLS and constraints for directors' approval tables"`

---

### Task 11: OpenAPI and frontend contracts/client

**Files:**
- Regenerate: `backend/Accounts.Api/OpenApi/accounts-api-v1.json`, `frontend/src/lib/generated/accounts-api-v1.ts`
- Modify: `frontend/src/lib/apiContracts.ts` (`filingReadinessProfileSchema` ~1640, `signOffPacketSchema` ~1612; new schemas)
- Modify: `frontend/src/lib/api.ts` (types + calls)
- Test: `frontend/tests/api-runtime-contracts.test.mjs` (fixture ~806–835, test ~368–371)

**Interfaces:**
- Produces (api.ts): `getDirectorsApproval(companyId, periodId, onDate?)`, `generateDirectorsApprovalPage(companyId, periodId, input)`, `directorsApprovalPageUrl(companyId, periodId, pageId)`, `recordDirectorsApproval(companyId, periodId, input)`, `recordCroSigningPack(companyId, periodId, input)`, `recordAuditorReport(companyId, periodId, input)`, `recordAccountantReview(companyId, periodId, input)`; types `DirectorsApprovalState`, `DirectorsApprovalPageSummary`, `DirectorsApprovalSummary`, `DirectorOption`.

- [ ] **Step 1: Failing contract test** — replace the assertion that `accountantReviewRequired: false` is rejected with:

```js
test("filing readiness profile: accountant review is never mandatory and the board approves", () => {
  const profile = filingReadinessProfileFixture();
  assert.equal(parse(filingReadinessProfileSchema, profile).approvalAuthority, "board-of-directors-s324");
  assert.throws(() => parse(filingReadinessProfileSchema, { ...profile, accountantReviewRequired: true }), /accountantReviewRequired/);
  const missing = structuredClone(profile);
  delete missing.approvalAuthority;
  assert.throws(() => parse(filingReadinessProfileSchema, missing), /approvalAuthority/);
});
```

and update the fixture: `accountantReviewRequired: false`, `accountantReviewState: "Not required by law — not recorded"`, `approvalAuthority: "board-of-directors-s324"`, packet `readyForDirectorsApproval`, state `ready-for-directors-approval`.

- [ ] **Step 2: Run** `cd frontend && node --test --experimental-strip-types tests/api-runtime-contracts.test.mjs` — Expected: FAIL.

- [ ] **Step 3: Implement schemas**

```ts
  accountantReviewRequired: z.literal(false),
  accountantReviewState: z.string().trim().min(1),
  approvalAuthority: z.literal("board-of-directors-s324"),
```

remove the superRefine branch "Qualified-accountant review must remain mandatory"; in `signOffPacketSchema` rename the state enum value and `readyForAccountantApproval` → `readyForDirectorsApproval`. Add:

```ts
const sha256 = z.string().regex(/^[a-f0-9]{64}$/i);
export const directorOptionSchema = z.object({ officerId: positiveIdSchema, name: z.string().trim().min(1) });
export const directorsApprovalPageSummarySchema = z.object({
  id: positiveIdSchema,
  approvalDate: isoDateSchema,
  firstDirectorName: z.string().trim().min(1),
  secondDirectorName: z.string().trim().min(1).nullable(),
  directorsInOfficeCount: z.number().int().positive(),
  resolutionReference: z.string().trim().min(1),
  pageSha256: sha256,
  accountsPdfSha256: sha256,
  recordSha256: sha256,
  generatedAtUtc: isoDateTimeSchema,
}).superRefine((page, context) => {
  if ((page.secondDirectorName === null) !== (page.directorsInOfficeCount === 1)) {
    addIssue(context, ["secondDirectorName"], "A sole-director page has one signatory; otherwise two");
  }
});
export const directorsApprovalSummarySchema = z.object({
  eventId: positiveIdSchema,
  page: directorsApprovalPageSummarySchema,
  signedPageSha256: sha256,
  resolutionSha256: sha256,
  recordedByDisplayName: z.string().trim().min(1),
  recordedAtUtc: isoDateTimeSchema,
  recordSha256: sha256,
});
export const directorsApprovalStateSchema = z.object({
  eligibleDirectors: z.array(directorOptionSchema),
  latestPage: directorsApprovalPageSummarySchema.nullable(),
  current: directorsApprovalSummarySchema.nullable(),
});
```

(The backend writes these nullable members explicitly via `JsonIgnoreCondition.Never`, Task 5.) Add the api.ts calls with `responseSchema`; uploads send base64 (`btoa` over the file bytes, computing SHA-256 with `crypto.subtle.digest("SHA-256", buffer)`). Regenerate the contract: backend build with `-p:OpenApiGenerateDocumentsOnBuild=true`, then `npm run generate:api-contract`, then `npm run test:api-contract`.

- [ ] **Step 4: Run** `npx tsc --noEmit --incremental false && node --test --experimental-strip-types tests/api-runtime-contracts.test.mjs` — Expected: PASS (fix every consumer of `readyForAccountantApproval` the compiler reports: `FilingReviewCentre.tsx`, `PeriodWorkbenchOverview.tsx`).

- [ ] **Step 5: Commit** — `git commit -am "feat: frontend contracts for the directors' approval"`

---

### Task 12: Filing tab UI — directors' approval, CRO signing pack, auditor report, optional accountant review

**Files:**
- Create: `frontend/src/components/period/DirectorsApprovalPanel.tsx`
- Create: `frontend/src/components/period/FilingEvidencePanels.tsx` (CRO signing pack, auditor report, optional accountant review)
- Modify: `frontend/src/components/period/PeriodFilingWorkspace.tsx` (render above `FilingReviewCentre`, ~line 133)
- Modify: `frontend/src/components/period/FilingReviewCentre.tsx` (labels ~70, ~87, ~357, ~556, ~654, ~670)
- Modify: `frontend/src/components/period/PeriodWorkbenchOverview.tsx` (gate snapshot ~232)
- Test: `frontend/tests/render/directors-approval-panel.test.tsx`

- [ ] **Step 1: Failing render tests**

```tsx
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach } from "vitest";

const api = vi.hoisted(() => ({
  getDirectorsApproval: vi.fn(),
  generateDirectorsApprovalPage: vi.fn(),
  recordDirectorsApproval: vi.fn(),
}));
vi.mock("@/lib/api", async (original) => ({ ...(await original<typeof import("@/lib/api")>()), ...api }));

import { DirectorsApprovalPanel } from "@/components/period/DirectorsApprovalPanel";

const page = {
  id: 4, approvalDate: "2026-11-20", firstDirectorName: "Director One", secondDirectorName: "Director Two",
  directorsInOfficeCount: 3, resolutionReference: "WR-2026-11-20", pageSha256: "c".repeat(64),
  accountsPdfSha256: "a".repeat(64), recordSha256: "d".repeat(64), generatedAtUtc: "2026-11-20T10:00:00Z",
};

describe("DirectorsApprovalPanel", () => {
  beforeEach(() => Object.values(api).forEach((m) => m.mockReset()));

  it("offers two director selects when two or more directors are in office", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: [{ officerId: 1, name: "Director One" }, { officerId: 2, name: "Director Two" }, { officerId: 3, name: "Director Three" }],
      latestPage: null, current: null,
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    expect(await screen.findByLabelText("First director")).toBeInTheDocument();
    expect(screen.getByLabelText("Second director")).toBeInTheDocument();
    expect(screen.getByText(/Companies Act 2014, s\.324/)).toBeInTheDocument();
  });

  it("offers one select for a sole director", async () => {
    api.getDirectorsApproval.mockResolvedValue({ eligibleDirectors: [{ officerId: 1, name: "Director One" }], latestPage: null, current: null });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    expect(await screen.findByLabelText("Sole director")).toBeInTheDocument();
    expect(screen.queryByLabelText("Second director")).not.toBeInTheDocument();
  });

  it("shows the recorded approval with its record hash", async () => {
    api.getDirectorsApproval.mockResolvedValue({
      eligibleDirectors: [], latestPage: page,
      current: { eventId: 7, page, signedPageSha256: "e".repeat(64), resolutionSha256: "f".repeat(64), recordedByDisplayName: "Firm Owner", recordedAtUtc: "2026-11-21T09:00:00Z", recordSha256: "9".repeat(64) },
    });
    render(<DirectorsApprovalPanel companyId={1} periodId={9} canRecord />);
    const recorded = await screen.findByRole("region", { name: "Recorded directors' approval" });
    expect(within(recorded).getByText(/Director One and Director Two/)).toBeInTheDocument();
    expect(within(recorded).getByText(/999999999999/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run** `cd frontend && npm run test:render -- tests/render/directors-approval-panel.test.tsx` — Expected: FAIL (component missing).

- [ ] **Step 3: Implement `DirectorsApprovalPanel`** following `CompanyAnnualReturnDatePanel.tsx` (HeroUI `Card`, `ReviewPanel`, toast, re-authentication handled by the API client's existing 401/`recent-authentication-required` flow — check how `CompanyAnnualReturnDatePanel` or mark-filed triggers re-auth and reuse it). Two steps:

  1. Form: approval date (`<input type="date" aria-label="Approval date">`), director selects (labels "First director"/"Second director", or "Sole director" when `eligibleDirectors.length === 1`), reference (`aria-label="Minute or written-resolution reference"`), button "Generate approval page" → `generateDirectorsApprovalPage`, then show "Download approval page" link (`directorsApprovalPageUrl`).
  2. Uploads: "Signed approval page (PDF)" and "Minute or written resolution (PDF)" file inputs; "Record directors' approval" button (shown only when `canRecord`) → `recordDirectorsApproval`.
  3. When `current` exists: `<section aria-label="Recorded directors' approval">` with date, signatories joined by " and ", reference, recorded by/at, and short hashes (first 12 chars) of page, signed page, resolution and record.

  Reload `getDirectorsApproval(companyId, periodId, approvalDate)` when the date changes so the eligible directors reflect that date.

- [ ] **Step 4: Implement `FilingEvidencePanels.tsx`**: `CroSigningPackPanel` (director select + secretary select from officers, signed date, PDF upload → `recordCroSigningPack`), `AuditorReportPanel` (shown only when the readiness profile's `requiredEvidence` includes an unsatisfied `audit-report`), `OptionalAccountantReviewPanel` (collapsed by default; heading "Optional: reviewed by an accountant"; copy: "Irish law does not require an accountant. Record a review here only if one was done.").

- [ ] **Step 5: Wire and relabel**: render the three panels and `DirectorsApprovalPanel` in `PeriodFilingWorkspace` above `FilingReviewCentre`, passing `canRecord` from `useAuth()` (Owner/Reviewer). In `FilingReviewCentre` and `PeriodWorkbenchOverview` replace "Accountant review" tiles with "Directors' approval" (value: "Recorded" when the `directors-approval` evidence is satisfied, else "Required") plus "Accountant review: Optional"; replace copy "Named accountant review…"/"Record the named accountant approval…" with directors' approval wording. Update the existing render tests that assert the old copy (`tests/render/period-workbench-overview.test.tsx:47-51,276-277`, `tests/render/filing-review-centre.test.tsx:602-603`).

- [ ] **Step 6: Run** `npm run lint && npx tsc --noEmit --incremental false && npm test` — Expected: PASS.

- [ ] **Step 7: Commit** — `git commit -am "feat: filing tab records the directors' s.324 approval and signing evidence"`

---

### Task 13: End-to-end verification on the Development stack

- [ ] **Step 1:** `docker compose up -d --build api frontend`; sign in as the demo admin (credentials in `LOCAL_SETUP.md`, TOTP from the enrolment secret).
- [ ] **Step 2:** On a Draft CLG period with officers: generate the CRO accounts PDF and signature page; record the CRO signing pack (director + secretary, any PDF); open the Filing tab; generate the s.324 page with two directors; upload a different PDF as "signed" and another as the minute; record. Confirm the readiness profile shows `directors-approval` satisfied, `accountantReviewRequired: false`, and `approve-cro-pack` allowed.
- [ ] **Step 3:** Approve the CRO pack; confirm `cro.ApprovalCapacity = board-of-directors-s324` and a 64-hex manifest. Regenerate the accounts PDF; confirm the approval is revoked and `approve-cro-pack` disappears.
- [ ] **Step 4:** Run the full backend suite (Postgres) and full frontend suite; record results in the PR description. Expected: only the three Windows byte-pinned fixture tests fail locally; CI green.
- [ ] **Step 5:** Open the PR (`gh pr create --base main --head feat/directors-s324-approval`), with commits replayed as one GitHub-signed commit if needed (see Global Constraints).
