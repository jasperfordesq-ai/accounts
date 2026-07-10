using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace Accounts.Tests;

public sealed class EntityWriteInputSecurityTests
{
    private const string PostgresConnectionEnvVar = "ACCOUNTS_POSTGRES_TEST_CONNECTION";

    [Fact]
    public async Task BankAndCategoryWrites_IgnoreNestedGraphsAndRouteOwnedIdentity()
    {
        await using var host = await SecurityTestHost.StartAsync();

        using var bankRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/bank-accounts", $$"""
            {
              "id": 424242,
              "companyId": {{host.OtherCompanyId}},
              "name": "Safe current account",
              "currency": "EUR",
              "openingBalance": 100,
              "openingBalanceDate": "2025-01-01",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version",
              "transactions": [
                {
                  "id": 8181,
                  "bankAccountId": 9191,
                  "periodId": {{host.OtherPeriodId}},
                  "date": "2025-01-02",
                  "description": "Injected transaction",
                  "amount": 999999
                }
              ],
              "importBatches": [
                {
                  "id": 7171,
                  "bankAccountId": 9191,
                  "filename": "injected.csv",
                  "transactions": []
                }
              ]
            }
            """);
        var bankResponse = await host.Client.SendAsync(bankRequest);
        Assert.Equal(HttpStatusCode.Created, bankResponse.StatusCode);

        using var categoryRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/categories", $$"""
            {
              "id": 525252,
              "companyId": null,
              "code": " 7777 ",
              "name": " Injected-safe root ",
              "type": "Expense",
              "isSystem": true,
              "approvedBy": "attacker",
              "rowVersion": "attacker-version",
              "children": [
                { "id": 1, "companyId": null, "code": "EVIL", "name": "Global child", "type": "Asset", "isSystem": true }
              ],
              "transactions": [
                { "bankAccountId": 999, "periodId": {{host.OtherPeriodId}}, "date": "2025-01-02", "description": "Injected", "amount": 1 }
              ],
              "rules": [
                { "companyId": {{host.OtherCompanyId}}, "pattern": "*", "categoryId": 1, "priority": -100 }
              ]
            }
            """);
        var categoryResponse = await host.Client.SendAsync(categoryRequest);
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);

        await host.InScopeAsync(async db =>
        {
            var bank = await db.BankAccounts.SingleAsync(b => b.Name == "Safe current account");
            Assert.NotEqual(424242, bank.Id);
            Assert.Equal(host.CompanyId, bank.CompanyId);
            Assert.Empty(await db.ImportedTransactions.ToListAsync());
            Assert.Empty(await db.ImportBatches.ToListAsync());

            var category = await db.AccountCategories.SingleAsync(c => c.Code == "7777");
            Assert.NotEqual(525252, category.Id);
            Assert.Equal(host.CompanyId, category.CompanyId);
            Assert.False(category.IsSystem);
            Assert.Equal("Injected-safe root", category.Name);
            Assert.DoesNotContain(await db.AccountCategories.ToListAsync(), c => c.Code == "EVIL");
            Assert.Empty(await db.TransactionRules.ToListAsync());
        });
    }

    [PostgresFact]
    public async Task NestedGraphOverposting_Postgres_PersistsOnlyGeneratedRootsAndLeavesProtectedRowsUnchanged()
    {
        await using var host = await SecurityTestHost.StartPostgresAsync(
            Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)!);

        int foreignCompanyId = 0;
        int lockedPeriodId = 0;
        ProtectedCounts before = null!;
        await host.InScopeAsync(async db =>
        {
            var foreignTenant = new Tenant
            {
                Name = "Foreign Security Tenant",
                Slug = $"foreign-security-{Guid.NewGuid():N}"
            };
            var foreignCompany = new Company
            {
                Tenant = foreignTenant,
                LegalName = "Foreign Tenant Limited",
                CompanyType = CompanyType.Private,
                IncorporationDate = new DateOnly(2025, 1, 1)
            };
            db.Companies.Add(foreignCompany);
            await db.SaveChangesAsync();
            var lockedPeriod = new AccountingPeriod
            {
                CompanyId = foreignCompany.Id,
                PeriodStart = new DateOnly(2025, 1, 1),
                PeriodEnd = new DateOnly(2025, 12, 31),
                IsFirstYear = true,
                Status = PeriodStatus.Finalised,
                LockedAt = DateTime.UtcNow,
                LockedBy = "Foreign reviewer"
            };
            var foreignBank = new BankAccount
            {
                CompanyId = foreignCompany.Id,
                Name = "Foreign protected bank",
                OpeningBalance = 0m
            };
            var foreignCategory = new AccountCategory
            {
                CompanyId = foreignCompany.Id,
                Code = "F001",
                Name = "Foreign protected category",
                Type = AccountCategoryType.Expense
            };
            db.AddRange(lockedPeriod, foreignBank, foreignCategory);
            await db.SaveChangesAsync();
            var foreignBatch = new ImportBatch
            {
                BankAccountId = foreignBank.Id,
                Filename = "foreign-protected.csv"
            };
            db.ImportBatches.Add(foreignBatch);
            await db.SaveChangesAsync();
            db.ImportedTransactions.Add(new ImportedTransaction
            {
                BankAccountId = foreignBank.Id,
                ImportBatchId = foreignBatch.Id,
                PeriodId = lockedPeriod.Id,
                CategoryId = foreignCategory.Id,
                Date = lockedPeriod.PeriodStart,
                Description = "Foreign protected transaction",
                Amount = 10m
            });
            await db.SaveChangesAsync();

            foreignCompanyId = foreignCompany.Id;
            lockedPeriodId = lockedPeriod.Id;
            before = await ProtectedCounts.ReadAsync(db, foreignCompanyId, lockedPeriodId);
        });

        using var bankRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/bank-accounts", $$"""
            {
              "id": 990001,
              "companyId": {{foreignCompanyId}},
              "name": "Postgres safe root bank",
              "currency": "EUR",
              "openingBalance": 0,
              "transactions": [
                {
                  "id": 990002,
                  "bankAccountId": 990001,
                  "periodId": {{lockedPeriodId}},
                  "date": "2025-06-01",
                  "description": "Injected locked-period transaction",
                  "amount": 999
                }
              ],
              "importBatches": [
                {
                  "id": 990003,
                  "bankAccountId": 990001,
                  "filename": "injected.csv",
                  "transactions": []
                }
              ]
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(bankRequest)).StatusCode);

