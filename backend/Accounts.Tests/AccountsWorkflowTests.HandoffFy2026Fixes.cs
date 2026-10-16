using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Accounts.Tests;

// Defects found in live use on 10–16 October 2026 (HANDOFF-2026-10-16): the fixes and the guard that
// keeps a comparative column from printing as zeros.
public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task Readiness_BlocksWhilePriorPeriodLedgerIsEmpty_UntilItsFiguresAreEntered()
    {
        // The accounts package prints the preceding period's ledger as the comparative column. A
        // preceding period set up from filed accounts but never populated printed every comparative
        // as zero with no warning.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: false);
        // The prior period itself follows recorded history (a 2023 period), as hOUR Timebank's FY2025
        // does: a company's very first period is not judged, so it may be taken on later.
        var earliest = new AccountingPeriod
        {
            CompanyId = period.CompanyId,
            PeriodStart = new DateOnly(2023, 1, 1),
            PeriodEnd = new DateOnly(2023, 12, 31),
            IsFirstYear = true
        };
        var prior = new AccountingPeriod
        {
            CompanyId = period.CompanyId,
            PeriodStart = new DateOnly(2024, 1, 1),
            PeriodEnd = new DateOnly(2024, 12, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(earliest, prior);
        await db.SaveChangesAsync();
        // A take-on period: its own opening balances prove the prior year closed with balances.
        var bank = new AccountCategory
        {
            CompanyId = period.CompanyId,
            Code = "1200",
            Name = "Bank current account",
            Type = AccountCategoryType.Asset,
            StatementLine = AccountStatementLine.CashAtBank,
            SystemRole = AccountSystemRole.BankControl
        };
        db.AccountCategories.Add(bank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = period.Id, AccountCategoryId = bank.Id, Debit = 250m, Credit = 0m, SourceNote = "Filed accounts", Reviewed = true });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        var blocked = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        var blocker = Assert.Single(blocked.MissingItems, item => item.StartsWith("Prior-period comparatives missing", StringComparison.Ordinal));
        Assert.Contains("1 Jan 2024 to 31 Dec 2024", blocker);
        Assert.Contains(blocker, await statements.GetFinalOutputReadinessBlockersAsync(period.CompanyId, period.Id));

        // Entering the prior period's figures (here one opening balance) clears the blocker.
        var reserves = new AccountCategory
        {
            CompanyId = period.CompanyId,
            Code = "3100",
            Name = "Retained Earnings",
            Type = AccountCategoryType.Equity,
            StatementLine = AccountStatementLine.Reserves,
            SystemRole = AccountSystemRole.RetainedEarnings
        };
        db.AccountCategories.Add(reserves);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = prior.Id, AccountCategoryId = reserves.Id, Debit = 0m, Credit = 250m, SourceNote = "Filed accounts", Reviewed = true });
        await db.SaveChangesAsync();

        var cleared = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(cleared.MissingItems, item => item.StartsWith("Prior-period comparatives missing", StringComparison.Ordinal));

        // A first period has no comparative column and is never asked for one.
        var first = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var firstScore = await statements.GetReadinessScoreAsync(first.CompanyId, first.Id);
        Assert.DoesNotContain(firstScore.MissingItems, item => item.StartsWith("Prior-period comparatives missing", StringComparison.Ordinal));

        // A second period whose only predecessor is the company's first period is not judged either:
        // the first year may be taken on later (the golden corpus adopts from year two this way).
        var second = new AccountingPeriod
        {
            CompanyId = first.CompanyId,
            PeriodStart = first.PeriodEnd.AddDays(1),
            PeriodEnd = first.PeriodEnd.AddYears(1),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(second);
        await db.SaveChangesAsync();
        var secondBank = new AccountCategory
        {
            CompanyId = first.CompanyId,
            Code = "1200",
            Name = "Bank current account",
            Type = AccountCategoryType.Asset,
            StatementLine = AccountStatementLine.CashAtBank,
            SystemRole = AccountSystemRole.BankControl
        };
        db.AccountCategories.Add(secondBank);
        await db.SaveChangesAsync();
        db.OpeningBalances.Add(new OpeningBalance { PeriodId = second.Id, AccountCategoryId = secondBank.Id, Debit = 100m, Credit = 0m, SourceNote = "Take-on", Reviewed = true });
        await db.SaveChangesAsync();
        var secondScore = await statements.GetReadinessScoreAsync(second.CompanyId, second.Id);
        Assert.DoesNotContain(secondScore.MissingItems, item => item.StartsWith("Prior-period comparatives missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GoingConcern_RejectsUnknownOrEmptyBodies_WithoutTouchingTheRecord()
    {
        // Defect: a body such as {goingConcernConfirmed, goingConcernNote} (the names the GET returns)
        // bound to confirmed=false / note=null and wiped the record with a 200.
        var databaseName = Guid.NewGuid().ToString();
        var (app, client, companyId, periodId) = await StartYearEndHostAsync(databaseName, async (db, period) =>
        {
            period.GoingConcernConfirmed = true;
            period.GoingConcernNote = "Material uncertainty note that must survive a bad request.";
            await db.SaveChangesAsync();
        });
        try
        {
            var route = $"/api/companies/{companyId}/periods/{periodId}/going-concern";

            var renamed = await client.PutAsync(route, Json("""{"goingConcernConfirmed": false, "goingConcernNote": null}"""));
            await ExpectStatusAsync(renamed, HttpStatusCode.BadRequest);
            var renamedBody = await renamed.Content.ReadAsStringAsync();
            Assert.Contains("Unknown field 'goingConcernConfirmed'", renamedBody);
            Assert.Contains("confirmed (boolean, required)", renamedBody);
            Assert.Contains("note (string, optional)", renamedBody);

            var empty = await client.PutAsync(route, Json("{}"));
            await ExpectStatusAsync(empty, HttpStatusCode.BadRequest);
            Assert.Contains("'confirmed' is required", await empty.Content.ReadAsStringAsync());

            var noBody = await client.PutAsync(route, Json(""));
            await ExpectStatusAsync(noBody, HttpStatusCode.BadRequest);
            Assert.Contains("empty", await noBody.Content.ReadAsStringAsync());

            var wrongType = await client.PutAsync(route, Json("""{"confirmed": "yes"}"""));
            await ExpectStatusAsync(wrongType, HttpStatusCode.BadRequest);
            Assert.Contains("'confirmed' must be true or false", await wrongType.Content.ReadAsStringAsync());

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var untouched = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
                Assert.True(untouched.GoingConcernConfirmed);
                Assert.Equal("Material uncertainty note that must survive a bad request.", untouched.GoingConcernNote);
            }

            var accepted = await client.PutAsync(route, Json("""{"confirmed": false, "note": "  Revised note.  "}"""));
            await ExpectStatusAsync(accepted, HttpStatusCode.OK);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                var updated = await db.AccountingPeriods.AsNoTracking().SingleAsync(p => p.Id == periodId);
                Assert.False(updated.GoingConcernConfirmed);
                Assert.Equal("Revised note.", updated.GoingConcernNote);
            }
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TaxBalance_UnknownTaxType_IsRefusedWithTheAllowedValues()
    {
        // Defect: the VAT and PAYE Save buttons sent "VAT" / "PAYE_PRSI"; the server replied with an
        // empty 400 that the client could only show as "Invalid request".
        var (app, client, companyId, periodId) = await StartYearEndHostAsync(Guid.NewGuid().ToString());
        try
        {
            var body = new { liability = 120m, paid = 20m, balance = 100m };
            var unknown = await client.PutAsJsonAsync($"/api/companies/{companyId}/periods/{periodId}/tax-balances/PAYE_PRSI", body);
            await ExpectStatusAsync(unknown, HttpStatusCode.BadRequest);
            var error = await unknown.Content.ReadAsStringAsync();
            Assert.Contains("PAYE_PRSI", error);
            Assert.Contains("Allowed values: CorporationTax, Vat, Paye, Rct", error);

            var accepted = await client.PutAsJsonAsync($"/api/companies/{companyId}/periods/{periodId}/tax-balances/Paye", body);
            await ExpectStatusAsync(accepted, HttpStatusCode.OK);
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var saved = await db.TaxBalances.AsNoTracking().SingleAsync(t => t.PeriodId == periodId);
            Assert.Equal((TaxType.Paye, 120m, 20m, 100m), (saved.TaxType, saved.Liability, saved.Paid, saved.Balance));
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Inventory_UnknownValuationMethod_IsRefusedWithTheAllowedValues()
    {
        // Defect: the form sent FIFO / WeightedAverage / LIFO; the server refused all three with an
        // empty 400. LIFO is not permitted under FRS 102 / FRS 105 in any case.
        var (app, client, companyId, periodId) = await StartYearEndHostAsync(Guid.NewGuid().ToString());
        try
        {
            var route = $"/api/companies/{companyId}/periods/{periodId}/inventory";
            var refused = await client.PostAsync(route, Json("""{"description": "Goods for resale", "value": 100, "valuationMethod": "FIFO"}"""));
            await ExpectStatusAsync(refused, HttpStatusCode.BadRequest);
            var error = await refused.Content.ReadAsStringAsync();
            Assert.Contains("valuationMethod", error);
            Assert.Contains("Allowed values: Cost, NetRealisableValue, LowerOfCostAndNrv", error);

            var accepted = await client.PostAsync(route, Json("""{"description": "Goods for resale", "value": 100, "valuationMethod": "LowerOfCostAndNrv"}"""));
            await ExpectStatusAsync(accepted, HttpStatusCode.Created);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Disclosures_ContingentLiabilityAndPostBalanceSheetEvent_CanBeCorrectedInPlace()
    {
        // Defect: both had DELETE but no PUT, so a stale entry could only be deleted and re-added.
        int contingencyId = 0;
        int eventId = 0;
        var (app, client, companyId, periodId) = await StartYearEndHostAsync(Guid.NewGuid().ToString(), async (db, period) =>
        {
            var contingency = new ContingentLiability { PeriodId = period.Id, Description = "GRENKE termination claim", Nature = "Legal Claim", EstimatedAmount = 19_771.08m, Likelihood = "Probable" };
            var evt = new PostBalanceSheetEvent { PeriodId = period.Id, Description = "OpenAI subscription cancelled", EventDate = new DateOnly(2026, 8, 15), IsAdjusting = false, FinancialImpact = 221.40m };
            db.ContingentLiabilities.Add(contingency);
            db.PostBalanceSheetEvents.Add(evt);
            await db.SaveChangesAsync();
            contingencyId = contingency.Id;
            eventId = evt.Id;
        });
        try
        {
            var contingencyRoute = $"/api/companies/{companyId}/periods/{periodId}/contingent-liabilities/{contingencyId}";
            var corrected = await client.PutAsJsonAsync(contingencyRoute, new { description = "GRENKE terminated agreement 069-24440", nature = "Legal Claim", estimatedAmount = 19_771.08m, likelihood = "Remote" });
            await ExpectStatusAsync(corrected, HttpStatusCode.OK);

            var eventRoute = $"/api/companies/{companyId}/periods/{periodId}/post-balance-sheet-events/{eventId}";
            var correctedEvent = await client.PutAsJsonAsync(eventRoute, new { description = "OpenAI subscription cancelled; no further charges", eventDate = "2026-09-01", isAdjusting = false, financialImpact = (decimal?)null });
            await ExpectStatusAsync(correctedEvent, HttpStatusCode.OK);

            var missing = await client.PutAsJsonAsync($"/api/companies/{companyId}/periods/{periodId}/contingent-liabilities/999999", new { description = "x", nature = "Other", likelihood = "Remote" });
            await ExpectStatusAsync(missing, HttpStatusCode.NotFound);

            var blank = await client.PutAsJsonAsync(contingencyRoute, new { description = "  ", nature = "Other", likelihood = "Remote" });
            await ExpectStatusAsync(blank, HttpStatusCode.BadRequest);

            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var contingency = await db.ContingentLiabilities.AsNoTracking().SingleAsync(c => c.Id == contingencyId);
            Assert.Equal(("GRENKE terminated agreement 069-24440", "Remote", 19_771.08m), (contingency.Description, contingency.Likelihood, contingency.EstimatedAmount));
            var evt = await db.PostBalanceSheetEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);
            Assert.Equal(("OpenAI subscription cancelled; no further charges", new DateOnly(2026, 9, 1), (decimal?)null), (evt.Description, evt.EventDate, evt.FinancialImpact));
            Assert.Equal(1, await db.AuditLogs.CountAsync(log => log.Action == AuditEventCodes.ContingentLiabilityUpdated));
            Assert.Equal(1, await db.AuditLogs.CountAsync(log => log.Action == AuditEventCodes.PostBalanceSheetEventUpdated));
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task CharitableTaxExemption_SuppressesTheScopeQuestionnaire_AndStatesThePolicy()
    {
        // Defect: with the exemption unrecorded the CT scope questionnaire showed as a warning and a
        // custom "Taxation" note was refused. While the exemption stands (and no liability is confirmed
        // as due) the questionnaire is not asked for and the accounting policies note states the exemption.
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        db.FilingRegimes.Add(new FilingRegime { PeriodId = period.Id, ElectedRegime = ElectedRegime.Micro, CanUseMicro = true, CanFileAbridged = true, AuditExempt = true });
        db.TaxBalances.Add(new TaxBalance { PeriodId = period.Id, TaxType = TaxType.CorporationTax, Liability = 0m, Paid = 0m, Balance = 0m });
        await db.SaveChangesAsync();
        var statements = new FinancialStatementsService(db);

        var withoutExemption = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.Contains(withoutExemption.Warnings, warning => warning.Contains("scope review", StringComparison.Ordinal));

        var company = await db.Companies.SingleAsync(c => c.Id == period.CompanyId);
        company.IsCharitableOrganisation = true;
        company.HoldsCharitableTaxExemption = true;
        company.CharitableTaxExemptionReference = "ROS enquiry 2109-57763";
        company.CharitableTaxExemptionConfirmedDate = new DateOnly(2022, 12, 16);
        await db.SaveChangesAsync();

        var withExemption = await statements.GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(withExemption.Warnings, warning => warning.Contains("scope review", StringComparison.Ordinal));
        Assert.DoesNotContain(await statements.GetPreFilingConsistencyIssuesAsync(period.CompanyId, period.Id), issue => issue.Contains("corporation-tax charge is blocked", StringComparison.Ordinal));

        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        var policies = await db.NotesDisclosures.AsNoTracking().SingleAsync(note => note.PeriodId == period.Id && note.Code == StatutoryNoteCodes.AccountingPolicies);
        Assert.Contains("charitable tax exemption from the Revenue Commissioners (reference ROS enquiry 2109-57763, confirmed 16 December 2022)", policies.Content);
        Assert.DoesNotContain("Current corporation tax is recognised by reference to taxable profits", policies.Content);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // Asserts the status and, on a mismatch, shows the response body so a 500's message is not lost.
    private static async Task<string> ExpectStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(expected == response.StatusCode, $"Expected {expected} but got {response.StatusCode}: {body}");
        return body;
    }

    // A loopback host over an in-memory database with the year-end endpoints and the exception
    // middleware, so binding failures surface exactly as the deployed API reports them.
    private static async Task<(WebApplication App, HttpClient Client, int CompanyId, int PeriodId)> StartYearEndHostAsync(
        string databaseName,
        Func<AccountsDbContext, AccountingPeriod, Task>? seed = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
        // The Development environment already throws on a binding failure (RouteHandlerOptions), which is
        // what Program.cs now sets for every deployment mode; ExceptionMiddleware turns it into the named 400.
        builder.Services.AddHttpContextAccessor();
        // Program.cs accepts string enum values in request bodies; the host must too.
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.Configure<ImportLimitConfig>(_ => { });
        builder.Services.AddSingleton<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<CategoryService>();
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DeadlineService>();
        builder.Services.AddScoped<NotesDisclosureService>();
        builder.Services.AddScoped<DirectorLoanComplianceService>();

        var app = builder.Build();
        int companyId;
        int periodId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
            if (seed is not null)
                await seed(db, period);
            companyId = period.CompanyId;
            periodId = period.Id;
        }

        app.UseMiddleware<ExceptionMiddleware>();
        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = AuthenticatedRole("Owner");
            await next();
        });
        app.MapYearEndEndpoints();
        await app.StartAsync();
        var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
        return (app, client, companyId, periodId);
    }
}
