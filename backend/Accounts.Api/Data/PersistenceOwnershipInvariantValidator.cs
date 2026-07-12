using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Data;

internal static class PersistenceOwnershipInvariantValidator
{
    public static async Task ValidateAsync(
        AccountsDbContext db,
        int? currentTenantId,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.DetectChanges();
        if (!db.ChangeTracker.HasChanges())
            return;

        ValidateImmutableOwnershipAnchors(db);
        var resolver = new OwnershipResolver(db, cancellationToken);

        foreach (var user in Changed<UserAccount>(db))
            RequireCurrentTenant(currentTenantId, user.TenantId, nameof(UserAccount));

        foreach (var access in Changed<UserCompanyAccess>(db))
        {
            var user = await resolver.UserAsync(access.UserId, access.User);
            var company = await resolver.CompanyAsync(access.CompanyId, access.Company);
            Require(user is not null && company is not null && user.TenantId == company.TenantId, nameof(UserCompanyAccess));
            RequireCurrentTenant(currentTenantId, user!.TenantId, nameof(UserCompanyAccess));
        }

        foreach (var token in Changed<UserActionToken>(db))
        {
            await ValidateUserOwnedAsync(token.UserId, token.User, token.TenantId, nameof(UserActionToken), resolver, currentTenantId);
            if (token.CreatedByActorKind == IdentityActorKinds.User && token.CreatedByUserId is int creatorId)
            {
                var creator = await resolver.UserAsync(creatorId);
                Require(creator is not null && creator.TenantId == token.TenantId, nameof(UserActionToken));
            }
            else
            {
                Require(token.CreatedByActorKind == IdentityActorKinds.PrivateServerHostOperator
                    && token.CreatedByUserId is null
                    && token.Purpose == UserActionPurposes.PasswordReset,
                    nameof(UserActionToken));
            }
        }
        foreach (var credential in Changed<UserMfaCredential>(db))
            await ValidateUserOwnedAsync(credential.UserId, credential.User, credential.TenantId, nameof(UserMfaCredential), resolver, currentTenantId);
        foreach (var code in Changed<UserMfaRecoveryCode>(db))
            await ValidateUserOwnedAsync(code.UserId, code.User, code.TenantId, nameof(UserMfaRecoveryCode), resolver, currentTenantId);
        foreach (var challenge in Changed<UserMfaChallenge>(db))
            await ValidateUserOwnedAsync(challenge.UserId, challenge.User, challenge.TenantId, nameof(UserMfaChallenge), resolver, currentTenantId);
        foreach (var evidence in Changed<UserLifecycleEvent>(db))
        {
            var target = await resolver.UserAsync(evidence.TargetUserId);
            Require(target is not null && target.TenantId == evidence.TenantId, nameof(UserLifecycleEvent));
            if (evidence.ActorKind == IdentityActorKinds.User && evidence.ActorUserId is int actorId)
            {
                var actor = await resolver.UserAsync(actorId);
                Require(actor is not null && actor.TenantId == evidence.TenantId, nameof(UserLifecycleEvent));
            }
            else
            {
                Require(evidence.ActorKind == IdentityActorKinds.PrivateServerHostOperator
                    && evidence.ActorUserId is null
                    && evidence.EventType is UserLifecycleEventTypes.PrivateOwnerRecoveryStarted or UserLifecycleEventTypes.PasswordResetCompleted,
                    nameof(UserLifecycleEvent));
            }
            RequireCurrentTenant(currentTenantId, evidence.TenantId, nameof(UserLifecycleEvent));
        }

        foreach (var company in Changed<Company>(db))
            RequireCurrentTenant(currentTenantId, company.TenantId, nameof(Company));

        foreach (var period in Changed<AccountingPeriod>(db))
        {
            var company = await resolver.CompanyAsync(period.CompanyId, period.Company);
            Require(company, nameof(AccountingPeriod));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(AccountingPeriod));
        }