        using var categoryRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/categories", $$"""
            {
              "id": 990004,
              "companyId": {{foreignCompanyId}},
              "code": " PG77 ",
              "name": " Postgres safe root category ",
              "type": "Expense",
              "isSystem": true,
              "children": [
                { "id": 990005, "companyId": null, "code": "EVIL", "name": "Injected global", "type": "Asset", "isSystem": true }
              ],
              "transactions": [
                { "id": 990006, "periodId": {{lockedPeriodId}}, "date": "2025-06-01", "description": "Injected", "amount": 1 }
              ],
              "rules": [
                { "id": 990007, "companyId": {{foreignCompanyId}}, "pattern": "*", "categoryId": 990004, "priority": -1 }
              ]
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(categoryRequest)).StatusCode);

        await host.InScopeAsync(async db =>
        {
            db.ChangeTracker.Clear();
            var bank = await db.BankAccounts.SingleAsync(item => item.Name == "Postgres safe root bank");
            var category = await db.AccountCategories.SingleAsync(item => item.Code == "PG77");
            Assert.NotEqual(990001, bank.Id);
            Assert.Equal(host.CompanyId, bank.CompanyId);
            Assert.NotEqual(990004, category.Id);
            Assert.Equal(host.CompanyId, category.CompanyId);
            Assert.False(category.IsSystem);

            Assert.False(await db.ImportedTransactions.IgnoreQueryFilters().AnyAsync(item => item.Id == 990002 || item.Id == 990006));
            Assert.False(await db.ImportBatches.IgnoreQueryFilters().AnyAsync(item => item.Id == 990003));
            Assert.False(await db.AccountCategories.IgnoreQueryFilters().AnyAsync(item => item.Id == 990005 || item.Code == "EVIL"));
            Assert.False(await db.TransactionRules.IgnoreQueryFilters().AnyAsync(item => item.Id == 990007));
            Assert.Equal(before, await ProtectedCounts.ReadAsync(db, foreignCompanyId, lockedPeriodId));
        });
    }

    [Fact]
    public async Task UpdateRouteId_RemainsAuthoritativeWhenBodyOverpostsAnotherEntityId()
    {
        await using var host = await SecurityTestHost.StartAsync();
        var firstResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/bank-accounts",
            new { name = "First account", currency = "EUR", openingBalance = 0m });
        var secondResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/bank-accounts",
            new { name = "Second account", currency = "EUR", openingBalance = 0m });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var first = Assert.IsType<BankAccount>(await firstResponse.Content.ReadFromJsonAsync<BankAccount>());
        var second = Assert.IsType<BankAccount>(await secondResponse.Content.ReadFromJsonAsync<BankAccount>());

        using var updateRequest = JsonRequest(HttpMethod.Put, $"/api/companies/{host.CompanyId}/bank-accounts/{first.Id}", $$"""
            {
              "id": {{second.Id}},
              "companyId": {{host.OtherCompanyId}},
              "name": "First account updated",
              "currency": "EUR",
              "openingBalance": 0,
              "approvedBy": "attacker",
              "rowVersion": "attacker-version",
              "transactions": [
                { "periodId": {{host.OtherPeriodId}}, "date": "2025-01-02", "description": "Injected", "amount": 1 }
              ]
            }
            """);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(updateRequest)).StatusCode);

        await host.InScopeAsync(async db =>
        {
            Assert.Equal("First account updated", (await db.BankAccounts.SingleAsync(b => b.Id == first.Id)).Name);
            Assert.Equal("Second account", (await db.BankAccounts.SingleAsync(b => b.Id == second.Id)).Name);
            Assert.All(await db.BankAccounts.ToListAsync(), bank => Assert.Equal(host.CompanyId, bank.CompanyId));
            Assert.Empty(await db.ImportedTransactions.ToListAsync());
        });
    }

    [Fact]
    public async Task YearEndAndCharityWrites_IgnoreForeignIdentityNavigationAndCalculatedFields()
    {
        await using var host = await SecurityTestHost.StartAsync();

        using var assetRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/fixed-assets", $$"""
            {
              "id": 10101,
              "companyId": {{host.OtherCompanyId}},
              "name": "Secure laptop",
              "category": "Computer Equipment",
              "cost": 2000,
              "acquisitionDate": "2025-01-02",
              "usefulLifeYears": 3,
              "depreciationMethod": "StraightLine",
              "approvedBy": "attacker",
              "createdAt": "2000-01-01T00:00:00Z",
              "rowVersion": "attacker-version",
              "depreciationEntries": [
                { "id": 20202, "periodId": {{host.OtherPeriodId}}, "openingNbv": 2000, "charge": 1999, "closingNbv": 1 }
              ]
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(assetRequest)).StatusCode);

        using var debtorRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/debtors", $$"""
            {
              "id": 30303,
              "periodId": {{host.OtherPeriodId}},
              "name": "Route-owned debtor",
              "amount": 125,
              "type": "Trade",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(debtorRequest)).StatusCode);

        using var shareRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/share-capital", $$"""
            {
              "id": 40404,
              "companyId": {{host.OtherCompanyId}},
              "shareClass": "Ordinary",
              "nominalValue": 2,
              "numberIssued": 10,
              "totalValue": 999999,
              "isFullyPaid": true,
              "issueDate": "2025-01-01"
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(shareRequest)).StatusCode);

        using var fundRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/charity/funds", $$"""
            {
              "id": 50505,
              "periodId": {{host.OtherPeriodId}},
              "fundName": "Restricted grant",
              "fundType": "Restricted",
              "openingBalance": 100,
              "incomingResources": 50,
              "resourcesExpended": 20,
              "transfers": 5,
              "gainsLosses": 2,
              "closingBalance": 999999,
              "createdAt": "2000-01-01T00:00:00Z"
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(fundRequest)).StatusCode);

        using var charityRequest = JsonRequest(HttpMethod.Put, $"/api/companies/{host.CompanyId}/charity/info", $$"""
            {
              "id": 60606,
              "companyId": {{host.OtherCompanyId}},
              "charityNumber": "CHY-SECURE",
              "charityType": "CLG",
              "grossIncome": 100000,
              "sorpTier": 3,
              "governanceCodeCompliant": true,
              "governanceCodeNote": "Board review complete",
              "governanceEvidenceReference": "GOV-SECURE-001",
              "governanceEvidenceArtifact": "Z292ZXJuYW5jZS1ldmlkZW5jZQ==",
              "createdAt": "2000-01-01T00:00:00Z",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(charityRequest)).StatusCode);

        await host.InScopeAsync(async db =>
        {
            var asset = await db.FixedAssets.SingleAsync(a => a.Name == "Secure laptop");
            Assert.NotEqual(10101, asset.Id);
            Assert.Equal(host.CompanyId, asset.CompanyId);
            Assert.Empty(await db.DepreciationEntries.ToListAsync());

            var debtor = await db.Debtors.SingleAsync(d => d.Name == "Route-owned debtor");
            Assert.NotEqual(30303, debtor.Id);
            Assert.Equal(host.PeriodId, debtor.PeriodId);

            var shares = await db.ShareCapitals.SingleAsync();
            Assert.NotEqual(40404, shares.Id);
            Assert.Equal(host.CompanyId, shares.CompanyId);
            Assert.Equal(20m, shares.TotalValue);

            var fund = await db.FundBalances.SingleAsync();
            Assert.NotEqual(50505, fund.Id);
            Assert.Equal(host.PeriodId, fund.PeriodId);
            Assert.Equal(137m, fund.ClosingBalance);

            var charity = await db.CharityInfos.SingleAsync();
            Assert.NotEqual(60606, charity.Id);
            Assert.Equal(host.CompanyId, charity.CompanyId);
            Assert.Equal(1, charity.SorpTier);
            Assert.True(charity.CreatedAt > new DateTime(2020, 1, 1));
        });
    }

    [Fact]
    public async Task ApprovalAndAuditIdentityOverposts_AreReplacedByServerOwnedValues()
    {
        await using var host = await SecurityTestHost.StartAsync();
        var requestStartedAt = DateTime.UtcNow.AddSeconds(-1);

        using var noteRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/notes", $$"""
            {
              "id": 70707,
              "periodId": {{host.OtherPeriodId}},
              "noteNumber": 999,
              "title": "Server-owned note metadata",
              "content": "Content",
              "isRequired": true,
              "isIncluded": true,
              "approvedBy": "attacker",
              "approvedAt": "2000-01-01T00:00:00Z",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(noteRequest)).StatusCode);

        using var snapshotRequest = JsonRequest(HttpMethod.Post, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/loan-balance-snapshots", $$"""
            {
              "id": 80808,
              "periodId": {{host.OtherPeriodId}},
              "loanId": {{host.LoanId}},
              "openingBalance": 0,
              "drawdowns": 100,
              "repayments": 20,
              "closingBalance": 80,
              "dueWithinYear": 30,
              "dueAfterYear": 50,
              "enteredBy": "attacker",
              "enteredAt": "2000-01-01T00:00:00Z",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.Created, (await host.Client.SendAsync(snapshotRequest)).StatusCode);

        using var openingRequest = JsonRequest(HttpMethod.Put, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/opening-balances/{host.AssetCategoryId}", """
            {
              "debit": 500,
              "credit": 0,
              "sourceNote": "Take-on",
              "reviewed": true,
              "enteredBy": "attacker",
              "enteredAt": "2000-01-01T00:00:00Z",
              "reviewedBy": "attacker",
              "reviewedAt": "2000-01-01T00:00:00Z",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(openingRequest)).StatusCode);

        using var reviewRequest = JsonRequest(HttpMethod.Put, $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/year-end-reviews/debtors", """
            {
              "confirmed": true,
              "confirmedBy": "attacker",
              "confirmedAt": "2000-01-01T00:00:00Z",
              "note": "Reviewed",
              "approvedBy": "attacker",
              "rowVersion": "attacker-version"
            }
            """);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(reviewRequest)).StatusCode);

        await host.InScopeAsync(async db =>
        {
            var note = await db.NotesDisclosures.SingleAsync();
            Assert.NotEqual(70707, note.Id);
            Assert.Equal(host.PeriodId, note.PeriodId);
            Assert.Equal(1, note.NoteNumber);
            Assert.False(note.IsRequired);

            var snapshot = await db.LoanBalanceSnapshots.SingleAsync();
            Assert.NotEqual(80808, snapshot.Id);
            Assert.Equal(host.PeriodId, snapshot.PeriodId);
            Assert.Equal(SecurityTestHost.ActorDisplayName, snapshot.EnteredBy);
            Assert.True(snapshot.EnteredAt >= requestStartedAt);

            var opening = await db.OpeningBalances.SingleAsync();
            Assert.Equal(SecurityTestHost.ActorDisplayName, opening.EnteredBy);
            Assert.Equal(SecurityTestHost.ActorDisplayName, opening.ReviewedBy);
            Assert.True(opening.EnteredAt >= requestStartedAt);
            Assert.True(opening.ReviewedAt >= requestStartedAt);

            var review = await db.YearEndReviewConfirmations.SingleAsync();
            Assert.Equal(SecurityTestHost.ActorDisplayName, review.ConfirmedBy);
            Assert.True(review.ConfirmedAt >= requestStartedAt);
        });
    }

    [Fact]
    public async Task ForeignRelationshipIds_AreRejectedWithoutCreatingRows()
    {
        await using var host = await SecurityTestHost.StartAsync();

        var categoryResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/categories",
            new
            {
                code = "8888",
                name = "Cross-company child",
                type = AccountCategoryType.Expense,
                parentId = host.ForeignCategoryId
            });
        Assert.Equal(HttpStatusCode.BadRequest, categoryResponse.StatusCode);

        var snapshotResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/periods/{host.PeriodId}/loan-balance-snapshots",
            new
            {
                loanId = host.ForeignLoanId,
                openingBalance = 0m,
                drawdowns = 100m,
                repayments = 20m,
                closingBalance = 80m,
                dueWithinYear = 30m,
                dueAfterYear = 50m
            });
        Assert.Equal(HttpStatusCode.BadRequest, snapshotResponse.StatusCode);

        var ruleResponse = await host.Client.PostAsJsonAsync(
            $"/api/companies/{host.CompanyId}/transaction-rules",
            new { pattern = "foreign", categoryId = host.ForeignCategoryId, priority = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, ruleResponse.StatusCode);

        await host.InScopeAsync(async db =>
        {
            Assert.DoesNotContain(await db.AccountCategories.ToListAsync(), c => c.Code == "8888");
            Assert.Empty(await db.LoanBalanceSnapshots.ToListAsync());
            Assert.Empty(await db.TransactionRules.ToListAsync());
        });
    }

    [Fact]
    public void MutatingEndpoints_DoNotBindPersistenceEntityTypes()
    {
        var entityTypes = typeof(Company).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.Namespace == typeof(Company).Namespace)
            .ToHashSet();
        var endpointTypes = typeof(BankingEndpoints).Assembly
            .GetTypes()
            .Where(type => type.IsClass && type.Namespace == typeof(BankingEndpoints).Namespace);

        var methodOffenders = endpointTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.Name.EndsWith("EndpointAsync", StringComparison.Ordinal)
                || method.DeclaringType == typeof(PeriodStatusEndpoint) && method.Name == nameof(PeriodStatusEndpoint.UpdateAsync)
                || method.DeclaringType == typeof(CompanyDeletionEndpoint) && method.Name == nameof(CompanyDeletionEndpoint.DeleteAsync))
            .SelectMany(method => method.GetParameters()
                .Where(parameter => entityTypes.Contains(parameter.ParameterType))
                .Select(parameter => $"{method.DeclaringType!.Name}.{method.Name}({parameter.ParameterType.Name} {parameter.Name})"))
            .ToArray();
        Assert.Empty(methodOffenders);

        var graphPropertyOffenders = typeof(BankingEndpoints).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && type.Namespace == typeof(BankingEndpoints).Namespace
                && type.Name.EndsWith("Input", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => ContainsEntityType(property.PropertyType, entityTypes))
                .Select(property => $"{type.Name}.{property.Name}: {property.PropertyType.Name}"))
            .ToArray();
        Assert.Empty(graphPropertyOffenders);

        var endpointDirectory = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Endpoints");
        var entityAlternation = string.Join("|", entityTypes.Select(type => Regex.Escape(type.Name)).OrderByDescending(name => name.Length));
        var inlineBinding = new Regex(
            $@"Map(?:Post|Put|Patch)\s*\([^\r\n]*(?:{entityAlternation})\s+\w+",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        var sourceOffenders = Directory.GetFiles(endpointDirectory, "*.cs")
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, lineNumber = index + 1 }))
            .Where(item => inlineBinding.IsMatch(item.line))
            .Select(item => $"{Path.GetFileName(item.path)}:{item.lineNumber}: {item.line.Trim()}")
            .ToArray();
        Assert.Empty(sourceOffenders);

        static bool ContainsEntityType(Type type, IReadOnlySet<Type> entityTypes)
        {
            if (entityTypes.Contains(type))
                return true;
            if (type.IsArray)
                return ContainsEntityType(type.GetElementType()!, entityTypes);
            return type.IsGenericType && type.GetGenericArguments().Any(argument => ContainsEntityType(argument, entityTypes));
        }
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string json) => new(method, path)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var start in new[]
        {
            Path.GetDirectoryName(sourceFilePath) ?? "",
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "backend", "Accounts.Api")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record ProtectedCounts(
        int Companies,
        int LockedPeriods,
        int Banks,
        int ImportBatches,
        int Transactions,
        int Categories,
        int Rules)
    {
        public static async Task<ProtectedCounts> ReadAsync(
            AccountsDbContext db,
            int companyId,
            int lockedPeriodId) => new(
            await db.Companies.IgnoreQueryFilters().CountAsync(company => company.Id == companyId),
            await db.AccountingPeriods.IgnoreQueryFilters().CountAsync(period =>
                period.Id == lockedPeriodId
                && period.CompanyId == companyId
                && period.Status == PeriodStatus.Finalised
                && period.LockedAt != null),
            await db.BankAccounts.IgnoreQueryFilters().CountAsync(bank => bank.CompanyId == companyId),
            await db.ImportBatches.IgnoreQueryFilters().CountAsync(batch => batch.BankAccount.CompanyId == companyId),
            await db.ImportedTransactions.IgnoreQueryFilters().CountAsync(transaction =>
                transaction.BankAccount.CompanyId == companyId || transaction.PeriodId == lockedPeriodId),
            await db.AccountCategories.IgnoreQueryFilters().CountAsync(category => category.CompanyId == companyId),
            await db.TransactionRules.IgnoreQueryFilters().CountAsync(rule => rule.CompanyId == companyId));
    }

    private sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresConnectionEnvVar)))
                Skip = $"{PostgresConnectionEnvVar} is not set.";
        }
    }

    private sealed class SecurityTestHost : IAsyncDisposable
    {
        public const string ActorDisplayName = "Security Test Owner";

        private readonly WebApplication _app;
        private readonly Func<Task>? _cleanup;

        private SecurityTestHost(
            WebApplication app,
            HttpClient client,
            int companyId,
            int otherCompanyId,
            int periodId,
            int otherPeriodId,
            int assetCategoryId,
            int foreignCategoryId,
            int loanId,
            int foreignLoanId,
            Func<Task>? cleanup)
        {
            _app = app;
            _cleanup = cleanup;
            Client = client;
            CompanyId = companyId;
            OtherCompanyId = otherCompanyId;
            PeriodId = periodId;
            OtherPeriodId = otherPeriodId;
            AssetCategoryId = assetCategoryId;
            ForeignCategoryId = foreignCategoryId;
            LoanId = loanId;
            ForeignLoanId = foreignLoanId;
        }

        public HttpClient Client { get; }
        public int CompanyId { get; }
        public int OtherCompanyId { get; }
        public int PeriodId { get; }
        public int OtherPeriodId { get; }
        public int AssetCategoryId { get; }
        public int ForeignCategoryId { get; }
        public int LoanId { get; }
        public int ForeignLoanId { get; }

        public static Task<SecurityTestHost> StartAsync() => StartCoreAsync(null, null);

        public static async Task<SecurityTestHost> StartPostgresAsync(string baseConnectionString)
        {
            var schemaName = $"entity_write_{Guid.NewGuid():N}";
            await using (var admin = new NpgsqlConnection(baseConnectionString))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
                await create.ExecuteNonQueryAsync();
            }

            var scopedConnection = new NpgsqlConnectionStringBuilder(baseConnectionString)
            {
                SearchPath = schemaName
            }.ConnectionString;
            async Task CleanupAsync()
            {
                NpgsqlConnection.ClearAllPools();
                await using var admin = new NpgsqlConnection(baseConnectionString);
                await admin.OpenAsync();
                await using var drop = admin.CreateCommand();
                drop.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
                await drop.ExecuteNonQueryAsync();
            }

            try
            {
                return await StartCoreAsync(scopedConnection, CleanupAsync);
            }
            catch
            {
                await CleanupAsync();
                throw;
            }
        }

        private static async Task<SecurityTestHost> StartCoreAsync(
            string? postgresConnectionString,
            Func<Task>? cleanup)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            var databaseName = $"entity-write-security-{Guid.NewGuid():N}";
            builder.Services.AddDbContext<AccountsDbContext>(options =>
            {
                if (postgresConnectionString is null)
                    options.UseInMemoryDatabase(databaseName);
                else
                    options.UseNpgsql(postgresConnectionString);
            });
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<ApiAccessConfig>(config => config.Enabled = false);
            builder.Services.Configure<ImportLimitConfig>(_ => { });
            builder.Services.AddSingleton<ApiAccessService>();
            builder.Services.AddScoped<AccountingWriteGuard>();
            builder.Services.AddScoped<AuditService>();
            builder.Services.AddScoped<CategoryService>();
            builder.Services.AddScoped<ImportService>();
            builder.Services.AddScoped<CharityReportingService>();

            var app = builder.Build();
            int tenantId;
            int companyId;
            int otherCompanyId;
            int periodId;
            int otherPeriodId;
            int assetCategoryId;
            int foreignCategoryId;
            int loanId;
            int foreignLoanId;

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
                if (postgresConnectionString is not null)
                    await db.Database.MigrateAsync();
                var tenant = new Tenant { Name = "Security Tenant", Slug = $"security-{Guid.NewGuid():N}" };
                var company = new Company
                {
                    Tenant = tenant,
                    LegalName = "Security Route Limited",
                    CompanyType = CompanyType.Private,
                    IncorporationDate = new DateOnly(2025, 1, 1)
                };
                var otherCompany = new Company
                {
                    Tenant = tenant,
                    LegalName = "Other Relationship Limited",
                    CompanyType = CompanyType.Private,
                    IncorporationDate = new DateOnly(2025, 1, 1)
                };
                db.Companies.AddRange(company, otherCompany);
                await db.SaveChangesAsync();

                var period = new AccountingPeriod
                {
                    CompanyId = company.Id,
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
                db.AccountingPeriods.AddRange(period, otherPeriod);
                await db.SaveChangesAsync();

                var assetCategory = new AccountCategory
                {
                    CompanyId = company.Id,
                    Code = "1000",
                    Name = "Cash",
                    Type = AccountCategoryType.Asset
                };
                var foreignCategory = new AccountCategory
                {
                    CompanyId = otherCompany.Id,
                    Code = "9000",
                    Name = "Foreign parent",
                    Type = AccountCategoryType.Expense
                };
                var loan = new Loan
                {
                    CompanyId = company.Id,
                    Lender = "Route bank",
                    OriginalAmount = 100m,
                    Balance = 80m,
                    DrawdownDate = period.PeriodStart,
                    BalanceAsOfDate = period.PeriodEnd
                };
                var foreignLoan = new Loan
                {
                    CompanyId = otherCompany.Id,
                    Lender = "Foreign bank",
                    OriginalAmount = 100m,
                    Balance = 80m,
                    DrawdownDate = otherPeriod.PeriodStart,
                    BalanceAsOfDate = otherPeriod.PeriodEnd
                };
                db.AccountCategories.AddRange(assetCategory, foreignCategory);
                db.Loans.AddRange(loan, foreignLoan);
                await db.SaveChangesAsync();

                tenantId = tenant.Id;
                companyId = company.Id;
                otherCompanyId = otherCompany.Id;
                periodId = period.Id;
                otherPeriodId = otherPeriod.Id;
                assetCategoryId = assetCategory.Id;
                foreignCategoryId = foreignCategory.Id;
                loanId = loan.Id;
                foreignLoanId = foreignLoan.Id;
            }

            app.Use(async (context, next) =>
            {
                context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
                    UserId: 1,
                    TenantId: tenantId,
                    TenantName: "Security Tenant",
                    Email: "security.owner@example.ie",
                    DisplayName: ActorDisplayName,
                    Role: "Owner",
                    AllowedCompanyIds: new HashSet<int> { companyId, otherCompanyId });
                await next();
            });

            app.MapBankingEndpoints();
            var createDebtor = YearEndEndpoints.CreateDebtorEndpointAsync;
            var createFixedAsset = YearEndEndpoints.CreateFixedAssetEndpointAsync;
            var createShareCapital = YearEndEndpoints.CreateShareCapitalEndpointAsync;
            var createLoanSnapshot = YearEndEndpoints.CreateLoanBalanceSnapshotEndpointAsync;
            var upsertOpeningBalance = YearEndEndpoints.UpsertOpeningBalanceEndpointAsync;
            var updateYearEndReview = YearEndEndpoints.UpdateYearEndReviewEndpointAsync;
            var createNote = YearEndEndpoints.CreateNoteEndpointAsync;
            var saveCharityInfo = CharityEndpoints.SaveCharityInfoEndpointAsync;
            var createFundBalance = CharityEndpoints.CreateFundBalanceEndpointAsync;
            app.MapPost("/api/companies/{companyId:int}/periods/{periodId:int}/debtors", createDebtor);
            app.MapPost("/api/companies/{companyId:int}/fixed-assets", createFixedAsset);
            app.MapPost("/api/companies/{companyId:int}/share-capital", createShareCapital);
            app.MapPost("/api/companies/{companyId:int}/periods/{periodId:int}/loan-balance-snapshots", createLoanSnapshot);
            app.MapPut("/api/companies/{companyId:int}/periods/{periodId:int}/opening-balances/{categoryId:int}", upsertOpeningBalance);
            app.MapPut("/api/companies/{companyId:int}/periods/{periodId:int}/year-end-reviews/{sectionKey}", updateYearEndReview);
            app.MapPost("/api/companies/{companyId:int}/periods/{periodId:int}/notes", createNote);
            app.MapPut("/api/companies/{companyId:int}/charity/info", saveCharityInfo);
            app.MapPost("/api/companies/{companyId:int}/periods/{periodId:int}/charity/funds", createFundBalance);

            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            var client = new HttpClient { BaseAddress = new Uri(Assert.Single(addresses!.Addresses)) };
            return new SecurityTestHost(
                app,
                client,
                companyId,
                otherCompanyId,
                periodId,
                otherPeriodId,
                assetCategoryId,
                foreignCategoryId,
                loanId,
                foreignLoanId,
                cleanup);
        }

        public async Task InScopeAsync(Func<AccountsDbContext, Task> action)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<AccountsDbContext>());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            try
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            finally
            {
                if (_cleanup is not null)
                    await _cleanup();
            }
        }
    }
}
