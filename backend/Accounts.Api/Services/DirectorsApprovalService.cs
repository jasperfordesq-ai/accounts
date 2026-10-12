using Accounts.Api.Data;
using Accounts.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Accounts.Api.Services;

public sealed record DirectorsApprovalPageInput(DateOnly ApprovalDate, int FirstDirectorOfficerId, int? SecondDirectorOfficerId, string ResolutionReference);
public sealed record DirectorsApprovalRecordInput(int ApprovalPageId, byte[] SignedPageArtifact, string SignedPageSha256, byte[] ResolutionArtifact, string ResolutionSha256);
public sealed record DirectorsApprovalPageContent(DateOnly ApprovalDate, string FirstDirectorName, string? SecondDirectorName, string ResolutionReference, string AccountsPdfSha256);
public sealed record CurrentDirectorsApproval(DirectorsApprovalPage Page, DirectorsApprovalEvent Approval);
/// <summary>Reason is one of <see cref="DirectorsApprovalService.WithdrawalReasons"/>; the note explains it (3 to 500 characters).</summary>
public sealed record DirectorsApprovalWithdrawalInput(string Reason, string Note);
public sealed record DirectorsApprovalHistoryEntry(DirectorsApprovalEvent Event, DirectorsApprovalPage Page);

/// <summary>An optional "reviewed by an accountant" attestation. Recorded and shown in readiness; never a gate.</summary>
public sealed record AccountantReviewAttestationInput(
    string ReviewerName,
    string ProfessionalBody,
    string MembershipNumber,
    DateOnly ReviewDate,
    byte[]? ReviewLetter,
    string? ReviewLetterSha256);

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
    private static readonly TimeZoneInfo Ireland = DeadlineService.ResolveIrelandTimeZone();
    private readonly TimeProvider time = clock ?? TimeProvider.System;

    /// <summary>Why a recorded approval is withdrawn; stored on the Revoked event as "withdrawn-" + reason.</summary>
    public static readonly IReadOnlyList<string> WithdrawalReasons = ["entered-in-error", "board-rescinded", "superseded"];

    /// <summary>The approval page prints each signatory's name; longer names are refused rather than truncated.</summary>
    public const int MaxSignatoryNameLength = 200;

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
        ValidateApprovalDate(period, input.ApprovalDate);
        RequirePeriodApprovalDate(period, input.ApprovalDate);
        var (first, second, inOffice) = await ResolveSignatoriesAsync(companyId, input.ApprovalDate, input.FirstDirectorOfficerId, input.SecondDirectorOfficerId);
        foreach (var director in second is null ? new[] { first } : new[] { first, second })
        {
            if (director.Name.Trim().Length > MaxSignatoryNameLength)
                throw new BusinessRuleException($"A director's name is longer than {MaxSignatoryNameLength} characters; shorten it on the company's officer record before generating the approval page.");
        }

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
        if (!IsOwnerOrReviewer(actor))
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
        ValidateApprovalDate(period, page.ApprovalDate);
        RequirePeriodApprovalDate(period, page.ApprovalDate);
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
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The previous-hash / genesis unique guards fired: a concurrent approval won the race.
            throw new BusinessRuleException("Another directors' approval was recorded for this period at the same time; reload and try again.");
        }
        await audit.LogAsync(companyId, periodId, nameof(DirectorsApprovalEvent), approval.Id,
            AuditEventCodes.DirectorsApprovalRecorded, null,
            new { page.ApprovalDate, page.FirstDirectorOfficerId, page.SecondDirectorOfficerId, page.ResolutionReference, approval.ApprovalPageSha256, approval.SignedPageSha256, approval.ResolutionSha256, approval.RecordSha256 },
            AuthenticatedIdentity.AuditUserId(actor));
        return approval;
    }

    /// <summary>
    /// Withdraws the current approval (for example one recorded in error) without deleting anything:
    /// a Revoked event attributed to the actor is chained after it, and every filing-package approval
    /// bound to the board is revoked in the same unit of work. The note is kept in the audit trail.
    /// </summary>
    public async Task<DirectorsApprovalEvent> WithdrawAsync(int companyId, int periodId, DirectorsApprovalWithdrawalInput input, AuthenticatedUser actor)
    {
        if (!IsOwnerOrReviewer(actor))
            throw new BusinessRuleException("Only an Owner or Reviewer can withdraw the directors' approval.");
        var reason = input.Reason ?? string.Empty;
        if (!WithdrawalReasons.Contains(reason, StringComparer.Ordinal))
            throw new BusinessRuleException("Choose why the approval is being withdrawn.");
        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length is < 3 or > 500)
            throw new BusinessRuleException("Explain why the approval is being withdrawn (3 to 500 characters).");

        var period = await db.AccountingPeriods
            .Include(p => p.CroFilingPackage)
            .Include(p => p.RevenueFilingPackage)
            .Include(p => p.CharityFilingPackage)
            .SingleOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");
        var events = await db.DirectorsApprovalEvents.Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var pages = await db.DirectorsApprovalPages.Where(p => p.PeriodId == periodId).ToListAsync();
        var current = ResolveCurrent(events, pages, period.CroFilingPackage)
            ?? throw new BusinessRuleException("There is no current directors' approval to withdraw.");

        var revokedFilings = FilingReleaseGate.RevokeApprovalsBoundToWithdrawnBoardApproval(period);
        var revoked = new DirectorsApprovalEvent
        {
            PeriodId = periodId,
            Kind = DirectorsApprovalEventKind.Revoked,
            ApprovalPageId = current.Approval.ApprovalPageId,
            ApprovalPageSha256 = current.Approval.ApprovalPageSha256,
            RevocationReason = "withdrawn-" + reason,
            RecordedByUserId = AuthenticatedIdentity.AuditUserId(actor),
            RecordedByDisplayName = AuthenticatedIdentity.ReviewerDisplayName(actor),
            RecordedAtUtc = Truncate(time.GetUtcNow().UtcDateTime),
            PreviousRecordSha256 = current.Approval.RecordSha256,
            RecordSha256 = string.Empty
        };
        revoked.RecordSha256 = DirectorsApprovalIntegrity.ComputeEventHash(revoked);
        db.DirectorsApprovalEvents.Add(revoked);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // The previous-hash unique guard fired: another approval change won the race.
            throw new BusinessRuleException("Another change to the directors' approval was recorded for this period at the same time; reload and try again.");
        }
        await audit.LogAsync(companyId, periodId, nameof(DirectorsApprovalEvent), revoked.Id,
            AuditEventCodes.DirectorsApprovalRevoked,
            new { ApprovalEventId = current.Approval.Id, current.Page.ApprovalDate, current.Page.ResolutionReference, ApprovalRecordSha256 = current.Approval.RecordSha256 },
            new { Reason = reason, Note = note, revoked.RevocationReason, revoked.ApprovalPageId, revoked.ApprovalPageSha256, revoked.RecordSha256, RevokedFilingApprovals = revokedFilings },
            AuthenticatedIdentity.AuditUserId(actor));
        return revoked;
    }

    /// <summary>
    /// Records the optional accountant review attestation on the period's CRO package. Irish law does
    /// not require an accountant, so nothing depends on it: no blocker, manifest entry or release gate.
    /// It is cleared when the accounts PDF is regenerated, so it refers to the accounts actually filed.
    /// </summary>
    public async Task<CroFilingPackage> RecordAccountantReviewAsync(int companyId, int periodId, AccountantReviewAttestationInput input, AuthenticatedUser actor)
    {
        if (actor.Role is not ("Owner" or "Accountant" or "Reviewer"))
            throw new BusinessRuleException("Only an Owner, Accountant or Reviewer can record an accountant review.");
        var period = await LoadPeriodAsync(companyId, periodId);
        var cro = RequireAccounts(period);

        var name = (input.ReviewerName ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200)
            throw new BusinessRuleException("Enter the reviewer name (2 to 200 characters).");
        var body = (input.ProfessionalBody ?? string.Empty).Trim();
        if (!AccountantCredentialChecks.IsRecognisedProfessionalBody(body))
            throw new BusinessRuleException("Choose a recognised professional body for the accountant review.");
        var membership = (input.MembershipNumber ?? string.Empty).Trim();
        if (membership.Length is 0 or > 100)
            throw new BusinessRuleException("Enter the reviewer's membership number (up to 100 characters).");
        if (input.ReviewDate < period.PeriodEnd)
            throw new BusinessRuleException($"The review date must be on or after the period end ({period.PeriodEnd:yyyy-MM-dd}).");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, Ireland));
        if (input.ReviewDate > today)
            throw new BusinessRuleException("The review date cannot be in the future.");
        if (input.ReviewLetter is not null || input.ReviewLetterSha256 is not null)
            RequirePdf(input.ReviewLetter, input.ReviewLetterSha256, "accountant review letter");

        cro.OptionalReviewerName = name;
        cro.ApproverProfessionalBody = body;
        cro.ApproverMembershipNumber = membership;
        cro.ApproverVerifiedAt = new DateTime(input.ReviewDate.Year, input.ReviewDate.Month, input.ReviewDate.Day, 0, 0, 0, DateTimeKind.Utc);
        cro.ApproverVerificationArtifact = input.ReviewLetter?.ToArray();
        cro.ApproverVerificationArtifactSha256 = input.ReviewLetter is null ? null : input.ReviewLetterSha256!.ToLowerInvariant();
        await db.SaveChangesAsync();
        await audit.LogAsync(companyId, periodId, nameof(CroFilingPackage), cro.Id,
            AuditEventCodes.AccountantReviewAttestationRecorded, null,
            new { ReviewerName = name, ProfessionalBody = body, MembershipNumber = membership, input.ReviewDate, ReviewLetterSha256 = cro.ApproverVerificationArtifactSha256 },
            AuthenticatedIdentity.AuditUserId(actor));
        return cro;
    }

    public async Task<CurrentDirectorsApproval?> GetCurrentApprovalAsync(int periodId)
    {
        var cro = await db.CroFilingPackages.AsNoTracking().SingleOrDefaultAsync(c => c.PeriodId == periodId);
        var events = await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var pages = await db.DirectorsApprovalPages.AsNoTracking().Where(p => p.PeriodId == periodId).ToListAsync();
        return ResolveCurrent(events, pages, cro);
    }

    /// <summary>
    /// Every approval and revocation for the period, newest first, when the whole hash chain verifies;
    /// nothing when it does not (a broken chain proves nothing about any event). Events whose page
    /// record does not verify are left out.
    /// </summary>
    public async Task<IReadOnlyList<DirectorsApprovalHistoryEntry>> GetVerifiedHistoryAsync(int periodId)
    {
        var events = await db.DirectorsApprovalEvents.AsNoTracking().Where(e => e.PeriodId == periodId).OrderBy(e => e.Id).ToListAsync();
        var pages = await db.DirectorsApprovalPages.AsNoTracking().Where(p => p.PeriodId == periodId).ToListAsync();
        if (!ChainVerifies(events))
            return [];
        return events
            .OrderByDescending(e => e.Id)
            .Select(e => (Event: e, Page: pages.SingleOrDefault(p => p.Id == e.ApprovalPageId)))
            .Where(x => x.Page is not null
                && DirectorsApprovalIntegrity.IsValid(x.Page)
                && string.Equals(x.Page.PageSha256, x.Event.ApprovalPageSha256, StringComparison.OrdinalIgnoreCase))
            .Select(x => new DirectorsApprovalHistoryEntry(x.Event, x.Page!))
            .ToList();
    }

    /// <summary>The latest Approved event, not followed by a revocation, whose page and event hashes verify and whose page binds the current accounts.</summary>
    public static CurrentDirectorsApproval? ResolveCurrent(
        IReadOnlyList<DirectorsApprovalEvent> events,
        IReadOnlyList<DirectorsApprovalPage> pages,
        CroFilingPackage? cro)
    {
        if (cro?.AccountsPdfSha256 is null || events.Count == 0)
            return null;
        if (!ChainVerifies(events))
            return null; // a broken chain invalidates every later approval
        var last = events.OrderBy(e => e.Id).Last();
        if (last.Kind != DirectorsApprovalEventKind.Approved)
            return null;
        var page = pages.SingleOrDefault(p => p.Id == last.ApprovalPageId);
        if (page is null
            || !DirectorsApprovalIntegrity.IsValid(page)
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(page.PageArtifact), page.PageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.PageSha256, last.ApprovalPageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.AccountsPdfSha256, cro.AccountsPdfSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(page.ArtifactSourceFingerprintSha256, cro.ArtifactSourceFingerprintSha256, StringComparison.OrdinalIgnoreCase)
            || last.SignedPageArtifact is null || last.ResolutionArtifact is null
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(last.SignedPageArtifact), last.SignedPageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(DirectorsApprovalIntegrity.Sha256(last.ResolutionArtifact), last.ResolutionSha256, StringComparison.OrdinalIgnoreCase))
            return null;
        return new CurrentDirectorsApproval(page, last);
    }

    /// <summary>True when the latest event is an approval of exactly this accounts PDF (by the SHA-256 its page bound).</summary>
    public bool IsApprovedAccountsPdf(int periodId, string accountsPdfSha256)
    {
        var last = db.DirectorsApprovalEvents.Where(e => e.PeriodId == periodId).OrderByDescending(e => e.Id).FirstOrDefault();
        if (last is null || last.Kind != DirectorsApprovalEventKind.Approved)
            return false;
        var page = db.DirectorsApprovalPages.SingleOrDefault(p => p.Id == last.ApprovalPageId);
        return page is not null
            && string.Equals(page.AccountsPdfSha256, accountsPdfSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds a Revoked event for a current approval and returns it (null when there is nothing to
    /// revoke); the caller saves in its own unit of work.
    /// </summary>
    public DirectorsApprovalEvent? AppendRevocationIfCurrent(AccountingPeriod period, CroFilingPackage cro, string reason)
    {
        var events = db.DirectorsApprovalEvents.Where(e => e.PeriodId == period.Id).OrderBy(e => e.Id).ToList();
        if (events.Count == 0 || events.Last().Kind != DirectorsApprovalEventKind.Approved)
            return null;
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
        return revoked;
    }

    private static bool ChainVerifies(IEnumerable<DirectorsApprovalEvent> events)
    {
        string? previous = null;
        foreach (var e in events.OrderBy(e => e.Id))
        {
            if (!DirectorsApprovalIntegrity.IsValid(e) || e.PreviousRecordSha256 != previous)
                return false;
            previous = e.RecordSha256;
        }
        return true;
    }

    // Case-insensitive, as RoleAuthorizationService compares roles.
    private static bool IsOwnerOrReviewer(AuthenticatedUser actor) =>
        string.Equals(actor.Role, "Owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(actor.Role, "Reviewer", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// The board date is recorded when the period is finalised and printed in the accounts, which are
    /// generated afterwards, so it is not compared with the PDF's generation time: the approval page
    /// binds the exact accounts PDF by SHA-256 instead.
    /// </summary>
    private void ValidateApprovalDate(AccountingPeriod period, DateOnly date)
    {
        if (date < period.PeriodEnd)
            throw new BusinessRuleException($"The board approval date must be on or after the period end ({period.PeriodEnd:yyyy-MM-dd}).");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(time.GetUtcNow().UtcDateTime, Ireland));
        if (date > today)
            throw new BusinessRuleException("The board approval date cannot be in the future.");
    }

    /// <summary>
    /// The period's approval date is an input to the accounts (signature page, approval note, source
    /// fingerprint). This service never sets it; the approval page must agree with it.
    /// </summary>
    private static void RequirePeriodApprovalDate(AccountingPeriod period, DateOnly date)
    {
        if (period.ApprovalDate is null)
            throw new BusinessRuleException("Record the board approval date on the period and regenerate the accounts before preparing the directors' approval.");
        if (period.ApprovalDate != date)
            throw new BusinessRuleException($"The approval date must match the board approval date stated in the accounts ({period.ApprovalDate:yyyy-MM-dd}), which was recorded when the period was finalised.");
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

    /// <summary>PostgreSQL timestamps hold microseconds; truncate so hashed values round-trip.</summary>
    private static DateTime Truncate(DateTime utc) =>
        new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
}
