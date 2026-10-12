using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace Accounts.Tests;

// Batch B1: a company holding a charitable tax exemption gets no automatic corporation-tax provision,
// and an entered CT liability blocks final outputs until an Owner or Reviewer confirms it is due (for
// example on income outside the exemption). A confirmed liability is provided for exactly as for a
// company without the exemption; changing the liability amount clears the confirmation.
public sealed partial class AccountChartSafeguardTests
{
    private const string ExemptCorporationTaxWarning150 =
        "This company holds a charitable tax exemption, but a corporation tax liability of €150.00 is entered; confirm it is due (for example on income outside the exemption) or remove it.";

    private const string DueReason = "Income outside the exemption: trading outside the primary purpose";

    // The attested taxable profit outside the exemption: 12.5% of 1,200 is the 150 liability.
    private const decimal AttestedProfit = 1_200m;

    [Fact]
    public async Task ExemptCorporationTax_Unconfirmed_IsBlocked_AndNoProvisionIsPosted()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg);
        var statements = new FinancialStatementsService(db);

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        Assert.Contains(ExemptCorporationTaxWarning150, (await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings);
        Assert.Contains(ExemptCorporationTaxWarning150, await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id));
        Assert.Empty(await CorporationTaxProvisionsAsync(db, period));
        Assert.Equal(0m, (await statements.GetProfitAndLossAsync(company.Id, period.Id)).TaxCharge);
    }

    [Theory]
    [InlineData(ChartOfAccountsVariant.CharityClg, "Owner")]
    [InlineData(ChartOfAccountsVariant.CharityClg, "Reviewer")]
    [InlineData(ChartOfAccountsVariant.GenericTrading, "Owner")]
    public async Task ExemptCorporationTax_ConfirmedAsDue_ClearsTheBlocker_AndIsProvidedForLikeANonExemptCompany(ChartOfAccountsVariant variant, string role)
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, variant);

        var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput($"  {DueReason} ", AttestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, role));

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        var balance = await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id);
        Assert.Equal($"Example {role}", balance.ConfirmedDueBy);
        Assert.NotNull(balance.ConfirmedDueAt);
        Assert.Equal(DueReason, balance.ConfirmedDueReason);
        Assert.Equal(150m, balance.Liability);
        Assert.Equal(150m, balance.ConfirmedDueLiability);
        Assert.Equal(AttestedProfit, balance.ConfirmedTaxableProfitOutsideExemption);
        var audit = Assert.Single(await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed)
            .ToListAsync());
        Assert.Equal("user:1", audit.UserId);
        Assert.Equal(balance.Id, audit.EntityId);
        Assert.Contains(DueReason, audit.NewValueJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(DueReason, audit.OldValueJson!, StringComparison.Ordinal);

        var statements = new FinancialStatementsService(db);
        Assert.DoesNotContain((await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings,
            warning => warning.Contains("charitable tax exemption", StringComparison.Ordinal));
        Assert.DoesNotContain(await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id),
            blocker => blocker.Contains("charitable tax exemption", StringComparison.Ordinal));

        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);

        var provision = Assert.Single(await CorporationTaxProvisionsAsync(db, period));
        Assert.Equal(150m, provision.Amount);
        var categories = await db.AccountCategories.AsNoTracking().Where(c => c.CompanyId == company.Id).ToListAsync();
        var charge = categories.Single(c => c.Id == provision.DebitCategoryId);
        var payable = categories.Single(c => c.Id == provision.CreditCategoryId);
        Assert.Equal(AccountSystemRole.CorporationTaxCharge, charge.SystemRole);
        Assert.Equal(AccountSystemRole.CorporationTaxPayable, payable.SystemRole);
        if (variant == ChartOfAccountsVariant.CharityClg)
        {
            // The charity chart's lazily created corporation-tax accounts.
            Assert.Equal("8900", charge.Code);
            Assert.Equal("2270", payable.Code);
        }
        Assert.Equal(2, categories.Count(c => c.SystemRole is AccountSystemRole.CorporationTaxCharge or AccountSystemRole.CorporationTaxPayable));
        Assert.Equal(150m, (await statements.GetProfitAndLossAsync(company.Id, period.Id)).TaxCharge);

        // The confirmed liability reconciles to the exemption-aware computation: nothing blocks.
        await SaveSimpleTaxScopeAsync(db, company, period);
        await AssertCorporationTaxReconcilesAsync(statements, company, period);
    }

    [Fact]
    public async Task ExemptCorporationTax_ChangingTheLiability_ClearsTheConfirmation_ButResavingTheSameAmountKeepsIt()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg);
        await ConfirmDueAsync(db, company, period);
        var statements = new FinancialStatementsService(db);

        // Re-saving the same liability (here with a payment recorded against it) keeps the confirmation.
        Assert.Equal(StatusCodes.Status200OK, StatusCode(await UpsertCorporationTaxAsync(db, company, period, 150m, 50m)));
        var kept = await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id);
        Assert.Equal(DueReason, kept.ConfirmedDueReason);
        Assert.NotNull(kept.ConfirmedDueAt);
        Assert.Equal("Example Owner", kept.ConfirmedDueBy);
        Assert.DoesNotContain((await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings,
            warning => warning.Contains("charitable tax exemption", StringComparison.Ordinal));

        // A different liability needs a fresh confirmation.
        Assert.Equal(StatusCodes.Status200OK, StatusCode(await UpsertCorporationTaxAsync(db, company, period, 175m, 50m)));
        var cleared = await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id);
        Assert.Null(cleared.ConfirmedDueBy);
        Assert.Null(cleared.ConfirmedDueAt);
        Assert.Null(cleared.ConfirmedDueReason);
        Assert.Null(cleared.ConfirmedDueLiability);
        Assert.Contains(
            "This company holds a charitable tax exemption, but a corporation tax liability of €175.00 is entered; confirm it is due (for example on income outside the exemption) or remove it.",
            (await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        Assert.Empty(await CorporationTaxProvisionsAsync(db, period));

        // The confirmation is part of the tax balance's audited state: the upsert that cleared it
        // records the confirmation it replaced.
        var upserts = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == AuditEventCodes.TaxBalanceUpserted)
            .OrderBy(log => log.Id)
            .ToListAsync();
        Assert.Contains(DueReason, upserts[^1].OldValueJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(DueReason, upserts[^1].NewValueJson!, StringComparison.Ordinal);
        Assert.Contains(DueReason, upserts[^2].NewValueJson!, StringComparison.Ordinal);
    }

    // Defence in depth: the confirmation counts only while it is for the current liability, even if
    // the liability changes by a path that does not clear it.
    [Fact]
    public async Task ExemptCorporationTax_Confirmation_CountsOnlyForTheConfirmedAmount()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg);
        await ConfirmDueAsync(db, company, period);
        var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
        balance.Liability = 175m;
        balance.Balance = 175m;
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        Assert.NotNull(balance.ConfirmedDueAt);
        Assert.Equal(150m, balance.ConfirmedDueLiability);
        Assert.Contains(
            "This company holds a charitable tax exemption, but a corporation tax liability of €175.00 is entered; confirm it is due (for example on income outside the exemption) or remove it.",
            (await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        Assert.Empty(await CorporationTaxProvisionsAsync(db, period));
        Assert.False(await CorporationTaxObligation.RevenueAppliesAsync(db, period.Id));

        // Back at the confirmed amount, the confirmation counts again.
        balance.Liability = 150m;
        balance.Balance = 150m;
        await db.SaveChangesAsync();
        Assert.True(await CorporationTaxObligation.RevenueAppliesAsync(db, period.Id));
        Assert.DoesNotContain((await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings,
            warning => warning.Contains("charitable tax exemption", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Accountant")]
    [InlineData("Client")]
    public async Task ConfirmingCorporationTaxDue_IsDeniedToAccountantsAndClients(string role)
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg);

        Assert.False(RoleAuthorizationService.Authorize(
            Owner(company) with { Role = role }, new PathString(ConfirmDuePath(company, period)), HttpMethods.Post).IsAllowed);
        var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput(DueReason, AttestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, role));

        Assert.Equal(StatusCodes.Status403Forbidden, StatusCode(result));
        var balance = await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id);
        Assert.Null(balance.ConfirmedDueAt);
        Assert.Null(balance.ConfirmedDueBy);
        Assert.Null(balance.ConfirmedDueReason);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Reviewer")]
    public void ConfirmingCorporationTaxDue_IsAnOwnerOrReviewerAct(string role)
    {
        var company = new Company { Id = 1, TenantId = 1, LegalName = "Example CLG" };
        Assert.True(RoleAuthorizationService.Authorize(
            Owner(company) with { Role = role },
            new PathString("/api/companies/1/periods/3/tax-balances/CorporationTax/confirm-due"),
            HttpMethods.Post).IsAllowed);
    }

    [Fact]
    public async Task ConfirmingCorporationTaxDue_RequiresARecentAuthenticatorVerification()
    {
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/1/periods/3/tax-balances/CorporationTax/confirm-due"), HttpMethods.Post));
        Assert.True(RecentAuthenticationMiddleware.RequiresRecentMfa(new PathString("/api/companies/1/periods/3/tax-balances/CorporationTax/confirm-due/"), HttpMethods.Post));

        var reachedEndpoint = false;
        var middleware = new RecentAuthenticationMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });
        var company = new Company { Id = 1, TenantId = 1, LegalName = "Example CLG" };
        var context = OwnerRequest(company, HttpMethods.Post, "/api/companies/1/periods/3/tax-balances/CorporationTax/confirm-due");
        context.Items[AuthContext.ItemKey] = Owner(company) with
        {
            MfaMethod = "totp",
            MfaVerifiedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-45)
        };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, Options.Create(new AuthSessionConfig()), TimeProvider.System);

        Assert.False(reachedEndpoint);
        Assert.Equal(StatusCodes.Status428PreconditionRequired, context.Response.StatusCode);
    }

    [Fact]
    public async Task ConfirmingCorporationTaxDue_ForACompanyWithoutTheExemption_IsRefused_AndItIsProvidedForAsBefore()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg, exempt: false);

        var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput(DueReason, AttestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, "Owner"));

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
        Assert.Equal(
            "Only a company holding a charitable tax exemption confirms its corporation tax liability; this company's liability is provided for automatically.",
            ErrorOf(result));
        Assert.Null((await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id)).ConfirmedDueAt);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed));
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        Assert.Equal(150m, Assert.Single(await CorporationTaxProvisionsAsync(db, period)).Amount);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("profit")]
    [InlineData("liability")]
    [InlineData("locked")]
    public async Task ConfirmingCorporationTaxDue_WithoutAReasonALiabilityOrAnOpenPeriod_IsRefused(string missing)
    {
        await using var db = CreateDb();
        var (company, period) = await SeedExemptCorporationTaxAsync(db, ChartOfAccountsVariant.CharityClg);
        if (missing == "liability")
        {
            var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
            balance.Liability = 0m;
            balance.Balance = 0m;
        }
        if (missing == "locked")
        {
            period.Status = PeriodStatus.Finalised;
            period.LockedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        var result = await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput(missing == "reason" ? "   " : DueReason, missing == "profit" ? null : AttestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, "Owner"));

        Assert.Equal(missing == "locked" ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest, StatusCode(result));
        if (missing == "reason")
            Assert.Equal("Give the reason the corporation tax liability is due (up to 1000 characters).", ErrorOf(result));
        if (missing == "profit")
            Assert.Equal("Give the taxable profit from activities outside the charitable exemption (0 or more).", ErrorOf(result));
        if (missing == "liability")
            Assert.Equal("There is no corporation tax liability entered for this period to confirm.", ErrorOf(result));
        Assert.Null((await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id)).ConfirmedDueAt);
        Assert.False(await db.AuditLogs.AnyAsync(log => log.Action == AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed));
    }

    // ---- Exemption-aware computation (final review finding 1) ----------------------------------------

    // The reviewer's probe: 40,000 donations (within the exemption) and 1,000 of shop sales (outside it).
    [Fact]
    public async Task ExemptCorporationTax_ConfirmedOnTheAttestedProfit_IsNotBlockedByTheComputation()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedDonationsAndShopSalesAsync(db, liability: 125m);
        await ConfirmDueAsync(db, company, period, attestedProfit: 1_000m, liability: 125m);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        await SaveSimpleTaxScopeAsync(db, company, period);
        var statements = new FinancialStatementsService(db);

        var tax = await new TaxComputationService(db, statements).ComputeAsync(company.Id, period.Id);

        Assert.Equal(41_000m, tax.AccountingProfit);
        var exempt = Assert.Single(tax.Adjustments, adjustment => adjustment.Description.StartsWith("Income within the charitable exemption (confirmed by Example Owner on ", StringComparison.Ordinal));
        Assert.Equal(-40_000m, exempt.Amount);
        Assert.Equal(1_000m, tax.TaxableProfit);
        Assert.Equal(125m, tax.TotalCorporationTax);
        Assert.True(tax.FinalTaxChargeSupported);
        await AssertCorporationTaxReconcilesAsync(statements, company, period);
        // The CT1 support carries the same line and figures.
        var ct1 = await new TaxComputationService(db, statements).GetCt1SupportDataAsync(company.Id, period.Id);
        Assert.Contains(ct1.Adjustments, adjustment => adjustment.Description == exempt.Description && adjustment.Amount == -40_000m);
        Assert.Equal(1_000m, ct1.TaxableProfit);
        Assert.Equal(125m, ct1.TaxDue);
        // The CT1 support worksheet reconciles taxable profit to the streams less the exemption line.
        var worksheet = (await new CorporationTaxFilingSupportService(db, new TaxComputationService(db, statements), new AuditService(db))
            .GetAsync(company.Id, period.Id)).Worksheet;
        Assert.True(Assert.Single(worksheet.Reconciliations, reconciliation => reconciliation.Code == "tax-streams").Reconciles);
    }

    [Fact]
    public async Task ExemptCorporationTax_ConfirmedLiabilityThatDoesNotMatchTheAttestedProfit_IsStillBlocked()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedDonationsAndShopSalesAsync(db, liability: 100m);
        await ConfirmDueAsync(db, company, period, attestedProfit: 1_000m, liability: 100m);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        await SaveSimpleTaxScopeAsync(db, company, period);
        var statements = new FinancialStatementsService(db);
        var mismatch = $"Entered corporation tax ({100m:C}) does not match the supported corporation tax computation ({125m:C}). Reconcile the tax charge before filing.";

        Assert.Contains(mismatch, (await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings);
        Assert.Contains(mismatch, await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id));
        Assert.Contains(await statements.GetPreFilingConsistencyIssuesAsync(company.Id, period.Id),
            issue => issue.Contains("does not match the supported corporation tax computation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExemptCorporationTax_ChangingTheAttestedProfit_InvalidatesTheConfirmation()
    {
        await using var db = CreateDb();
        var (company, period) = await SeedDonationsAndShopSalesAsync(db, liability: 125m);
        await ConfirmDueAsync(db, company, period, attestedProfit: 1_000m, liability: 125m);
        await new AdjustmentService(db).GenerateAutoAdjustmentsAsync(company.Id, period.Id);
        await SaveSimpleTaxScopeAsync(db, company, period);
        var statements = new FinancialStatementsService(db);
        await AssertCorporationTaxReconcilesAsync(statements, company, period);

        // A different attested figure is a new confirmation: it replaces the earlier one (audited), the
        // retained tax computation no longer matches until the scope is re-saved, and the unchanged 125
        // liability then no longer reconciles to the computation on 2,000.
        await ConfirmDueAsync(db, company, period, attestedProfit: 2_000m, liability: 125m);
        var confirmations = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == AuditEventCodes.TaxBalanceCorporationTaxDueConfirmed)
            .OrderBy(log => log.Id)
            .ToListAsync();
        Assert.Equal(2, confirmations.Count);
        Assert.Contains("1000", confirmations[1].OldValueJson!, StringComparison.Ordinal);
        Assert.Contains("2000", confirmations[1].NewValueJson!, StringComparison.Ordinal);
        Assert.Contains(await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id),
            blocker => blocker.Contains("corporation-tax charge is blocked", StringComparison.Ordinal));
        await SaveSimpleTaxScopeAsync(db, company, period);
        Assert.Contains($"Entered corporation tax ({125m:C}) does not match the supported corporation tax computation ({250m:C}). Reconcile the tax charge before filing.",
            await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id));

        // A confirmation without its attested figure does not count at all.
        var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
        balance.ConfirmedTaxableProfitOutsideExemption = null;
        await db.SaveChangesAsync();
        Assert.False(await CorporationTaxObligation.IsConfirmedDueAsync(db, period.Id));
        Assert.Contains(
            "This company holds a charitable tax exemption, but a corporation tax liability of €125.00 is entered; confirm it is due (for example on income outside the exemption) or remove it.",
            (await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings);
        var tax = await new TaxComputationService(db, statements).ComputeAsync(company.Id, period.Id);
        Assert.DoesNotContain(tax.Adjustments, adjustment => adjustment.Description.StartsWith("Income within the charitable exemption", StringComparison.Ordinal));
    }

    // Without a valid confirmation, and for a company without the exemption, the computation is
    // byte-identical to the computation with no confirmation fields at all.
    [Theory]
    [InlineData(false, "valid")]
    [InlineData(true, "none")]
    [InlineData(true, "different-liability")]
    public async Task CorporationTaxComputation_WithoutAValidExemptConfirmation_IsByteIdentical(bool exempt, string confirmation)
    {
        await using var db = CreateDb();
        var (company, period) = await SeedDonationsAndShopSalesAsync(db, liability: 125m);
        company.HoldsCharitableTaxExemption = exempt;
        company.CharitableTaxExemptionReference = exempt ? "CHY 00000" : null;
        await db.SaveChangesAsync();
        await SaveSimpleTaxScopeAsync(db, company, period);
        var statements = new FinancialStatementsService(db);
        var baseline = JsonSerializer.Serialize(await new TaxComputationService(db, statements).ComputeAsync(company.Id, period.Id));

        if (confirmation != "none")
        {
            var balance = await db.TaxBalances.SingleAsync(t => t.PeriodId == period.Id);
            balance.ConfirmedDueBy = "Example Owner";
            balance.ConfirmedDueAt = new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc);
            balance.ConfirmedDueReason = DueReason;
            balance.ConfirmedDueLiability = confirmation == "valid" ? 125m : 99m;
            balance.ConfirmedTaxableProfitOutsideExemption = 1_000m;
            await db.SaveChangesAsync();
        }

        Assert.Equal(baseline, JsonSerializer.Serialize(await new TaxComputationService(db, statements).ComputeAsync(company.Id, period.Id)));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static async Task<(Company Company, AccountingPeriod Period)> SeedDonationsAndShopSalesAsync(AccountsDbContext db, decimal liability)
    {
        var (company, period, bank, ids) = await SeedCharityCompanyAsync(db);
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = true;
        company.CharitableTaxExemptionReference = "CHY 00000";
        db.ImportedTransactions.AddRange(
            Transaction(bank, period, new DateOnly(2025, 3, 1), "Donations received", 40_000m, ids["4000"]),
            Transaction(bank, period, new DateOnly(2025, 4, 1), "Charity shop sales", 1_000m, ids["4100"]));
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = liability, Balance = liability });
        await db.SaveChangesAsync();
        return (company, period);
    }

    private static async Task ConfirmDueAsync(AccountsDbContext db, Company company, AccountingPeriod period, decimal attestedProfit, decimal liability)
    {
        Assert.Equal(StatusCodes.Status200OK, StatusCode(await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput(DueReason, attestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, "Owner"))));
        Assert.Equal(liability, (await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == period.Id)).ConfirmedDueLiability);
    }

    private static Task SaveSimpleTaxScopeAsync(AccountsDbContext db, Company company, AccountingPeriod period) =>
        new TaxComputationService(db, new FinancialStatementsService(db)).SaveScopeReviewAsync(
            company.Id,
            period.Id,
            new TaxComputationService.CorporationTaxScopeReviewInput(
                IsCloseCompany: false,
                IsServiceCompany: null,
                HasGroupOrConsortiumRelief: false,
                HasChargeableGains: false,
                HasForeignIncomeOrTaxCredits: false,
                HasExceptedTrade: false,
                HasOtherReliefsOrSpecialRegimes: false,
                DeclaredPassiveIncomePresent: false,
                PassiveIncomeClassificationReviewed: false,
                LossTreatment: CorporationTaxLossTreatment.NotApplicable,
                BroughtForwardTradingLoss: 0m,
                BroughtForwardLossEvidence: null,
                EvidenceNote: "Automated test scope evidence; not professional acceptance."),
            "Automated test actor");

    // No corporation-tax mismatch or blocked-charge item in readiness, final outputs or pre-filing.
    private static async Task AssertCorporationTaxReconcilesAsync(FinancialStatementsService statements, Company company, AccountingPeriod period)
    {
        static bool IsCorporationTaxBlocker(string item) =>
            item.Contains("does not match the supported corporation tax computation", StringComparison.Ordinal)
            || item.Contains("corporation-tax charge is blocked", StringComparison.Ordinal)
            || item.Contains("charitable tax exemption", StringComparison.Ordinal);
        Assert.DoesNotContain((await statements.GetReadinessScoreAsync(company.Id, period.Id)).Warnings, IsCorporationTaxBlocker);
        Assert.DoesNotContain(await statements.GetFinalOutputReadinessBlockersAsync(company.Id, period.Id), IsCorporationTaxBlocker);
        Assert.DoesNotContain(await statements.GetPreFilingConsistencyIssuesAsync(company.Id, period.Id), IsCorporationTaxBlocker);
    }

    private static async Task<(Company Company, AccountingPeriod Period)> SeedExemptCorporationTaxAsync(
        AccountsDbContext db,
        ChartOfAccountsVariant variant,
        bool exempt = true)
    {
        var company = await SeedCompanyAsync(db, variant, CompanyType.CompanyLimitedByGuarantee);
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = exempt;
        company.CharitableTaxExemptionReference = exempt ? "CHY 00000" : null;
        var period = await AddPeriodAsync(db, company);
        var ids = (await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id)).ToDictionary(c => c.Code, c => c.Id);
        var bank = new BankAccount { CompanyId = company.Id, Name = "Current account" };
        db.BankAccounts.Add(bank);
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(Transaction(bank, period, new DateOnly(2025, 5, 1), "Income", 2_000m, ids["4000"]));
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 150m, Balance = 150m });
        await db.SaveChangesAsync();
        return (company, period);
    }

    private static async Task ConfirmDueAsync(AccountsDbContext db, Company company, AccountingPeriod period) =>
        Assert.Equal(StatusCodes.Status200OK, StatusCode(await YearEndEndpoints.ConfirmCorporationTaxDueEndpointAsync(
            company.Id,
            period.Id,
            new CorporationTaxDueConfirmationInput(DueReason, AttestedProfit),
            db,
            new AuditService(db),
            ConfirmDueRequest(company, period, "Owner"))));

    private static Task<IResult> UpsertCorporationTaxAsync(AccountsDbContext db, Company company, AccountingPeriod period, decimal liability, decimal paid) =>
        YearEndEndpoints.UpsertTaxBalanceEndpointAsync(
            company.Id,
            period.Id,
            TaxType.CorporationTax,
            new TaxBalanceInput { Liability = liability, Paid = paid, Balance = liability - paid },
            db,
            new AuditService(db),
            OwnerRequest(company, HttpMethods.Put, $"/api/companies/{company.Id}/periods/{period.Id}/tax-balances/CorporationTax"));

    private static Task<List<Adjustment>> CorporationTaxProvisionsAsync(AccountsDbContext db, AccountingPeriod period) =>
        db.Adjustments.AsNoTracking()
            .Where(adjustment => adjustment.PeriodId == period.Id && adjustment.Description == "Corporation tax provision")
            .ToListAsync();

    private static string ConfirmDuePath(Company company, AccountingPeriod period) =>
        $"/api/companies/{company.Id}/periods/{period.Id}/tax-balances/CorporationTax/confirm-due";

    private static DefaultHttpContext ConfirmDueRequest(Company company, AccountingPeriod period, string role)
    {
        var context = OwnerRequest(company, HttpMethods.Post, ConfirmDuePath(company, period));
        context.Items[AuthContext.ItemKey] = Owner(company) with { Role = role, DisplayName = $"Example {role}" };
        return context;
    }
}