        foreach (var bank in Changed<BankAccount>(db))
        {
            var company = await resolver.CompanyAsync(bank.CompanyId, bank.Company);
            Require(company, nameof(BankAccount));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(BankAccount));
        }

        foreach (var category in Changed<AccountCategory>(db))
            await ValidateCategoryAsync(category, resolver, currentTenantId);

        foreach (var batch in Changed<ImportBatch>(db))
        {
            var bank = await resolver.BankAsync(batch.BankAccountId, batch.BankAccount);
            Require(bank, nameof(ImportBatch));
            RequireCurrentTenant(currentTenantId, bank!.TenantId, nameof(ImportBatch));
        }

        foreach (var transaction in Changed<ImportedTransaction>(db))
            await ValidateTransactionAsync(transaction, resolver, currentTenantId);

        foreach (var rule in Changed<TransactionRule>(db))
        {
            var company = await resolver.CompanyAsync(rule.CompanyId, rule.Company);
            var category = await resolver.CategoryAsync(rule.CategoryId, rule.Category);
            Require(company, nameof(TransactionRule));
            Require(CategoryAvailable(category, company!.CompanyId), nameof(TransactionRule));
            RequireCurrentTenant(currentTenantId, company.TenantId, nameof(TransactionRule));
        }

        foreach (var package in Changed<CroFilingPackage>(db))
            await ValidatePeriodOwnedAsync(package.PeriodId, package.Period, nameof(CroFilingPackage), resolver, currentTenantId);
        foreach (var package in Changed<RevenueFilingPackage>(db))
            await ValidatePeriodOwnedAsync(package.PeriodId, package.Period, nameof(RevenueFilingPackage), resolver, currentTenantId);
        foreach (var package in Changed<CharityFilingPackage>(db))
            await ValidatePeriodOwnedAsync(package.PeriodId, package.Period, nameof(CharityFilingPackage), resolver, currentTenantId);
        foreach (var authority in Changed<FilingAuthorityEngagement>(db))
        {
            var company = await resolver.CompanyAsync(authority.CompanyId, authority.Company);
            Require(company is not null && company.TenantId == authority.TenantId, nameof(FilingAuthorityEngagement));
            RequireCurrentTenant(currentTenantId, authority.TenantId, nameof(FilingAuthorityEngagement));
            if (authority.SupersedesAuthorityId is { } predecessorId)
            {
                var predecessor = await AuthorityScopeAsync(db, predecessorId, cancellationToken);
                Require(predecessor is not null
                    && predecessor.TenantId == authority.TenantId
                    && predecessor.CompanyId == authority.CompanyId
                    && predecessor.Workflow == authority.Workflow
                    && predecessor.Version + 1 == authority.Version,
                    nameof(FilingAuthorityEngagement));
            }
        }
        foreach (var snapshot in Changed<ExternalFilingHandoffSnapshot>(db))
        {
            var company = await resolver.CompanyAsync(snapshot.CompanyId, snapshot.Company);
            var period = await resolver.PeriodAsync(snapshot.PeriodId, snapshot.Period);
            var authority = await AuthorityScopeAsync(db, snapshot.AuthorityId, cancellationToken);
            Require(company is not null
                && company.TenantId == snapshot.TenantId
                && period is not null
                && period.CompanyId == snapshot.CompanyId
                && period.TenantId == snapshot.TenantId
                && authority is not null
                && authority.TenantId == snapshot.TenantId
                && authority.CompanyId == snapshot.CompanyId
                && authority.Workflow == snapshot.Workflow
                && string.Equals(authority.EvidenceSha256, snapshot.AuthorityEvidenceSha256, StringComparison.OrdinalIgnoreCase),
                nameof(ExternalFilingHandoffSnapshot));
            RequireCurrentTenant(currentTenantId, snapshot.TenantId, nameof(ExternalFilingHandoffSnapshot));
            if (snapshot.SupersedesSnapshotRecordId is { } predecessorId)
            {
                var predecessor = await SnapshotScopeAsync(db, predecessorId, cancellationToken);
                Require(predecessor is not null
                    && predecessor.TenantId == snapshot.TenantId
                    && predecessor.CompanyId == snapshot.CompanyId
                    && predecessor.PeriodId == snapshot.PeriodId
                    && predecessor.Workflow == snapshot.Workflow
                    && predecessor.Version + 1 == snapshot.Version
                    && predecessor.SnapshotId == snapshot.SupersedesSnapshotId
                    && string.Equals(predecessor.ArtifactSha256, snapshot.SupersedesArtifactSha256, StringComparison.OrdinalIgnoreCase),
                    nameof(ExternalFilingHandoffSnapshot));
            }
        }
        foreach (var outcome in Changed<ExternalFilingOutcomeEvent>(db))
        {
            var company = await resolver.CompanyAsync(outcome.CompanyId, outcome.Company);
            var period = await resolver.PeriodAsync(outcome.PeriodId, outcome.Period);
            var snapshot = await SnapshotScopeAsync(db, outcome.SnapshotRecordId, cancellationToken);
            Require(company is not null
                && company.TenantId == outcome.TenantId
                && period is not null
                && period.CompanyId == outcome.CompanyId
                && period.TenantId == outcome.TenantId
                && snapshot is not null
                && snapshot.TenantId == outcome.TenantId
                && snapshot.CompanyId == outcome.CompanyId
                && snapshot.PeriodId == outcome.PeriodId
                && snapshot.SnapshotId == outcome.SnapshotId
                && string.Equals(snapshot.ArtifactSha256, outcome.SnapshotArtifactSha256, StringComparison.OrdinalIgnoreCase),
                nameof(ExternalFilingOutcomeEvent));
            RequireCurrentTenant(currentTenantId, outcome.TenantId, nameof(ExternalFilingOutcomeEvent));
            if (outcome.SupersedingSnapshotRecordId is { } successorId)
            {
                var successor = await SnapshotScopeAsync(db, successorId, cancellationToken);
                Require(successor is not null
                    && successor.TenantId == outcome.TenantId
                    && successor.CompanyId == outcome.CompanyId
                    && successor.PeriodId == outcome.PeriodId
                    && successor.Workflow == snapshot!.Workflow
                    && successor.Version == snapshot.Version + 1
                    && successor.SnapshotId == outcome.SupersedingSnapshotId
                    && string.Equals(successor.ArtifactSha256, outcome.SupersedingSnapshotArtifactSha256, StringComparison.OrdinalIgnoreCase)
                    && successor.SupersedesSnapshotId == snapshot.SnapshotId
                    && string.Equals(successor.SupersedesArtifactSha256, snapshot.ArtifactSha256, StringComparison.OrdinalIgnoreCase),
                    nameof(ExternalFilingOutcomeEvent));
            }
        }
        foreach (var review in Changed<CorporationTaxScopeReview>(db))
            await ValidatePeriodOwnedAsync(review.PeriodId, review.Period, nameof(CorporationTaxScopeReview), resolver, currentTenantId);
        foreach (var record in Changed<CorporationTaxLossRecord>(db))
            await ValidatePeriodOwnedAsync(record.PeriodId, record.Period, nameof(CorporationTaxLossRecord), resolver, currentTenantId);
        foreach (var review in Changed<CorporationTaxFilingSupportReview>(db))
            await ValidatePeriodOwnedAsync(review.PeriodId, review.Period, nameof(CorporationTaxFilingSupportReview), resolver, currentTenantId);
        foreach (var payment in Changed<CorporationTaxPaymentRecord>(db))
            await ValidatePeriodOwnedAsync(payment.PeriodId, payment.Period, nameof(CorporationTaxPaymentRecord), resolver, currentTenantId);

        foreach (var movement in Changed<DirectorLoanMovement>(db))
        {
            var loan = await resolver.DirectorLoanAsync(movement.DirectorLoanId, movement.DirectorLoan);
            Require(loan, nameof(DirectorLoanMovement));
            RequireCurrentTenant(currentTenantId, loan!.TenantId, nameof(DirectorLoanMovement));
        }

        foreach (var deadline in Changed<FilingDeadline>(db))
        {
            var company = await resolver.CompanyAsync(deadline.CompanyId, deadline.Company);
            var period = await resolver.PeriodAsync(deadline.PeriodId, deadline.Period);
            Require(company is not null && period is not null && company.CompanyId == period.CompanyId, nameof(FilingDeadline));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(FilingDeadline));
        }

        foreach (var reminder in Changed<DeadlineReminderOutbox>(db))
        {
            var company = await resolver.CompanyAsync(reminder.CompanyId, reminder.Company);
            var period = await resolver.PeriodAsync(reminder.PeriodId, reminder.Period);
            var deadline = await DeadlineScopeAsync(db, reminder.FilingDeadlineId, cancellationToken);
            Require(company is not null
                && company.TenantId == reminder.TenantId
                && period is not null
                && period.TenantId == reminder.TenantId
                && period.CompanyId == reminder.CompanyId
                && deadline is not null
                && deadline.CompanyId == reminder.CompanyId
                && deadline.PeriodId == reminder.PeriodId,
                nameof(DeadlineReminderOutbox));
            RequireCurrentTenant(currentTenantId, reminder.TenantId, nameof(DeadlineReminderOutbox));
        }

        foreach (var job in Changed<PlatformJobRun>(db))
            RequireCurrentTenant(currentTenantId, job.TenantId, nameof(PlatformJobRun));

        foreach (var history in Changed<FilingHistory>(db))
        {
            var company = await resolver.CompanyAsync(history.CompanyId, history.Company);
            Require(company, nameof(FilingHistory));
            if (history.PeriodId is { } periodId)
            {
                var period = await resolver.PeriodAsync(periodId, history.Period);
                Require(period is not null && period.CompanyId == company!.CompanyId, nameof(FilingHistory));
            }
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(FilingHistory));
        }

        foreach (var audit in Changed<AuditLog>(db))
            await ValidateAuditLogAsync(audit, resolver, currentTenantId);

        foreach (var checkpoint in Changed<AuditIntegrityCheckpoint>(db))
        {
            var company = await resolver.CompanyAsync(checkpoint.CompanyId);
            var anchoredAudit = await resolver.AuditLogAsync(checkpoint.LastAuditLogId);
            Require(company is not null
                && anchoredAudit is not null
                && anchoredAudit.CompanyId == company.CompanyId
                && string.Equals(anchoredAudit.IntegrityHash, checkpoint.LastIntegrityHash, StringComparison.Ordinal),
                nameof(AuditIntegrityCheckpoint));
            if (checkpoint.TenantId is { } checkpointTenantId)
                Require(company!.TenantId == checkpointTenantId, nameof(AuditIntegrityCheckpoint));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(AuditIntegrityCheckpoint));
        }

        foreach (var evidence in Changed<CompanyQuarantineEvent>(db))
        {
            var company = await resolver.CompanyAsync(evidence.CompanyId);
            Require(company is not null && company.TenantId == evidence.TenantId, nameof(CompanyQuarantineEvent));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(CompanyQuarantineEvent));
        }

        foreach (var onboarding in Changed<CompanyOnboardingRequest>(db))
        {
            RequireCurrentTenant(currentTenantId, onboarding.TenantId, nameof(CompanyOnboardingRequest));
            if (onboarding.CompanyId is not { } companyId)
                continue;

            var company = await resolver.CompanyAsync(companyId);
            var period = onboarding.PeriodId is { } periodId
                ? await resolver.PeriodAsync(periodId)
                : null;
            var bank = onboarding.BankAccountId is { } bankId
                ? await resolver.BankAsync(bankId)
                : null;
            Require(company is not null
                && company.TenantId == onboarding.TenantId
                && period is not null
                && period.CompanyId == company.CompanyId
                && bank is not null
                && bank.CompanyId == company.CompanyId,
                nameof(CompanyOnboardingRequest));
        }

        foreach (var record in Changed<IdempotencyRecord>(db))
            RequireCurrentTenant(currentTenantId, record.TenantId, nameof(IdempotencyRecord));

        foreach (var loginEvent in Changed<LoginSecurityEvent>(db))
        {
            if (loginEvent.UserId is { } userId)
            {
                var user = await resolver.UserAsync(userId);
                Require(user is not null
                    && loginEvent.TenantId is not null
                    && user.TenantId == loginEvent.TenantId,
                    nameof(LoginSecurityEvent));
            }
            else
            {
                Require(loginEvent.TenantId is null, nameof(LoginSecurityEvent));
            }
            if (loginEvent.TenantId is { } tenantId)
                RequireCurrentTenant(currentTenantId, tenantId, nameof(LoginSecurityEvent));
        }

        foreach (var request in Changed<PrivacySubjectRequest>(db))
        {
            var subject = await resolver.UserAsync(request.SubjectUserId);
            var requester = await resolver.UserAsync(request.RequestedByUserId);
            var decider = request.DecidedByUserId is { } deciderId
                ? await resolver.UserAsync(deciderId)
                : null;
            Require(subject is not null
                && requester is not null
                && subject.TenantId == request.TenantId
                && requester.TenantId == request.TenantId
                && (request.DecidedByUserId is null || decider?.TenantId == request.TenantId),
                nameof(PrivacySubjectRequest));
            RequireCurrentTenant(currentTenantId, request.TenantId, nameof(PrivacySubjectRequest));
        }

        foreach (var exercise in Changed<PrivacyIncidentExercise>(db))
        {
            var reviewer = await resolver.UserAsync(exercise.ReviewedByUserId);
            Require(reviewer is not null && reviewer.TenantId == exercise.TenantId, nameof(PrivacyIncidentExercise));
            RequireCurrentTenant(currentTenantId, exercise.TenantId, nameof(PrivacyIncidentExercise));
        }
    }

    private static async Task ValidateCategoryAsync(
        AccountCategory category,
        OwnershipResolver resolver,
        int? currentTenantId)
    {
        CompanyScope? company = null;
        if (category.CompanyId is { } companyId)
        {
            company = await resolver.CompanyAsync(companyId, category.Company);
            Require(company, nameof(AccountCategory));
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(AccountCategory));
        }
        else
        {
            Require(category.IsSystem, nameof(AccountCategory));
        }

        if (category.ParentId is not { } parentId)
            return;

        var parent = await resolver.CategoryAsync(parentId, category.Parent);
        Require(parent, nameof(AccountCategory));
        var parentAllowed = category.CompanyId is { } childCompanyId
            ? CategoryAvailable(parent, childCompanyId)
            : parent!.CompanyId is null && parent.IsSystem;
        Require(parentAllowed, nameof(AccountCategory));
    }

    private static async Task ValidateUserOwnedAsync(
        int userId,
        UserAccount? navigation,
        int tenantId,
        string entityName,
        OwnershipResolver resolver,
        int? currentTenantId)
    {
        var user = await resolver.UserAsync(userId, navigation);
        Require(user is not null && user.TenantId == tenantId, entityName);
        RequireCurrentTenant(currentTenantId, tenantId, entityName);
    }

    private static async Task ValidateTransactionAsync(
        ImportedTransaction transaction,
        OwnershipResolver resolver,
        int? currentTenantId)
    {
        var bank = await resolver.BankAsync(transaction.BankAccountId, transaction.BankAccount);
        Require(bank, nameof(ImportedTransaction));

        if (transaction.PeriodId is { } periodId)
        {
            var period = await resolver.PeriodAsync(periodId, transaction.Period);
            Require(period is not null && period.CompanyId == bank!.CompanyId, nameof(ImportedTransaction));
        }

        if (transaction.ImportBatchId is { } batchId)
        {
            var batch = await resolver.BatchAsync(batchId, transaction.ImportBatch);
            Require(batch is not null && batch.BankAccountId == bank!.BankAccountId, nameof(ImportedTransaction));
        }

        if (transaction.CategoryId is { } categoryId)
            Require(CategoryAvailable(await resolver.CategoryAsync(categoryId, transaction.Category), bank!.CompanyId), nameof(ImportedTransaction));

        RequireCurrentTenant(currentTenantId, bank!.TenantId, nameof(ImportedTransaction));
    }

    private static async Task ValidateAuditLogAsync(
        AuditLog audit,
        OwnershipResolver resolver,
        int? currentTenantId)
    {
        if (audit.CompanyId is { } companyId)
        {
            var company = await resolver.CompanyAsync(companyId);
            Require(company, nameof(AuditLog));
            if (audit.TenantId is { } tenantId)
                Require(company!.TenantId == tenantId, nameof(AuditLog));
            if (audit.PeriodId is { } periodId)
            {
                var period = await resolver.PeriodAsync(periodId);
                Require(period is not null && period.CompanyId == company!.CompanyId, nameof(AuditLog));
            }
            RequireCurrentTenant(currentTenantId, company!.TenantId, nameof(AuditLog));
            return;
        }

        Require(audit.PeriodId is null, nameof(AuditLog));
        if (currentTenantId is not null)
            Require(audit.TenantId == currentTenantId, nameof(AuditLog));
    }

    private static void ValidateImmutableOwnershipAnchors(AccountsDbContext db)
    {
        foreach (var company in db.ChangeTracker.Entries<Company>().Where(entry => entry.State == EntityState.Modified))
        {
            var tenant = company.Property(nameof(Company.TenantId));
            if (tenant.IsModified
                && tenant.OriginalValue is not null
                && !Equals(tenant.OriginalValue, tenant.CurrentValue))
            {
                throw new PersistenceOwnershipException(nameof(Company));
            }
        }

        RequireUnchanged<AccountingPeriod>(db, nameof(AccountingPeriod), nameof(AccountingPeriod.CompanyId));
        RequireUnchanged<BankAccount>(db, nameof(BankAccount), nameof(BankAccount.CompanyId));
        RequireUnchanged<ImportBatch>(db, nameof(ImportBatch), nameof(ImportBatch.BankAccountId));
        RequireUnchanged<AccountCategory>(db, nameof(AccountCategory), nameof(AccountCategory.CompanyId));
        RequireUnchanged<ImportedTransaction>(db, nameof(ImportedTransaction), nameof(ImportedTransaction.BankAccountId));
        RequireUnchanged<TransactionRule>(db, nameof(TransactionRule), nameof(TransactionRule.CompanyId));
        RequireUnchanged<CroFilingPackage>(db, nameof(CroFilingPackage), nameof(CroFilingPackage.PeriodId));
        RequireUnchanged<RevenueFilingPackage>(db, nameof(RevenueFilingPackage), nameof(RevenueFilingPackage.PeriodId));
        RequireUnchanged<CharityFilingPackage>(db, nameof(CharityFilingPackage), nameof(CharityFilingPackage.PeriodId));
        RequireUnchanged<FilingAuthorityEngagement>(
            db,
            nameof(FilingAuthorityEngagement),
            nameof(FilingAuthorityEngagement.TenantId),
            nameof(FilingAuthorityEngagement.CompanyId),
            nameof(FilingAuthorityEngagement.Workflow),
            nameof(FilingAuthorityEngagement.Version),
            nameof(FilingAuthorityEngagement.SupersedesAuthorityId));
        RequireUnchanged<ExternalFilingHandoffSnapshot>(
            db,
            nameof(ExternalFilingHandoffSnapshot),
            nameof(ExternalFilingHandoffSnapshot.TenantId),
            nameof(ExternalFilingHandoffSnapshot.CompanyId),
            nameof(ExternalFilingHandoffSnapshot.PeriodId),
            nameof(ExternalFilingHandoffSnapshot.Workflow),
            nameof(ExternalFilingHandoffSnapshot.Version),
            nameof(ExternalFilingHandoffSnapshot.SupersedesSnapshotRecordId),
            nameof(ExternalFilingHandoffSnapshot.AuthorityId));
        RequireUnchanged<ExternalFilingOutcomeEvent>(
            db,
            nameof(ExternalFilingOutcomeEvent),
            nameof(ExternalFilingOutcomeEvent.TenantId),
            nameof(ExternalFilingOutcomeEvent.CompanyId),
            nameof(ExternalFilingOutcomeEvent.PeriodId),
            nameof(ExternalFilingOutcomeEvent.SnapshotRecordId),
            nameof(ExternalFilingOutcomeEvent.Sequence),
            nameof(ExternalFilingOutcomeEvent.SupersedingSnapshotRecordId));
        RequireUnchanged<CorporationTaxScopeReview>(db, nameof(CorporationTaxScopeReview), nameof(CorporationTaxScopeReview.PeriodId));
        RequireUnchanged<CorporationTaxLossRecord>(db, nameof(CorporationTaxLossRecord), nameof(CorporationTaxLossRecord.PeriodId));
        RequireUnchanged<CorporationTaxFilingSupportReview>(db, nameof(CorporationTaxFilingSupportReview), nameof(CorporationTaxFilingSupportReview.PeriodId));
        RequireUnchanged<CorporationTaxPaymentRecord>(db, nameof(CorporationTaxPaymentRecord), nameof(CorporationTaxPaymentRecord.PeriodId));
        RequireUnchanged<DirectorLoanMovement>(db, nameof(DirectorLoanMovement), nameof(DirectorLoanMovement.DirectorLoanId));
        RequireUnchanged<AuditLog>(
            db,
            nameof(AuditLog),
            nameof(AuditLog.CompanyId),
            nameof(AuditLog.PeriodId),
            nameof(AuditLog.TenantId));
        RequireUnchanged<AuditIntegrityCheckpoint>(
            db,
            nameof(AuditIntegrityCheckpoint),
            nameof(AuditIntegrityCheckpoint.CompanyId),
            nameof(AuditIntegrityCheckpoint.TenantId),
            nameof(AuditIntegrityCheckpoint.LastAuditLogId),
            nameof(AuditIntegrityCheckpoint.LastIntegrityHash));
        RequireUnchanged<CompanyOnboardingRequest>(
            db,
            nameof(CompanyOnboardingRequest),
            nameof(CompanyOnboardingRequest.TenantId),
            nameof(CompanyOnboardingRequest.IdempotencyKey),
            nameof(CompanyOnboardingRequest.RequestSha256),
            nameof(CompanyOnboardingRequest.CreatedByUserId),
            nameof(CompanyOnboardingRequest.CreatedByDisplayName),
            nameof(CompanyOnboardingRequest.StartedAtUtc));
        RequireUnchanged<IdempotencyRecord>(
            db,
            nameof(IdempotencyRecord),
            nameof(IdempotencyRecord.TenantId),
            nameof(IdempotencyRecord.IdempotencyKey),
            nameof(IdempotencyRecord.Operation),
            nameof(IdempotencyRecord.RequestFingerprintSha256),
            nameof(IdempotencyRecord.CreatedByUserId),
            nameof(IdempotencyRecord.CreatedByDisplayName),
            nameof(IdempotencyRecord.StartedAtUtc));
        RequireUnchanged<LoginSecurityEvent>(
            db,
            nameof(LoginSecurityEvent),
            nameof(LoginSecurityEvent.TenantId),
            nameof(LoginSecurityEvent.UserId),
            nameof(LoginSecurityEvent.IdentifierFingerprint),
            nameof(LoginSecurityEvent.OccurredAtUtc),
            nameof(LoginSecurityEvent.ExpiresAtUtc));
        RequireUnchanged<PrivacySubjectRequest>(
            db,
            nameof(PrivacySubjectRequest),
            nameof(PrivacySubjectRequest.TenantId),
            nameof(PrivacySubjectRequest.SubjectUserId),
            nameof(PrivacySubjectRequest.RequestKind),
            nameof(PrivacySubjectRequest.RequestedByUserId),
            nameof(PrivacySubjectRequest.RequestedAtUtc));
        RequireUnchanged<PrivacyIncidentExercise>(
            db,
            nameof(PrivacyIncidentExercise),
            nameof(PrivacyIncidentExercise.TenantId),
            nameof(PrivacyIncidentExercise.ReleaseCandidate),
            nameof(PrivacyIncidentExercise.EnvironmentName),
            nameof(PrivacyIncidentExercise.ReviewedByUserId));
        RequireUnchanged<DeadlineReminderOutbox>(
            db,
            nameof(DeadlineReminderOutbox),
            nameof(DeadlineReminderOutbox.TenantId),
            nameof(DeadlineReminderOutbox.CompanyId),
            nameof(DeadlineReminderOutbox.PeriodId),
            nameof(DeadlineReminderOutbox.FilingDeadlineId),
            nameof(DeadlineReminderOutbox.DeadlineType),
            nameof(DeadlineReminderOutbox.ReminderKind),
            nameof(DeadlineReminderOutbox.ObservedDueDate),
            nameof(DeadlineReminderOutbox.ObservedCalculationFingerprintSha256),
            nameof(DeadlineReminderOutbox.DeduplicationKeySha256),
            nameof(DeadlineReminderOutbox.CreatedAtUtc));
        RequireUnchanged<PlatformJobRun>(
            db,
            nameof(PlatformJobRun),
            nameof(PlatformJobRun.TenantId),
            nameof(PlatformJobRun.JobKind),
            nameof(PlatformJobRun.Trigger),
            nameof(PlatformJobRun.ScheduledSlotUtc),
            nameof(PlatformJobRun.StartedAtUtc),
            nameof(PlatformJobRun.CreatedAtUtc));
    }

    private static void RequireUnchanged<TEntity>(
        AccountsDbContext db,
        string entityName,
        params string[] propertyNames)
        where TEntity : class
    {
        foreach (var entry in db.ChangeTracker.Entries<TEntity>().Where(candidate => candidate.State == EntityState.Modified))
        {
            foreach (var propertyName in propertyNames)
            {
                var property = entry.Property(propertyName);
                if (property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                    throw new PersistenceOwnershipException(entityName);
            }
        }
    }

    private static async Task<FilingAuthorityScope?> AuthorityScopeAsync(
        AccountsDbContext db,
        long id,
        CancellationToken cancellationToken)
    {
        var tracked = db.ChangeTracker.Entries<FilingAuthorityEngagement>()
            .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
        if (tracked is not null)
        {
            return new FilingAuthorityScope(
                tracked.Id,
                tracked.TenantId,
                tracked.CompanyId,
                tracked.Workflow,
                tracked.Version,
                tracked.AuthorityEvidenceSha256);
        }
        return await db.FilingAuthorityEngagements.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new FilingAuthorityScope(
                item.Id,
                item.TenantId,
                item.CompanyId,
                item.Workflow,
                item.Version,
                item.AuthorityEvidenceSha256))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<DeadlineScope?> DeadlineScopeAsync(
        AccountsDbContext db,
        int id,
        CancellationToken cancellationToken)
    {
        var tracked = db.ChangeTracker.Entries<FilingDeadline>()
            .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
        if (tracked is not null)
            return new DeadlineScope(tracked.Id, tracked.CompanyId, tracked.PeriodId);

        return await db.FilingDeadlines.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new DeadlineScope(item.Id, item.CompanyId, item.PeriodId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<FilingSnapshotScope?> SnapshotScopeAsync(
        AccountsDbContext db,
        long id,
        CancellationToken cancellationToken)
    {
        var tracked = db.ChangeTracker.Entries<ExternalFilingHandoffSnapshot>()
            .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
        if (tracked is not null)
        {
            return new FilingSnapshotScope(
                tracked.Id,
                tracked.TenantId,
                tracked.CompanyId,
                tracked.PeriodId,
                tracked.Workflow,
                tracked.Version,
                tracked.SnapshotId,
                tracked.ArtifactSha256,
                tracked.SupersedesSnapshotId,
                tracked.SupersedesArtifactSha256);
        }
        return await db.ExternalFilingHandoffSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new FilingSnapshotScope(
                item.Id,
                item.TenantId,
                item.CompanyId,
                item.PeriodId,
                item.Workflow,
                item.Version,
                item.SnapshotId,
                item.ArtifactSha256,
                item.SupersedesSnapshotId,
                item.SupersedesArtifactSha256))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task ValidatePeriodOwnedAsync(
        int periodId,
        AccountingPeriod? navigation,
        string entityName,
        OwnershipResolver resolver,
        int? currentTenantId)
    {
        var period = await resolver.PeriodAsync(periodId, navigation);
        Require(period, entityName);
        RequireCurrentTenant(currentTenantId, period!.TenantId, entityName);
    }

    private static bool CategoryAvailable(CategoryScope? category, int companyId) =>
        category is not null
        && (category.CompanyId == companyId || category.CompanyId is null && category.IsSystem);

    private static IEnumerable<T> Changed<T>(AccountsDbContext db) where T : class =>
        db.ChangeTracker.Entries<T>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity);

    private static void Require(object? value, string entityName)
    {
        if (value is null || value is false)
            throw new PersistenceOwnershipException(entityName);
    }

    private static void RequireCurrentTenant(int? currentTenantId, int? entityTenantId, string entityName)
    {
        if (currentTenantId is not null && entityTenantId != currentTenantId)
            throw new PersistenceOwnershipException(entityName);
    }

    private sealed class OwnershipResolver(AccountsDbContext db, CancellationToken cancellationToken)
    {
        private readonly Dictionary<int, CompanyScope?> companies = [];
        private readonly Dictionary<int, PeriodScope?> periods = [];
        private readonly Dictionary<int, BankScope?> banks = [];
        private readonly Dictionary<int, BatchScope?> batches = [];
        private readonly Dictionary<int, CategoryScope?> categories = [];
        private readonly Dictionary<int, CompanyOwnedScope?> fixedAssets = [];
        private readonly Dictionary<int, CompanyOwnedScope?> loans = [];
        private readonly Dictionary<int, CompanyOwnedScope?> officers = [];
        private readonly Dictionary<int, DirectorLoanScope?> directorLoans = [];
        private readonly Dictionary<int, UserScope?> users = [];
        private readonly Dictionary<int, AuditScope?> auditLogs = [];

        public async Task<CompanyScope?> CompanyAsync(int id, Company? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return new CompanyScope(navigation.Id, navigation.TenantId);
            if (id <= 0)
                return null;
            if (companies.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<Company>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            var result = tracked is not null
                ? new CompanyScope(tracked.Id, tracked.TenantId)
                : await db.Companies.IgnoreQueryFilters().AsNoTracking()
                    .Where(company => company.Id == id)
                    .Select(company => new CompanyScope(company.Id, company.TenantId))
                    .FirstOrDefaultAsync(cancellationToken);
            companies[id] = result;
            return result;
        }

        public async Task<PeriodScope?> PeriodAsync(int id, AccountingPeriod? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return await PeriodFromEntityAsync(navigation);
            if (id <= 0)
                return null;
            if (periods.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<AccountingPeriod>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            if (tracked is not null)
            {
                var trackedResult = await PeriodFromEntityAsync(tracked);
                periods[id] = trackedResult;
                return trackedResult;
            }

            var companyId = await db.AccountingPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(period => period.Id == id)
                .Select(period => (int?)period.CompanyId)
                .FirstOrDefaultAsync(cancellationToken);
            var company = companyId is null ? null : await CompanyAsync(companyId.Value);
            var result = company is null ? null : new PeriodScope(id, company.CompanyId, company.TenantId);
            periods[id] = result;
            return result;
        }

        public async Task<BankScope?> BankAsync(int id, BankAccount? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return await BankFromEntityAsync(navigation);
            if (id <= 0)
                return null;
            if (banks.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<BankAccount>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            if (tracked is not null)
            {
                var trackedResult = await BankFromEntityAsync(tracked);
                banks[id] = trackedResult;
                return trackedResult;
            }

            var companyId = await db.BankAccounts.IgnoreQueryFilters().AsNoTracking()
                .Where(bank => bank.Id == id)
                .Select(bank => (int?)bank.CompanyId)
                .FirstOrDefaultAsync(cancellationToken);
            var company = companyId is null ? null : await CompanyAsync(companyId.Value);
            var result = company is null ? null : new BankScope(id, company.CompanyId, company.TenantId);
            banks[id] = result;
            return result;
        }

        public async Task<BatchScope?> BatchAsync(int id, ImportBatch? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return await BatchFromEntityAsync(navigation);
            if (id <= 0)
                return null;
            if (batches.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<ImportBatch>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            if (tracked is not null)
            {
                var trackedResult = await BatchFromEntityAsync(tracked);
                batches[id] = trackedResult;
                return trackedResult;
            }

            var bankId = await db.ImportBatches.IgnoreQueryFilters().AsNoTracking()
                .Where(batch => batch.Id == id)
                .Select(batch => (int?)batch.BankAccountId)
                .FirstOrDefaultAsync(cancellationToken);
            var bank = bankId is null ? null : await BankAsync(bankId.Value);
            var result = bank is null ? null : new BatchScope(id, bank.BankAccountId, bank.CompanyId, bank.TenantId);
            batches[id] = result;
            return result;
        }

        public async Task<CategoryScope?> CategoryAsync(int id, AccountCategory? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return await CategoryFromEntityAsync(navigation);
            if (id <= 0)
                return null;
            if (categories.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<AccountCategory>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            if (tracked is not null)
            {
                var trackedResult = await CategoryFromEntityAsync(tracked);
                categories[id] = trackedResult;
                return trackedResult;
            }

            var row = await db.AccountCategories.IgnoreQueryFilters().AsNoTracking()
                .Where(category => category.Id == id)
                .Select(category => new { category.Id, category.CompanyId, category.IsSystem })
                .FirstOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                categories[id] = null;
                return null;
            }

            var company = row.CompanyId is null ? null : await CompanyAsync(row.CompanyId.Value);
            var result = new CategoryScope(row.Id, row.CompanyId, row.IsSystem, company?.TenantId);
            categories[id] = result;
            return result;
        }

        public Task<CompanyOwnedScope?> FixedAssetAsync(int id, FixedAsset? navigation = null) =>
            CompanyOwnedAsync(id, navigation?.Id, navigation?.CompanyId, fixedAssets, db.FixedAssets, asset => asset.Id, asset => asset.CompanyId);

        public Task<CompanyOwnedScope?> LoanAsync(int id, Loan? navigation = null) =>
            CompanyOwnedAsync(id, navigation?.Id, navigation?.CompanyId, loans, db.Loans, loan => loan.Id, loan => loan.CompanyId);

        public Task<CompanyOwnedScope?> OfficerAsync(int id, CompanyOfficer? navigation = null) =>
            CompanyOwnedAsync(id, navigation?.Id, navigation?.CompanyId, officers, db.CompanyOfficers, officer => officer.Id, officer => officer.CompanyId);

        public async Task<DirectorLoanScope?> DirectorLoanAsync(int id, DirectorLoan? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
            {
                var navigationPeriod = await PeriodAsync(navigation.PeriodId, navigation.Period);
                return navigationPeriod is null
                    ? null
                    : new DirectorLoanScope(navigation.Id, navigationPeriod.PeriodId, navigationPeriod.TenantId);
            }
            if (id <= 0)
                return null;
            if (directorLoans.TryGetValue(id, out var cached))
                return cached;

            var tracked = db.ChangeTracker.Entries<DirectorLoan>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            if (tracked is not null)
            {
                var trackedPeriod = await PeriodAsync(tracked.PeriodId, tracked.Period);
                var trackedResult = trackedPeriod is null
                    ? null
                    : new DirectorLoanScope(tracked.Id, trackedPeriod.PeriodId, trackedPeriod.TenantId);
                directorLoans[id] = trackedResult;
                return trackedResult;
            }

            var periodId = await db.DirectorLoans.IgnoreQueryFilters().AsNoTracking()
                .Where(loan => loan.Id == id)
                .Select(loan => (int?)loan.PeriodId)
                .FirstOrDefaultAsync(cancellationToken);
            var period = periodId is null ? null : await PeriodAsync(periodId.Value);
            var result = period is null ? null : new DirectorLoanScope(id, period.PeriodId, period.TenantId);
            directorLoans[id] = result;
            return result;
        }

        public async Task<UserScope?> UserAsync(int id, UserAccount? navigation = null)
        {
            if (navigation is not null && (id <= 0 || navigation.Id == id))
                return new UserScope(navigation.Id, navigation.TenantId);
            if (id <= 0)
                return null;
            if (users.TryGetValue(id, out var cached))
                return cached;
            var tracked = db.ChangeTracker.Entries<UserAccount>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            var result = tracked is not null
                ? new UserScope(tracked.Id, tracked.TenantId)
                : await db.UserAccounts.IgnoreQueryFilters().AsNoTracking()
                    .Where(user => user.Id == id)
                    .Select(user => new UserScope(user.Id, user.TenantId))
                    .FirstOrDefaultAsync(cancellationToken);
            users[id] = result;
            return result;
        }

        public async Task<AuditScope?> AuditLogAsync(int id)
        {
            if (id <= 0)
                return null;
            if (auditLogs.TryGetValue(id, out var cached))
                return cached;
            var tracked = db.ChangeTracker.Entries<AuditLog>()
                .FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.Id == id)?.Entity;
            var result = tracked is not null
                ? new AuditScope(tracked.Id, tracked.CompanyId, tracked.TenantId, tracked.IntegrityHash)
                : await db.AuditLogs.AsNoTracking()
                    .Where(audit => audit.Id == id)
                    .Select(audit => new AuditScope(audit.Id, audit.CompanyId, audit.TenantId, audit.IntegrityHash))
                    .FirstOrDefaultAsync(cancellationToken);
            auditLogs[id] = result;
            return result;
        }

        private async Task<PeriodScope?> PeriodFromEntityAsync(AccountingPeriod period)
        {
            var company = await CompanyAsync(period.CompanyId, period.Company);
            return company is null ? null : new PeriodScope(period.Id, company.CompanyId, company.TenantId);
        }

        private async Task<BankScope?> BankFromEntityAsync(BankAccount bank)
        {
            var company = await CompanyAsync(bank.CompanyId, bank.Company);
            return company is null ? null : new BankScope(bank.Id, company.CompanyId, company.TenantId);
        }

        private async Task<BatchScope?> BatchFromEntityAsync(ImportBatch batch)
        {
            var bank = await BankAsync(batch.BankAccountId, batch.BankAccount);
            return bank is null ? null : new BatchScope(batch.Id, bank.BankAccountId, bank.CompanyId, bank.TenantId);
        }

        private async Task<CategoryScope> CategoryFromEntityAsync(AccountCategory category)
        {
            var company = category.CompanyId is null
                ? null
                : await CompanyAsync(category.CompanyId.Value, category.Company);
            return new CategoryScope(category.Id, category.CompanyId, category.IsSystem, company?.TenantId);
        }

        private async Task<CompanyOwnedScope?> CompanyOwnedAsync<TEntity>(
            int id,
            int? navigationId,
            int? navigationCompanyId,
            Dictionary<int, CompanyOwnedScope?> cache,
            DbSet<TEntity> set,
            System.Linq.Expressions.Expression<Func<TEntity, int>> idSelector,
            System.Linq.Expressions.Expression<Func<TEntity, int>> companySelector)
            where TEntity : class
        {
            if (navigationId is not null && navigationCompanyId is not null && (id <= 0 || navigationId == id))
            {
                var navigationCompany = await CompanyAsync(navigationCompanyId.Value);
                return navigationCompany is null
                    ? null
                    : new CompanyOwnedScope(navigationId.Value, navigationCompany.CompanyId, navigationCompany.TenantId);
            }
            if (id <= 0)
                return null;
            if (cache.TryGetValue(id, out var cached))
                return cached;

            var parameter = idSelector.Parameters[0];
            var predicate = System.Linq.Expressions.Expression.Lambda<Func<TEntity, bool>>(
                System.Linq.Expressions.Expression.Equal(
                    idSelector.Body,
                    System.Linq.Expressions.Expression.Constant(id)),
                parameter);
            var companyId = await set.IgnoreQueryFilters().AsNoTracking()
                .Where(predicate)
                .Select(companySelector)
                .Select(value => (int?)value)
                .FirstOrDefaultAsync(cancellationToken);
            var company = companyId is null ? null : await CompanyAsync(companyId.Value);
            var result = company is null ? null : new CompanyOwnedScope(id, company.CompanyId, company.TenantId);
            cache[id] = result;
            return result;
        }
    }

    private sealed record CompanyScope(int CompanyId, int? TenantId);
    private sealed record PeriodScope(int PeriodId, int CompanyId, int? TenantId);
    private sealed record BankScope(int BankAccountId, int CompanyId, int? TenantId);
    private sealed record BatchScope(int ImportBatchId, int BankAccountId, int CompanyId, int? TenantId);
    private sealed record CategoryScope(int CategoryId, int? CompanyId, bool IsSystem, int? TenantId);
    private sealed record CompanyOwnedScope(int EntityId, int CompanyId, int? TenantId);
    private sealed record DirectorLoanScope(int DirectorLoanId, int PeriodId, int? TenantId);
    private sealed record UserScope(int UserId, int TenantId);
    private sealed record AuditScope(int AuditLogId, int? CompanyId, int? TenantId, string? IntegrityHash);
    private sealed record DeadlineScope(int DeadlineId, int CompanyId, int PeriodId);
    private sealed record FilingAuthorityScope(long Id, int TenantId, int CompanyId, string Workflow, int Version, string EvidenceSha256);
    private sealed record FilingSnapshotScope(
        long Id,
        int TenantId,
        int CompanyId,
        int PeriodId,
        string Workflow,
        int Version,
        Guid SnapshotId,
        string ArtifactSha256,
        Guid? SupersedesSnapshotId,
        string? SupersedesArtifactSha256);
}
