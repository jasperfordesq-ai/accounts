using Accounts.Api.Endpoints;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Middleware;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Accounts.Tests;

public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task AdjustmentRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config =>
        {
            config.Enabled = true;
            config.Keys =
            [
                new ApiAccessKeyConfig
                {
                    Name = "Reader",
                    Role = "Reader",
                    DevelopmentKey = "reader",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Writer",
                    Role = "Writer",
                    DevelopmentKey = "writer",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.Configure<AuditIntegrityConfig>(config =>
        {
            config.ActiveKeyId = "audit-key-2026";
            config.SigningKeys =
            [
                new AuditIntegritySigningKeyConfig
                {
                    KeyId = "audit-key-2026",
                    SigningKey = StrongAuditCheckpointSigningKey()
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<AdjustmentService>();
        builder.Services.AddScoped<AuditIntegrityService>();
        builder.Services.AddScoped<AuditIntegrityCheckpointService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int debitCategoryId;
        int creditCategoryId;
        int approvalAdjustmentId;
        int deleteAdjustmentId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            var debit = AddCategory(db, allowedCompany.Id, "6813", "Audit fees", AccountCategoryType.Expense);
            var credit = AddCategory(db, allowedCompany.Id, "2103", "Accruals", AccountCategoryType.Liability);
            var approvalAdjustment = new Adjustment
            {
                PeriodId = allowedPeriod.Id,
                Description = "Approval accrual",
                DebitCategoryId = debit.Id,
                CreditCategoryId = credit.Id,
                Amount = 500m,
                Reason = "Year-end",
                LegalBasis = "FRS 102",
                ImpactOnProfit = -500m,
                ImpactOnAssets = 0m,
                CreatedBy = "Accountant"
            };
            var deleteAdjustment = new Adjustment
            {
                PeriodId = allowedPeriod.Id,
                Description = "Delete accrual",
                DebitCategoryId = debit.Id,
                CreditCategoryId = credit.Id,
                Amount = 300m,
                Reason = "Year-end",
                LegalBasis = "FRS 102",
                ImpactOnProfit = -300m,
                ImpactOnAssets = 0m,
                CreatedBy = "Accountant"
            };
            db.Adjustments.AddRange(approvalAdjustment, deleteAdjustment);
            for (var i = 0; i < 125; i++)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = allowedCompany.Id,
                    PeriodId = allowedPeriod.Id,
                    EntityType = "SyntheticAuditEvidence",
                    EntityId = i + 1,
                    Action = "SyntheticAudit",
                    OldValueJson = "{\"internal\":\"old\"}",
                    NewValueJson = "{\"internal\":\"new\"}",
                    UserId = "accountant@tenant-a.test",
                    RequestId = $"audit-{i + 1}",
                    ActorDisplayName = "Internal Accountant",
                    IntegrityHash = $"hash-{i + 1}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            debitCategoryId = debit.Id;
            creditCategoryId = credit.Id;
            approvalAdjustmentId = approvalAdjustment.Id;
            deleteAdjustmentId = deleteAdjustment.Id;
        }

        app.Use(async (context, next) =>
        {
            var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Accountant";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: $"{role.ToLowerInvariant()}@tenant-a.test",
                DisplayName: $"Tenant A {role}",
                Role: role,
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapAdjustmentEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var createBody = new
            {
                description = "Manual accrual",
                debitCategoryId,
                creditCategoryId,
                amount = 100m,
                reason = "Year-end",
                legalBasis = "FRS 102",
                impactOnProfit = -100m,
                impactOnAssets = 0m
            };

            using var hiddenListRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/adjustments/",
                role: "Client");
            var hiddenList = await client.SendAsync(hiddenListRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenList.StatusCode);

            using var hiddenAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/audit-log/",
                role: "Client");
            var hiddenAudit = await client.SendAsync(hiddenAuditRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenAudit.StatusCode);

            using var clientAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/",
                role: "Client");
            var clientAuditDenied = await client.SendAsync(clientAuditRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientAuditDenied.StatusCode);

            using var clientIntegrityRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/integrity",
                role: "Client");
            var clientIntegrityDenied = await client.SendAsync(clientIntegrityRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientIntegrityDenied.StatusCode);

            using var clientCheckpointRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/integrity/checkpoints/latest",
                role: "Client");
            var clientCheckpointDenied = await client.SendAsync(clientCheckpointRequest);
            Assert.Equal(HttpStatusCode.Forbidden, clientCheckpointDenied.StatusCode);

            using var oversizedAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/?page=0&pageSize=5000",
                role: "Accountant");
            var oversizedAuditResponse = await client.SendAsync(oversizedAuditRequest);
            Assert.Equal(HttpStatusCode.OK, oversizedAuditResponse.StatusCode);
            var oversizedAuditJson = await oversizedAuditResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(100, oversizedAuditJson.GetProperty("items").GetArrayLength());
            Assert.Equal(125, oversizedAuditJson.GetProperty("total").GetInt32());

            using var hugePageAuditRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/audit-log/?page=2147483647&pageSize=100",
                role: "Accountant");
            var hugePageAuditResponse = await client.SendAsync(hugePageAuditRequest);
            Assert.Equal(HttpStatusCode.OK, hugePageAuditResponse.StatusCode);
            var hugePageAuditJson = await hugePageAuditResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(25, hugePageAuditJson.GetProperty("items").GetArrayLength());
            Assert.Equal(125, hugePageAuditJson.GetProperty("total").GetInt32());
            Assert.Equal(2, hugePageAuditJson.GetProperty("page").GetInt32());
            Assert.Equal(2, hugePageAuditJson.GetProperty("totalPages").GetInt32());
            Assert.True(hugePageAuditJson.GetProperty("hasPreviousPage").GetBoolean());
            Assert.False(hugePageAuditJson.GetProperty("hasNextPage").GetBoolean());

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Reviewer",
                apiKey: "writer",
                body: createBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Accountant",
                apiKey: "reader",
                body: createBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/",
                role: "Accountant",
                apiKey: "writer",
                body: createBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var accountantApprove = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{approvalAdjustmentId}/approve",
                role: "Accountant",
                apiKey: "writer");
            var accountantApproveDenied = await client.SendAsync(accountantApprove);
            Assert.Equal(HttpStatusCode.Forbidden, accountantApproveDenied.StatusCode);

            using var reviewerApprove = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{approvalAdjustmentId}/approve",
                role: "Reviewer",
                apiKey: "writer");
            var reviewerApproveAllowed = await client.SendAsync(reviewerApprove);
            Assert.Equal(HttpStatusCode.OK, reviewerApproveAllowed.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{deleteAdjustmentId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var lockedGenerate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/adjustments/generate",
                role: "Accountant",
                apiKey: "writer");
            var lockedGenerateDenied = await client.SendAsync(lockedGenerate);
            Assert.Equal(HttpStatusCode.Conflict, lockedGenerateDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/adjustments/{deleteAdjustmentId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Equal(2, await verifyDb.Adjustments.CountAsync(a => a.PeriodId == allowedPeriodId));
            Assert.False(await verifyDb.Adjustments.AnyAsync(a => a.PeriodId == lockedPeriodId));
            var approved = await verifyDb.Adjustments.SingleAsync(a => a.Id == approvalAdjustmentId);
            Assert.Equal("Tenant A Reviewer", approved.ApprovedBy);
            Assert.False(await verifyDb.Adjustments.AnyAsync(a => a.Id == deleteAdjustmentId));
            Assert.Equal(3, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "Adjustment"));
        }
        finally
        {
            await app.StopAsync();
        }

        static HttpRequestMessage Request(HttpMethod method, string path, string role, string? apiKey = null, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            if (apiKey is not null)
                request.Headers.Add("X-Accounts-Api-Key", apiKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }
    }

    [Fact]
    public async Task TenantIsolation_CompanyAndPeriodAccessIsScopedToCallersTenant()
    {
        // BL-10: behavioural guard that company/period access is filtered to the caller's tenant at the
        // data-access query layer, so a cross-tenant id is invisible (the endpoint then returns 404).
        await using var db = CreateDbContext();
        var companyA = new Company { TenantId = 1, LegalName = "Tenant A Ltd", CroNumber = "100001", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), AnnualReturnDate = new DateOnly(2025, 12, 15), RegisteredOfficeAddress1 = "1 A Street", RegisteredOfficeCity = "Dublin", RegisteredOfficeCounty = "Dublin" };
        var companyB = new Company { TenantId = 2, LegalName = "Tenant B Ltd", CroNumber = "100002", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), AnnualReturnDate = new DateOnly(2025, 12, 15), RegisteredOfficeAddress1 = "1 B Street", RegisteredOfficeCity = "Cork", RegisteredOfficeCounty = "Cork" };
        db.Companies.AddRange(companyA, companyB);
        await db.SaveChangesAsync();
        var periodB = new AccountingPeriod { CompanyId = companyB.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
        db.AccountingPeriods.Add(periodB);
        await db.SaveChangesAsync();

        var tenantAContext = new DefaultHttpContext();
        tenantAContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(7, 1, "Firm A", "owner@a.ie", "Owner", "Owner");

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(tenantAContext, db, companyA.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(tenantAContext, db, companyB.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(tenantAContext, db, companyB.Id, periodB.Id));
    }

    [Fact]
    public async Task TenantIsolation_DbContextCompanyQueryFilterBackstopsRequestScopedQueries()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        await using (var seedDb = CreateDbContext(databaseName, root))
        {
            var tenantACompany = new Company { TenantId = 1, LegalName = "Tenant A Ltd", CroNumber = "100001", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), AnnualReturnDate = new DateOnly(2025, 12, 15), RegisteredOfficeAddress1 = "1 A Street", RegisteredOfficeCity = "Dublin", RegisteredOfficeCounty = "Dublin" };
            var tenantBCompany = new Company { TenantId = 2, LegalName = "Tenant B Ltd", CroNumber = "100002", CompanyType = CompanyType.Private, IncorporationDate = new DateOnly(2025, 1, 1), AnnualReturnDate = new DateOnly(2025, 12, 15), RegisteredOfficeAddress1 = "1 B Street", RegisteredOfficeCity = "Cork", RegisteredOfficeCounty = "Cork" };
            seedDb.Companies.AddRange(tenantACompany, tenantBCompany);
            await seedDb.SaveChangesAsync();

            var tenantAPeriod = new AccountingPeriod { CompanyId = tenantACompany.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
            var tenantBPeriod = new AccountingPeriod { CompanyId = tenantBCompany.Id, PeriodStart = new DateOnly(2025, 1, 1), PeriodEnd = new DateOnly(2025, 12, 31), IsFirstYear = true };
            var tenantABank = new BankAccount { CompanyId = tenantACompany.Id, Name = "Tenant A Bank", OpeningBalanceDate = new DateOnly(2025, 1, 1) };
            var tenantBBank = new BankAccount { CompanyId = tenantBCompany.Id, Name = "Tenant B Bank", OpeningBalanceDate = new DateOnly(2025, 1, 1) };
            var tenantACategory = new AccountCategory { CompanyId = tenantACompany.Id, Code = "A100", Name = "Tenant A Sales", Type = AccountCategoryType.Income };
            var tenantBCategory = new AccountCategory { CompanyId = tenantBCompany.Id, Code = "B100", Name = "Tenant B Sales", Type = AccountCategoryType.Income };
            var tenantAFixedAsset = new FixedAsset { CompanyId = tenantACompany.Id, Name = "Tenant A Laptop", Category = "Office", Cost = 1000m, AcquisitionDate = new DateOnly(2025, 2, 1) };
            var tenantBFixedAsset = new FixedAsset { CompanyId = tenantBCompany.Id, Name = "Tenant B Laptop", Category = "Office", Cost = 1000m, AcquisitionDate = new DateOnly(2025, 2, 1) };
            var tenantALoan = new Loan { CompanyId = tenantACompany.Id, Lender = "Tenant A Lender", OriginalAmount = 5000m, Balance = 5000m, DrawdownDate = new DateOnly(2025, 1, 1), BalanceAsOfDate = new DateOnly(2025, 12, 31) };
            var tenantBLoan = new Loan { CompanyId = tenantBCompany.Id, Lender = "Tenant B Lender", OriginalAmount = 5000m, Balance = 5000m, DrawdownDate = new DateOnly(2025, 1, 1), BalanceAsOfDate = new DateOnly(2025, 12, 31) };
            var tenantAUser = new UserAccount { TenantId = 1, Email = "owner@tenant-a.ie", DisplayName = "Tenant A Owner", Role = "Owner", PasswordHash = "hash", PasswordSalt = "salt", PasswordAlgorithm = "test" };
            var tenantBUser = new UserAccount { TenantId = 2, Email = "owner@tenant-b.ie", DisplayName = "Tenant B Owner", Role = "Owner", PasswordHash = "hash", PasswordSalt = "salt", PasswordAlgorithm = "test" };

            seedDb.AddRange(
                tenantAPeriod,
                tenantBPeriod,
                new CompanyOfficer { CompanyId = tenantACompany.Id, Name = "Tenant A Director", Role = OfficerRole.Director, AppointedDate = new DateOnly(2025, 1, 1) },
                new CompanyOfficer { CompanyId = tenantBCompany.Id, Name = "Tenant B Director", Role = OfficerRole.Director, AppointedDate = new DateOnly(2025, 1, 1) },
                tenantABank,
                tenantBBank,
                tenantACategory,
                tenantBCategory,
                tenantAFixedAsset,
                tenantBFixedAsset,
                tenantALoan,
                tenantBLoan,
                new ShareCapital { CompanyId = tenantACompany.Id, ShareClass = "Tenant A Ordinary", IssueDate = new DateOnly(2025, 1, 1) },
                new ShareCapital { CompanyId = tenantBCompany.Id, ShareClass = "Tenant B Ordinary", IssueDate = new DateOnly(2025, 1, 1) },
                tenantAUser,
                tenantBUser);
            await seedDb.SaveChangesAsync();

            seedDb.AddRange(
                new FilingHistory { CompanyId = tenantACompany.Id, PeriodId = tenantAPeriod.Id, DeadlineType = DeadlineType.CRO, DueDate = new DateOnly(2026, 9, 30), FiledDate = new DateOnly(2026, 9, 1) },
                new FilingHistory { CompanyId = tenantBCompany.Id, PeriodId = tenantBPeriod.Id, DeadlineType = DeadlineType.CRO, DueDate = new DateOnly(2026, 9, 30), FiledDate = new DateOnly(2026, 9, 1) },
                new FilingDeadline { CompanyId = tenantACompany.Id, PeriodId = tenantAPeriod.Id, DeadlineType = DeadlineType.Revenue, DueDate = new DateOnly(2026, 9, 23) },
                new FilingDeadline { CompanyId = tenantBCompany.Id, PeriodId = tenantBPeriod.Id, DeadlineType = DeadlineType.Revenue, DueDate = new DateOnly(2026, 9, 23) },
                new Debtor { PeriodId = tenantAPeriod.Id, Name = "Tenant A Debtor", Amount = 100m },
                new Debtor { PeriodId = tenantBPeriod.Id, Name = "Tenant B Debtor", Amount = 100m },
                new ImportBatch { BankAccountId = tenantABank.Id, Filename = "tenant-a.csv" },
                new ImportBatch { BankAccountId = tenantBBank.Id, Filename = "tenant-b.csv" },
                new DepreciationEntry { AssetId = tenantAFixedAsset.Id, PeriodId = tenantAPeriod.Id, OpeningNbv = 1000m, Charge = 100m, ClosingNbv = 900m },
                new DepreciationEntry { AssetId = tenantBFixedAsset.Id, PeriodId = tenantBPeriod.Id, OpeningNbv = 1000m, Charge = 100m, ClosingNbv = 900m },
                new LoanBalanceSnapshot { LoanId = tenantALoan.Id, PeriodId = tenantAPeriod.Id, OpeningBalance = 5000m, ClosingBalance = 5000m },
                new LoanBalanceSnapshot { LoanId = tenantBLoan.Id, PeriodId = tenantBPeriod.Id, OpeningBalance = 5000m, ClosingBalance = 5000m },
                new UserCompanyAccess { UserId = tenantAUser.Id, CompanyId = tenantACompany.Id },
                new UserCompanyAccess { UserId = tenantBUser.Id, CompanyId = tenantBCompany.Id });
            await seedDb.SaveChangesAsync();
        }

        var tenantAContext = new DefaultHttpContext();
        tenantAContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(7, 1, "Firm A", "owner@a.ie", "Owner", "Owner");
        await using var requestDb = CreateDbContext(databaseName, root, tenantAContext);

        var visibleCompanies = await requestDb.Companies
            .OrderBy(c => c.LegalName)
            .Select(c => c.LegalName)
            .ToListAsync();

        Assert.Equal(["Tenant A Ltd"], visibleCompanies);
        Assert.Equal(["Tenant A Director"], await requestDb.CompanyOfficers.Select(o => o.Name).ToListAsync());
        Assert.Equal([new DateOnly(2025, 12, 31)], await requestDb.AccountingPeriods.Select(p => p.PeriodEnd).ToListAsync());
        Assert.Equal(["Tenant A Bank"], await requestDb.BankAccounts.Select(b => b.Name).ToListAsync());
        Assert.Equal(["Tenant A Sales"], await requestDb.AccountCategories.Select(c => c.Name).ToListAsync());
        Assert.Equal(["Tenant A Laptop"], await requestDb.FixedAssets.Select(a => a.Name).ToListAsync());
        Assert.Equal(["Tenant A Lender"], await requestDb.Loans.Select(l => l.Lender).ToListAsync());
        Assert.Equal(["Tenant A Ordinary"], await requestDb.ShareCapitals.Select(s => s.ShareClass).ToListAsync());
        Assert.Equal(["Tenant A Debtor"], await requestDb.Debtors.Select(d => d.Name).ToListAsync());
        Assert.Single(await requestDb.FilingDeadlines.ToListAsync());
        Assert.Single(await requestDb.FilingHistories.ToListAsync());
        Assert.Equal(["tenant-a.csv"], await requestDb.ImportBatches.Select(b => b.Filename).ToListAsync());
        Assert.Single(await requestDb.DepreciationEntries.ToListAsync());
        Assert.Single(await requestDb.LoanBalanceSnapshots.ToListAsync());
        Assert.Equal(["owner@tenant-a.ie"], await requestDb.UserAccounts.Select(u => u.Email).ToListAsync());
        Assert.Single(await requestDb.UserCompanyAccesses.ToListAsync());
        Assert.True(await requestDb.Companies.IgnoreQueryFilters().AnyAsync(c => c.LegalName == "Tenant B Ltd"));
        Assert.True(await requestDb.CompanyOfficers.IgnoreQueryFilters().AnyAsync(o => o.Name == "Tenant B Director"));
        Assert.True(await requestDb.Debtors.IgnoreQueryFilters().AnyAsync(d => d.Name == "Tenant B Debtor"));
        Assert.True(await requestDb.ImportBatches.IgnoreQueryFilters().AnyAsync(b => b.Filename == "tenant-b.csv"));
    }

    [Fact]
    public void TenantIsolation_QueryFiltersCoverRequiredDependentsOfFilteredEntities()
    {
        using var db = CreateDbContext();

        var missingDependentFilters = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys()
                .Where(foreignKey =>
                    foreignKey.IsRequired
                    && foreignKey.PrincipalEntityType.GetDeclaredQueryFilters().Any()
                    && !foreignKey.DeclaringEntityType.GetDeclaredQueryFilters().Any())
                .Select(foreignKey =>
                    $"{foreignKey.DeclaringEntityType.ClrType.Name}->{foreignKey.PrincipalEntityType.ClrType.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingDependentFilters);
    }

    [Fact]
    public async Task BankingEvidence_BulkCategoriseHidesMissingPeriodBeforeEmptySelectionAndRoleDenial()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var otherPeriod = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var category = AddCategory(db, period.CompanyId, "4008", "Sales", AccountCategoryType.Income);
        var audit = new AuditService(db);
        var context = AuthenticatedRequest("Client", HttpMethods.Post, $"/api/companies/{period.CompanyId}/periods/{otherPeriod.Id}/transactions/bulk-categorise");
        context.Items[AuthContext.ItemKey] = AuthenticatedRole("Client") with
        {
            AllowedCompanyIds = new HashSet<int> { period.CompanyId }
        };

        var result = await BankingEndpoints.BulkCategoriseTransactionsEndpointAsync(
            period.CompanyId,
            otherPeriod.Id,
            new BulkCategoriseInput([], category.Id),
            db,
            audit,
            context,
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
        Assert.Empty(await db.AuditLogs.Where(a => a.EntityType.StartsWith("ImportedTransaction")).ToListAsync());
    }

    [Fact]
    public async Task YearEndEvidence_DeniesReviewerPreparationWriteWithoutApiAccessService()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        var audit = new AuditService(db);
        var reviewerContext = AuthenticatedRequest(
            "Reviewer",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        var denied = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Reviewer debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            reviewerContext);

        Assert.Equal(StatusCodes.Status403Forbidden, ResultStatusCode(denied));
        Assert.Empty(await db.Debtors.Where(d => d.PeriodId == period.Id).ToListAsync());

        var accountantContext = AuthenticatedRequest(
            "Accountant",
            HttpMethods.Post,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors");

        var created = await YearEndEndpoints.CreateDebtorEndpointAsync(
            period.CompanyId,
            period.Id,
            new Debtor { Name = "Accountant debtor", Amount = 100m, Type = DebtorType.Other },
            db,
            audit,
            accountantContext);

        Assert.Equal(StatusCodes.Status201Created, ResultStatusCode(created));
        var debtor = await db.Debtors.SingleAsync(d => d.PeriodId == period.Id);
        Assert.Equal("Accountant debtor", debtor.Name);
    }

    [Fact]
    public async Task YearEndRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config =>
        {
            config.Enabled = true;
            config.Keys =
            [
                new ApiAccessKeyConfig
                {
                    Name = "Reader",
                    Role = "Reader",
                    DevelopmentKey = "reader",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Writer",
                    Role = "Writer",
                    DevelopmentKey = "writer",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<FinancialStatementsService>();
        builder.Services.AddScoped<DirectorLoanComplianceService>();
        builder.Services.AddScoped<NotesDisclosureService>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int deleteDebtorId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            await db.SaveChangesAsync();
            var deleteDebtor = new Debtor
            {
                PeriodId = allowedPeriod.Id,
                Name = "Delete debtor",
                Amount = 75m,
                Type = DebtorType.Trade
            };
            db.Debtors.AddRange(
                deleteDebtor,
                new Debtor
                {
                    PeriodId = restrictedPeriod.Id,
                    Name = "Hidden debtor",
                    Amount = 900m,
                    Type = DebtorType.Other
                });
            db.FixedAssets.Add(new FixedAsset
            {
                CompanyId = restrictedCompany.Id,
                Name = "Hidden laptop",
                Category = "Computer Equipment",
                Cost = 1_500m,
                AcquisitionDate = restrictedPeriod.PeriodStart,
                UsefulLifeYears = 3,
                DepreciationMethod = DepreciationMethod.StraightLine
            });
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            deleteDebtorId = deleteDebtor.Id;
        }

        app.Use(async (context, next) =>
        {
            var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Accountant";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: $"{role.ToLowerInvariant()}@tenant-a.test",
                DisplayName: $"Tenant A {role}",
                Role: role,
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapYearEndEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var debtorBody = new
            {
                name = "Runtime debtor",
                amount = 100m,
                type = DebtorType.Trade,
                notes = "Invoice support"
            };
            var reviewBody = new
            {
                confirmed = true,
                note = "Reviewed by file reviewer."
            };

            using var hiddenDebtorsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/debtors/",
                role: "Client");
            var hiddenDebtors = await client.SendAsync(hiddenDebtorsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenDebtors.StatusCode);

            using var hiddenAssetsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/fixed-assets/",
                role: "Client");
            var hiddenAssets = await client.SendAsync(hiddenAssetsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenAssets.StatusCode);

            using var hiddenLoansRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/loans/",
                role: "Client");
            var hiddenLoans = await client.SendAsync(hiddenLoansRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenLoans.StatusCode);

            using var hiddenSharesRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/share-capital/",
                role: "Client");
            var hiddenShares = await client.SendAsync(hiddenSharesRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenShares.StatusCode);

            using var hiddenSummaryRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/year-end-summary",
                role: "Client");
            var hiddenSummary = await client.SendAsync(hiddenSummaryRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSummary.StatusCode);

            using var hiddenNotesRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/notes/",
                role: "Client");
            var hiddenNotes = await client.SendAsync(hiddenNotesRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenNotes.StatusCode);

            using var mismatchedVisibleCompanyPeriodRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/going-concern/",
                role: "Client");
            var mismatchedVisibleCompanyPeriod = await client.SendAsync(mismatchedVisibleCompanyPeriodRequest);
            Assert.Equal(HttpStatusCode.NotFound, mismatchedVisibleCompanyPeriod.StatusCode);

            using var hiddenComplianceRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/director-loans/compliance",
                role: "Client");
            var hiddenCompliance = await client.SendAsync(hiddenComplianceRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenCompliance.StatusCode);

            using var hiddenSection307Request = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/director-loans/section-307-note",
                role: "Client");
            var hiddenSection307 = await client.SendAsync(hiddenSection307Request);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSection307.StatusCode);

            using var hiddenWriterCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/debtors/",
                role: "Client",
                apiKey: "writer",
                body: debtorBody);
            var hiddenWriterCreateDenied = await client.SendAsync(hiddenWriterCreate);
            Assert.Equal(HttpStatusCode.NotFound, hiddenWriterCreateDenied.StatusCode);

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Reviewer",
                apiKey: "writer",
                body: debtorBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "reader",
                body: debtorBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "writer",
                body: debtorBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var accountantReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Accountant",
                apiKey: "writer",
                body: reviewBody);
            var accountantReviewDenied = await client.SendAsync(accountantReview);
            Assert.Equal(HttpStatusCode.Forbidden, accountantReviewDenied.StatusCode);

            using var readerReviewerReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "reader",
                body: reviewBody);
            var readerReviewerReviewDenied = await client.SendAsync(readerReviewerReview);
            Assert.Equal(HttpStatusCode.Unauthorized, readerReviewerReviewDenied.StatusCode);

            using var reviewerReview = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "writer",
                body: reviewBody);
            var reviewerReviewAllowed = await client.SendAsync(reviewerReview);
            Assert.Equal(HttpStatusCode.OK, reviewerReviewAllowed.StatusCode);

            using var lockedCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/debtors/",
                role: "Accountant",
                apiKey: "writer",
                body: debtorBody);
            var lockedCreateDenied = await client.SendAsync(lockedCreate);
            Assert.Equal(HttpStatusCode.Conflict, lockedCreateDenied.StatusCode);

            using var lockedReview = Request(
                HttpMethod.Put,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/year-end-reviews/tax",
                role: "Reviewer",
                apiKey: "writer",
                body: reviewBody);
            var lockedReviewDenied = await client.SendAsync(lockedReview);
            Assert.Equal(HttpStatusCode.Conflict, lockedReviewDenied.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/{deleteDebtorId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/debtors/{deleteDebtorId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            Assert.Single(await verifyDb.Debtors.Where(d => d.PeriodId == allowedPeriodId).ToListAsync());
            Assert.Single(await verifyDb.Debtors.Where(d => d.PeriodId == restrictedPeriodId).ToListAsync());
            Assert.False(await verifyDb.Debtors.AnyAsync(d => d.PeriodId == lockedPeriodId));
            var review = await verifyDb.YearEndReviewConfirmations.SingleAsync(r => r.PeriodId == allowedPeriodId && r.SectionKey == "tax");
            Assert.True(review.Confirmed);
            Assert.Equal(2, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "Debtor"));
            Assert.Single(await verifyDb.AuditLogs.Where(a => a.EntityType == "YearEndReviewConfirmation").ToListAsync());
        }
        finally
        {
            await app.StopAsync();
        }

        static HttpRequestMessage Request(HttpMethod method, string path, string role, string? apiKey = null, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            if (apiKey is not null)
                request.Headers.Add("X-Accounts-Api-Key", apiKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }
    }

    [Fact]
    public void ApiAccess_AllowsKeyForConfiguredCompanyOnly()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Firm A",
                        KeyHash = ApiAccessService.HashKey("secret-a"),
                        AllowedCompanyIds = [10]
                    }
                ]
            }),
            new TestEnvironment("Production"));

        var allowed = service.Authorize("secret-a", new PathString("/api/companies/10/periods"));
        var denied = service.Authorize("secret-a", new PathString("/api/companies/11/periods"));
        var invalid = service.Authorize("wrong", new PathString("/api/companies/10/periods"));

        Assert.True(allowed.IsAllowed);
        Assert.False(denied.IsAllowed);
        Assert.False(invalid.IsAllowed);
        Assert.Contains("not authorised", denied.DenialReason);
    }

    [Fact]
    public void ApiAccess_EnforcesRolesForWritesAndAdminActions()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys =
                [
                    new ApiAccessKeyConfig
                    {
                        Name = "Read only",
                        Role = "Reader",
                        DevelopmentKey = "reader",
                        AllowedCompanyIds = [10]
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Writer",
                        Role = "Writer",
                        DevelopmentKey = "writer",
                        AllowedCompanyIds = [10]
                    },
                    new ApiAccessKeyConfig
                    {
                        Name = "Admin",
                        Role = "Admin",
                        DevelopmentKey = "admin"
                    }
                ]
            }),
            new TestEnvironment("Development"));

        var readerWrite = service.Authorize("reader", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerWrite = service.Authorize("writer", new PathString("/api/companies/10/periods"), HttpMethods.Post);
        var writerDelete = service.Authorize("writer", new PathString("/api/companies/10"), HttpMethods.Delete);
        var scopedCompanyCreate = service.Authorize("writer", new PathString("/api/companies"), HttpMethods.Post);
        var adminCompanyCreate = service.Authorize("admin", new PathString("/api/companies"), HttpMethods.Post);

        Assert.False(readerWrite.IsAllowed);
        Assert.Contains("read-only", readerWrite.DenialReason);
        Assert.True(writerWrite.IsAllowed);
        Assert.False(writerDelete.IsAllowed);
        Assert.Contains("administrative", writerDelete.DenialReason);
        Assert.False(scopedCompanyCreate.IsAllowed);
        Assert.Contains("administrative", scopedCompanyCreate.DenialReason);
        Assert.True(adminCompanyCreate.IsAllowed);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ApiAccess_RejectsDevelopmentKeysOutsideDevelopment(string environmentName)
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Dev key", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment(environmentName));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("DevelopmentKey"));
    }

    [Fact]
    public void ApiAccess_MatchesDevelopmentKeysOnlyInDevelopment()
    {
        var config = new ApiAccessConfig
        {
            Enabled = true,
            Keys = [new ApiAccessKeyConfig { Name = "Dev key", DevelopmentKey = "plain-text" }]
        };
        var development = new ApiAccessService(Options.Create(config), new TestEnvironment("Development"));
        var staging = new ApiAccessService(Options.Create(config), new TestEnvironment("Staging"));

        Assert.True(development.Authorize("plain-text", new PathString("/api/companies")).IsAllowed);
        var stagingDecision = staging.Authorize("plain-text", new PathString("/api/companies"));
        Assert.False(stagingDecision.IsAllowed);
        Assert.Contains("Invalid API access key", stagingDecision.DenialReason);
    }

    [Fact]
    public void ApiAccess_DisabledBypassIsDevelopmentOnly()
    {
        var config = new ApiAccessConfig { Enabled = false };
        var development = new ApiAccessService(Options.Create(config), new TestEnvironment("Development"));
        var staging = new ApiAccessService(Options.Create(config), new TestEnvironment("Staging"));

        Assert.True(development.Authorize(null, new PathString("/api/companies")).IsAllowed);
        var stagingDecision = staging.Authorize(null, new PathString("/api/companies"));
        Assert.False(stagingDecision.IsAllowed);
        Assert.Contains("not configured", stagingDecision.DenialReason);
    }

    [Fact]
    public void ApiAccess_RejectsInvalidRoleConfiguration()
    {
        var service = new ApiAccessService(
            Options.Create(new ApiAccessConfig
            {
                Enabled = true,
                Keys = [new ApiAccessKeyConfig { Name = "Odd key", Role = "SuperUser", DevelopmentKey = "plain-text" }]
            }),
            new TestEnvironment("Development"));

        var failures = service.ValidateConfiguration();

        Assert.Contains(failures, f => f.Contains("invalid Role"));
    }

    [Fact]
    public async Task TenantAccessMiddleware_HidesCrossTenantCompany()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var tenantB = await SeedTenantAsync(db, name: "Tenant B", slug: "tenant-b");
        var companyB = await SeedTenantCompanyAsync(db, tenantB.Id, "Tenant B Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{companyB.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task TenantAccessMiddleware_AllowsOwnTenantCompany()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var companyA = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant A Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{companyA.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantAccessMiddleware_HidesSameTenantCompanyOutsideClientAssignment()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Path = $"/api/companies/{otherCompany.Id}";
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });
        var middleware = new TenantAccessMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task CompanyList_IntersectsTenantAndApiKeyAllowedCompanyIds()
    {
        await using var db = CreateDbContext();
        var tenantA = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var companyA = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant A Limited");
        var companyB = await SeedTenantCompanyAsync(db, tenantA.Id, "Tenant B Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenantA.Id,
            TenantName: tenantA.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        context.Items["ApiAllowedCompanyIds"] = new[] { companyA.Id };

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([companyA.Id], visibleCompanyIds);
        Assert.DoesNotContain(companyB.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyList_TrimsClientRoleBeforeApplyingCompanyAssignments()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var context = new DefaultHttpContext();
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: " Client ",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        var visibleCompanyIds = await CompanyListQuery
            .ForContext(context, db.Companies)
            .Select(c => c.Id)
            .ToListAsync();

        Assert.Equal([assignedCompany.Id], visibleCompanyIds);
        Assert.DoesNotContain(otherCompany.Id, visibleCompanyIds);
    }

    [Fact]
    public async Task CompanyEndpointAccess_UsesCompanyListPolicyForDirectHandlerGuards()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var clientContext = new DefaultHttpContext();
        clientContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });
        var ownerContext = new DefaultHttpContext();
        ownerContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 2,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "owner@tenant-a.test",
            DisplayName: "Tenant A Owner",
            Role: "Owner");
        ownerContext.Items["ApiAllowedCompanyIds"] = new[] { assignedCompany.Id };

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(clientContext, db, assignedCompany.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(clientContext, db, otherCompany.Id));
        Assert.True(await CompanyEndpointAccess.CanAccessCompanyAsync(ownerContext, db, assignedCompany.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyAsync(ownerContext, db, otherCompany.Id));
    }

    [Fact]
    public async Task CompanyEndpointAccess_UsesCompanyListAndPeriodOwnershipForDirectPeriodGuards()
    {
        await using var db = CreateDbContext();
        var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
        var assignedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Assigned Client Limited");
        var otherCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Other Client Limited");
        var assignedPeriod = new AccountingPeriod
        {
            CompanyId = assignedCompany.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        var otherPeriod = new AccountingPeriod
        {
            CompanyId = otherCompany.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = true
        };
        db.AccountingPeriods.AddRange(assignedPeriod, otherPeriod);
        await db.SaveChangesAsync();
        var clientContext = new DefaultHttpContext();
        clientContext.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 1,
            TenantId: tenant.Id,
            TenantName: tenant.Name,
            Email: "client@tenant-a.test",
            DisplayName: "Tenant A Client",
            Role: "Client",
            AllowedCompanyIds: new HashSet<int> { assignedCompany.Id });

        Assert.True(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, assignedCompany.Id, assignedPeriod.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, assignedCompany.Id, otherPeriod.Id));
        Assert.False(await CompanyEndpointAccess.CanAccessCompanyPeriodAsync(clientContext, db, otherCompany.Id, otherPeriod.Id));
    }

    [Fact]
    public async Task ReportingOutputEndpoint_HidesInaccessibleCompanyPeriodAtRuntime()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.AddScoped<FinancialStatementsService>();

        await using var app = builder.Build();
        int tenantId;
        int restrictedPeriodId;
        int allowedCompanyId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Client Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Client Limited");
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.Add(restrictedPeriod);
            await db.SaveChangesAsync();
            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedPeriodId = restrictedPeriod.Id;
        }

        app.Use(async (context, next) =>
        {
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: "client@tenant-a.test",
                DisplayName: "Tenant A Client",
                Role: "Client",
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId });
            await next();
        });
        app.MapStatementsEndpoints();
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(LoopbackBaseAddress(app)) };
            var response = await client.GetAsync($"/api/companies/{allowedCompanyId}/periods/{restrictedPeriodId}/statements/readiness");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }

    }

    [Fact]
    public async Task PeriodLockMiddleware_BlocksAccountingWritesToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task PeriodLockMiddleware_AllowsReadsToFinalisedPeriod()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/debtors";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("PUT", "filing/cro-status")]
    [InlineData("POST", "filing/cro-payment")]
    [InlineData("POST", "documents/cro-filing-pack")]
    [InlineData("POST", "documents/signature-page")]
    [InlineData("POST", "mark-filed")]
    [InlineData("POST", "deadlines/calculate")]
    public async Task PeriodLockMiddleware_AllowsExactPostLockWorkflowWritesToFinalisedPeriod(string method, string suffix)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/{suffix}";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("POST", "filing/mark-generated")]
    [InlineData("POST", "filing/validate-ixbrl")]
    [InlineData("POST", "filing/cro-payment/extra")]
    [InlineData("POST", "documents/cro-filing-pack/extra")]
    [InlineData("POST", "deadlines/calculate/extra")]
    [InlineData("PUT", "filing/mark-generated")]
    public async Task PeriodLockMiddleware_BlocksPreparationAndFalsePositiveWorkflowWritesToFinalisedPeriod(string method, string suffix)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var nextCalled = false;
        var context = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = method;
        context.Request.Path = $"/api/companies/{period.CompanyId}/periods/{period.Id}/{suffix}";
        var middleware = new PeriodLockMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, db);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task AccountingWriteGuard_BlocksCompanyAccountingWritesWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyAccountingWriteAsync(period.CompanyId);

        Assert.False(decision.CanWrite);
        Assert.Contains("locked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountingWriteGuard_BlocksMasterDataWritesWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var guard = new AccountingWriteGuard(db);

        var decision = await guard.CheckCompanyMasterDataWriteAsync(period.CompanyId);

        Assert.False(decision.CanWrite);
        Assert.Contains("locked", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CharityRoutes_EnforceRuntimeCompanyPeriodRoleApiAndLockGuards()
    {
        var databaseName = Guid.NewGuid().ToString();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AccountsDbContext>(options => options.UseInMemoryDatabase(databaseName));
        builder.Services.Configure<ApiAccessConfig>(config =>
        {
            config.Enabled = true;
            config.Keys =
            [
                new ApiAccessKeyConfig
                {
                    Name = "Reader",
                    Role = "Reader",
                    DevelopmentKey = "reader",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Writer",
                    Role = "Writer",
                    DevelopmentKey = "writer",
                    AllowedCompanyIds = []
                },
                new ApiAccessKeyConfig
                {
                    Name = "Admin",
                    Role = "Admin",
                    DevelopmentKey = "admin",
                    AllowedCompanyIds = []
                }
            ];
        });
        builder.Services.AddScoped<ApiAccessService>();
        builder.Services.AddScoped<AccountingWriteGuard>();
        builder.Services.AddScoped<AuditService>();
        builder.Services.AddScoped<CharityReportingService>();
        builder.Services.AddScoped<CharityPdfService>();
        builder.Services.AddScoped<FinancialStatementsService>(); // for charity SoFA reconciliation route
        builder.Services.AddScoped<FilingReadinessProfileService>();
        builder.Services.AddSingleton<FilingReleaseIdentityProvider>();
        builder.Services.AddScoped<FilingReleaseGate>();

        await using var app = builder.Build();
        int tenantId;
        int allowedCompanyId;
        int restrictedCompanyId;
        int lockedCompanyId;
        int allowedPeriodId;
        int restrictedPeriodId;
        int lockedPeriodId;
        int deleteFundId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var tenant = await SeedTenantAsync(db, name: "Tenant A", slug: "tenant-a");
            var allowedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Allowed Charity Limited");
            var lockedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Locked Charity Limited");
            var restrictedCompany = await SeedTenantCompanyAsync(db, tenant.Id, "Restricted Charity Limited");
            var allowedPeriod = new AccountingPeriod
            {
                CompanyId = allowedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = lockedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Reviewer"
            };
            var restrictedPeriod = new AccountingPeriod
            {
                CompanyId = restrictedCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true
            };
            db.AccountingPeriods.AddRange(allowedPeriod, lockedPeriod, restrictedPeriod);
            db.CharityInfos.AddRange(
                new CharityInfo
                {
                    CompanyId = allowedCompany.Id,
                    CharityNumber = "CHY-ALLOWED",
                    CharityType = "CLG",
                    GrossIncome = 100_000m
                },
                new CharityInfo
                {
                    CompanyId = restrictedCompany.Id,
                    CharityNumber = "CHY-HIDDEN",
                    CharityType = "CLG",
                    GrossIncome = 100_000m
                });
            await db.SaveChangesAsync();

            var generalFund = new FundBalance
            {
                PeriodId = allowedPeriod.Id,
                FundName = "General fund",
                FundType = "Unrestricted",
                OpeningBalance = 100m,
                ClosingBalance = 100m
            };
            var deleteFund = new FundBalance
            {
                PeriodId = allowedPeriod.Id,
                FundName = "Delete fund",
                FundType = "Restricted",
                OpeningBalance = 50m,
                ClosingBalance = 50m
            };
            var hiddenFund = new FundBalance
            {
                PeriodId = restrictedPeriod.Id,
                FundName = "Hidden fund",
                FundType = "Restricted",
                OpeningBalance = 900m,
                ClosingBalance = 900m
            };
            db.FundBalances.AddRange(generalFund, deleteFund, hiddenFund);
            await db.SaveChangesAsync();

            tenantId = tenant.Id;
            allowedCompanyId = allowedCompany.Id;
            restrictedCompanyId = restrictedCompany.Id;
            lockedCompanyId = lockedCompany.Id;
            allowedPeriodId = allowedPeriod.Id;
            restrictedPeriodId = restrictedPeriod.Id;
            lockedPeriodId = lockedPeriod.Id;
            deleteFundId = deleteFund.Id;
        }

        app.Use(async (context, next) =>
        {
            var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Accountant";
            context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                UserId: 1,
                TenantId: tenantId,
                TenantName: "Tenant A",
                Email: $"{role.ToLowerInvariant()}@tenant-a.test",
                DisplayName: $"Tenant A {role}",
                Role: role,
                AllowedCompanyIds: new HashSet<int> { allowedCompanyId, lockedCompanyId });
            await next();
        });
        app.MapCharityEndpoints();
        await app.StartAsync();

        try
        {
            var addresses = app.Services
                .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features
                .Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            var charityInfoBody = new
            {
                charityNumber = "CHY-WRITER",
                charityType = "CLG",
                grossIncome = 200_000m,
                charitableObjectives = "Community services",
                principalActivities = "Local programmes",
                governanceCodeCompliant = true,
                governanceCodeNote = "Board review complete",
                governanceEvidenceReference = "GOV-WRITER-001",
                governanceEvidenceArtifact = Convert.ToBase64String(Encoding.UTF8.GetBytes("governance evidence"))
            };
            var fundBody = new
            {
                fundName = "New restricted grant",
                fundType = "Restricted",
                openingBalance = 10m,
                incomingResources = 25m,
                resourcesExpended = 5m,
                transfers = 0m,
                gainsLosses = 0m,
                notes = "Grant letter"
            };

            using var hiddenInfoRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/charity/info",
                role: "Client");
            var hiddenInfo = await client.SendAsync(hiddenInfoRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenInfo.StatusCode);

            using var hiddenFundsRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/charity/funds",
                role: "Client");
            var hiddenFunds = await client.SendAsync(hiddenFundsRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenFunds.StatusCode);

            using var hiddenSofaRequest = Request(
                HttpMethod.Get,
                $"/api/companies/{restrictedCompanyId}/periods/{restrictedPeriodId}/charity/sofa",
                role: "Client");
            var hiddenSofa = await client.SendAsync(hiddenSofaRequest);
            Assert.Equal(HttpStatusCode.NotFound, hiddenSofa.StatusCode);

            using var reviewerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Reviewer",
                apiKey: "writer",
                body: charityInfoBody);
            var reviewerInfoDenied = await client.SendAsync(reviewerInfo);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerInfoDenied.StatusCode);

            using var readerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Accountant",
                apiKey: "reader",
                body: charityInfoBody);
            var readerInfoDenied = await client.SendAsync(readerInfo);
            Assert.Equal(HttpStatusCode.Unauthorized, readerInfoDenied.StatusCode);

            using var writerInfo = Request(
                HttpMethod.Put,
                $"/api/companies/{allowedCompanyId}/charity/info",
                role: "Accountant",
                apiKey: "writer",
                body: charityInfoBody);
            var writerInfoAllowed = await client.SendAsync(writerInfo);
            Assert.Equal(HttpStatusCode.OK, writerInfoAllowed.StatusCode);

            using var reviewerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Reviewer",
                apiKey: "writer",
                body: fundBody);
            var reviewerCreateDenied = await client.SendAsync(reviewerCreate);
            Assert.Equal(HttpStatusCode.Forbidden, reviewerCreateDenied.StatusCode);

            using var readerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "reader",
                body: fundBody);
            var readerCreateDenied = await client.SendAsync(readerCreate);
            Assert.Equal(HttpStatusCode.Unauthorized, readerCreateDenied.StatusCode);

            using var writerCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "writer",
                body: fundBody);
            var writerCreateAllowed = await client.SendAsync(writerCreate);
            Assert.Equal(HttpStatusCode.Created, writerCreateAllowed.StatusCode);

            using var lockedCreate = Request(
                HttpMethod.Post,
                $"/api/companies/{lockedCompanyId}/periods/{lockedPeriodId}/charity/funds",
                role: "Accountant",
                apiKey: "writer",
                body: fundBody);
            var lockedCreateDenied = await client.SendAsync(lockedCreate);
            Assert.Equal(HttpStatusCode.Conflict, lockedCreateDenied.StatusCode);

            using var writerDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds/{deleteFundId}",
                role: "Accountant",
                apiKey: "writer");
            var writerDeleteDenied = await client.SendAsync(writerDelete);
            Assert.Equal(HttpStatusCode.Unauthorized, writerDeleteDenied.StatusCode);

            using var adminDelete = Request(
                HttpMethod.Delete,
                $"/api/companies/{allowedCompanyId}/periods/{allowedPeriodId}/charity/funds/{deleteFundId}",
                role: "Accountant",
                apiKey: "admin");
            var adminDeleteAllowed = await client.SendAsync(adminDelete);
            Assert.Equal(HttpStatusCode.NoContent, adminDeleteAllowed.StatusCode);

            await using var verifyScope = app.Services.CreateAsyncScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            var info = await verifyDb.CharityInfos.SingleAsync(c => c.CompanyId == allowedCompanyId);
            Assert.Equal("CHY-WRITER", info.CharityNumber);
            Assert.Equal(2, await verifyDb.FundBalances.CountAsync(f => f.PeriodId == allowedPeriodId));
            Assert.False(await verifyDb.FundBalances.AnyAsync(f => f.Id == deleteFundId));
            Assert.Equal(2, await verifyDb.AuditLogs.CountAsync(a => a.EntityType == "FundBalance"));
            var charityAudit = await verifyDb.AuditLogs.SingleAsync(a => a.EntityType == "CharityInfo");
            Assert.Equal(AuditEventCodes.CharityInfoUpdated, charityAudit.Action);
            Assert.Contains("\"CharityNumber\":\"CHY-ALLOWED\"", charityAudit.OldValueJson);
            Assert.Contains("\"CharityNumber\":\"CHY-WRITER\"", charityAudit.NewValueJson);
        }
        finally
        {
            await app.StopAsync();
        }

        static HttpRequestMessage Request(HttpMethod method, string path, string role, string? apiKey = null, object? body = null)
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Test-Role", role);
            if (apiKey is not null)
                request.Headers.Add("X-Accounts-Api-Key", apiKey);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            return request;
        }
    }

    [Fact]
    public async Task CharityInfoEndpoint_BlocksCompanyAccountingWriteWhenAnyPeriodLocked()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var service = new CharityReportingService(db);
        var guard = new AccountingWriteGuard(db);
        var context = AuthenticatedRequest("Accountant", HttpMethods.Put, $"/api/companies/{period.CompanyId}/charity/info");
        var input = new CharityInfo
        {
            CompanyId = 999,
            CharityNumber = "CHY-1",
            CharityType = "CLG",
            GrossIncome = 100_000m
        };

        var result = await CharityEndpoints.SaveCharityInfoEndpointAsync(
            period.CompanyId,
            input,
            service,
            guard,
            new AuditService(db),
            db,
            context,
            DisabledApiAccess());

        Assert.IsAssignableFrom<IResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, ResultStatusCode(result));
        Assert.Null(await db.CharityInfos.FirstOrDefaultAsync(c => c.CompanyId == period.CompanyId));
    }

}
