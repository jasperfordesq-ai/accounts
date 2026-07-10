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
    static AccountsWorkflowTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static AccountsDbContext CreateDbContext(string? databaseName = null, InMemoryDatabaseRoot? root = null)
    {
        var builder = new DbContextOptionsBuilder<AccountsDbContext>();
        if (root is null)
            builder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());
        else
            builder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString(), root);

        return new AccountsDbContext(builder.Options);
    }

    private static AccountsDbContext CreateDbContext(string databaseName, InMemoryDatabaseRoot root, HttpContext httpContext)
    {
        var builder = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName, root);

        return new AccountsDbContext(
            builder.Options,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static AccountsDbContext CreateAuditFailingDbContext(string databaseName, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        return new AuditFailingDbContext(options);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) })
        {
            if (string.IsNullOrWhiteSpace(startPath))
                continue;

            var directory = new DirectoryInfo(startPath);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yml")))
            {
                directory = directory.Parent;
            }

            if (directory is not null)
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static string ProductionReadinessSource(string repositoryRoot) => string.Join(
        "\n",
        Directory
            .GetFiles(
                Path.Combine(repositoryRoot, "backend", "Accounts.Api", "Services"),
                "ProductionReadiness*.cs",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));

    private static string AccountsWorkflowTestSource() => string.Join(
        "\n",
        Directory
            .GetFiles(
                Path.Combine(RepositoryRoot(), "backend", "Accounts.Tests"),
                "AccountsWorkflowTests*.cs",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));

    private static string TestMethodSnippet(string source, string methodName)
    {
        var declaration = Regex.Match(
            source,
            $@"\n\s+public\s+(?:async\s+)?Task\s+{Regex.Escape(methodName)}\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        Assert.True(declaration.Success, $"Expected to find test method {methodName}.");
        var start = declaration.Index;
        var next = Regex.Match(
            source[(start + declaration.Length)..],
            @"\n\s+\[(?:Fact|Theory)\]",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
        return next.Success
            ? source[start..(start + declaration.Length + next.Index)]
            : source[start..];
    }

    private static void AssertOccursBefore(string snippet, string first, string second)
    {
        var firstIndex = snippet.IndexOf(first, StringComparison.Ordinal);
        var secondIndex = snippet.IndexOf(second, StringComparison.Ordinal);
        Assert.True(firstIndex >= 0, $"Expected snippet to contain '{first}'.");
        Assert.True(secondIndex >= 0, $"Expected snippet to contain '{second}'.");
        Assert.True(firstIndex < secondIndex, $"Expected '{first}' before '{second}'.");
    }

    private static async Task<AccountingPeriod> SeedCompanyPeriodAsync(AccountsDbContext db, bool isFirstYear)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Example Micro Limited",
            CroNumber = "123456",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.CompanyOfficers.AddRange(
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "A Director",
                Role = OfficerRole.Director,
                AppointedDate = company.IncorporationDate
            },
            new CompanyOfficer
            {
                CompanyId = company.Id,
                Name = "B Secretary",
                Role = OfficerRole.Secretary,
                AppointedDate = company.IncorporationDate
            }
        );

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            IsFirstYear = isFirstYear
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        return period;
    }

    private static async Task SeedAcceptedCroFilingPackageAsync(AccountsDbContext db, int periodId)
    {
        var period = await db.AccountingPeriods.SingleAsync(candidate => candidate.Id == periodId);
        var officers = await db.CompanyOfficers
            .Where(officer => officer.CompanyId == period.CompanyId)
            .ToListAsync();
        if (!officers.Any(officer => officer.Role == OfficerRole.Director))
        {
            db.CompanyOfficers.Add(new CompanyOfficer
            {
                CompanyId = period.CompanyId,
                Name = "Deadline Test Director",
                Role = OfficerRole.Director,
                AppointedDate = period.PeriodStart
            });
        }
        if (!officers.Any(officer => officer.Role is OfficerRole.Secretary or OfficerRole.CompanySecretary))
        {
            db.CompanyOfficers.Add(new CompanyOfficer
            {
                CompanyId = period.CompanyId,
                Name = "Deadline Test Secretary",
                Role = OfficerRole.Secretary,
                AppointedDate = period.PeriodStart
            });
        }
        await db.SaveChangesAsync();
        await PrepareFinalisedReleaseTestPeriodAsync(db, period);
        var statements = new FinancialStatementsService(db);
        var workflow = new FilingWorkflowService(db, statements, new IxbrlService(db, statements));
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "accounts", retainedFinalArtifact: [1, 2, 3]);
        await workflow.RecordCroDocumentGeneratedAsync(period.CompanyId, period.Id, "signature", retainedFinalArtifact: [4, 5, 6]);
        await BindTrustedCroApprovalAsync(db, period);
        await workflow.UpdateCroStatusAsync(
            period.CompanyId,
            period.Id,
            FilingStatus.Submitted,
            "Reviewer",
            submissionReference: "CORE-ACCEPTED-TEST");
        await workflow.ConfirmCroPaymentAsync(period.CompanyId, period.Id, "Reviewer");
        await workflow.UpdateCroStatusAsync(period.CompanyId, period.Id, FilingStatus.Accepted, "Reviewer");
    }

    private static async Task PrepareFinalisedReleaseTestPeriodAsync(AccountsDbContext db, AccountingPeriod period)
    {
        if (!await db.SizeClassifications.AnyAsync(classification => classification.PeriodId == period.Id))
            await MakePeriodReadyForCroDocumentsAsync(db, period);
        await FinaliseReleaseTestPeriodAsync(db, period);
    }

    private static async Task SeedRevenueInternalIxbrlChecksPassedAsync(
        AccountsDbContext db,
        int periodId,
        string? ct1Reference = "ROS-INTERNAL-CHECKS-TEST")
    {
        db.RevenueFilingPackages.Add(new RevenueFilingPackage
        {
            PeriodId = periodId,
            FilingStatus = FilingStatus.InProgress,
            IxbrlGenerated = true,
            IxbrlValidated = false,
            IxbrlValidationErrors = "Internal checks passed. External ROS/iXBRL validation is still required before Revenue filing.",
            Ct1Reference = ct1Reference
        });
        await db.SaveChangesAsync();
    }

    private static AccountCategory AddCategory(
        AccountsDbContext db,
        int companyId,
        string code,
        string name,
        AccountCategoryType type)
    {
        var category = new AccountCategory
        {
            CompanyId = companyId,
            Code = code,
            Name = name,
            Type = type,
            IsSystem = true
        };
        db.AccountCategories.Add(category);
        db.SaveChanges();
        return category;
    }

    private static void SetRequiredDate(object target, string propertyName, DateOnly value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} is required for period-effective accounting data.");
        property.SetValue(target, value);
    }

    private static void SetRequiredValue(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} is required.");
        property.SetValue(target, value);
    }

    private static DefaultHttpContext WriteAuditContext(string method, string path, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        context.TraceIdentifier = "trace-" + correlationId;
        context.Items[AuthContext.ItemKey] = new AuthenticatedUser(
            UserId: 7,
            TenantId: 1,
            TenantName: "Firm A",
            Email: "reviewer@example.ie",
            DisplayName: "Maeve Reviewer",
            Role: "Reviewer");
        return context;
    }

    private static DefaultHttpContext AuthenticatedRequest(string role, string method, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (!HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method)
            && !HttpMethods.IsOptions(method)
            && !HttpMethods.IsTrace(method))
        {
            context.Request.Headers[IdempotencyHttpContract.RequestHeader] = $"test-command-{Guid.NewGuid():N}";
        }
        context.Items[AuthContext.ItemKey] = AuthenticatedRole(role);
        return context;
    }

    private static DefaultHttpContext MonitoringSmokeContext(string role, string correlationId)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response =
            {
                Body = new MemoryStream()
            }
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/system/monitoring/error-smoke";
        context.TraceIdentifier = correlationId;
        context.Items[AuthContext.ItemKey] = AuthenticatedRole(role) with
        {
            UserId = 41,
            Email = "owner@example.ie"
        };
        return context;
    }

    private static ApiAccessService DisabledApiAccess() =>
        new(
            Options.Create(new ApiAccessConfig { Enabled = false }),
            new TestEnvironment("Development"));

    private static async Task LogAuditWithEvidenceMetadataAsync(
        AuditService audit,
        int? companyId,
        int? periodId,
        string entityType,
        int entityId,
        string action,
        object? oldValue,
        object? newValue,
        string? userId,
        int tenantId,
        string requestId,
        string actorDisplayName)
    {
        var method = typeof(AuditService)
            .GetMethods()
            .SingleOrDefault(m =>
            {
                if (m.Name != nameof(AuditService.LogAsync))
                    return false;

                var names = m.GetParameters().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return names.Contains("tenantId")
                    && names.Contains("requestId")
                    && names.Contains("actorDisplayName");
            });

        Assert.True(
            method is not null,
            "AuditService.LogAsync should accept tenantId, requestId, and actorDisplayName metadata.");

        var arguments = method.GetParameters()
            .Select(parameter => parameter.Name?.ToLowerInvariant() switch
            {
                "companyid" => companyId,
                "periodid" => periodId,
                "entitytype" => entityType,
                "entityid" => entityId,
                "action" => action,
                "oldvalue" => oldValue,
                "newvalue" => newValue,
                "userid" => userId,
                "tenantid" => tenantId,
                "requestid" => requestId,
                "actordisplayname" => actorDisplayName,
                _ when parameter.HasDefaultValue => parameter.DefaultValue,
                _ => throw new InvalidOperationException($"Unexpected AuditService.LogAsync parameter '{parameter.Name}'.")
            })
            .ToArray();

        var result = method.Invoke(audit, arguments);
        await Assert.IsAssignableFrom<Task>(result);
    }

    private static void AssertAuditLogValue(AuditLog entry, string propertyName, object expected) =>
        Assert.Equal(expected, ReadAuditLogValue(entry, propertyName));

    private static int? ResultStatusCode(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static object? ReadAuditLogValue(AuditLog entry, string propertyName)
    {
        var property = typeof(AuditLog).GetProperty(propertyName);
        Assert.True(property is not null, $"AuditLog should persist evidence metadata property '{propertyName}'.");
        return property.GetValue(entry);
    }

    private static string RequiredAuditLogString(AuditLog entry, string propertyName)
    {
        var value = ReadAuditLogValue(entry, propertyName);
        return Assert.IsType<string>(value);
    }

    private static string RequiredCookieValue(CookieContainer cookies, Uri baseUri, string cookieName)
    {
        var value = cookies.GetCookies(baseUri)[cookieName]?.Value;
        Assert.False(string.IsNullOrWhiteSpace(value), $"Expected cookie '{cookieName}' to be set.");
        return value;
    }

    private static string LoopbackBaseAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses
            ?? [];
        var loopbackAddresses = addresses
            .Where(value => value.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            loopbackAddresses.Length == 1,
            $"Expected Kestrel to publish exactly one http://127.0.0.1 loopback test address; found {loopbackAddresses.Length}: {string.Join(", ", addresses)}.");
        var address = loopbackAddresses[0];
        Assert.False(string.IsNullOrWhiteSpace(address), "Expected Kestrel to publish an actual loopback test address.");
        Assert.False(
            address!.EndsWith(":0", StringComparison.Ordinal),
            "Expected Kestrel to replace port 0 with the actual bound port.");
        return address;
    }

    private static bool IsHex64(string value) =>
        value.Length == 64 && value.All(c =>
            c is >= '0' and <= '9'
            || c is >= 'a' and <= 'f'
            || c is >= 'A' and <= 'F');

    private static async Task MakePeriodReadyForCroDocumentsAsync(AccountsDbContext db, AccountingPeriod period)
    {
        period.ApprovalDate ??= period.PeriodEnd;
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = CompanySizeClass.Micro,
            Turnover = 100m,
            BalanceSheetTotal = 101m,
            AvgEmployees = 1
        });

        var bankCategory = AddCategory(db, period.CompanyId, "1400", "Bank Current Account", AccountCategoryType.Asset);
        var salesCategory = AddCategory(db, period.CompanyId, "4000", "Sales / Revenue", AccountCategoryType.Income);
        var shareCapitalCategory = AddCategory(db, period.CompanyId, "3000", "Share Capital", AccountCategoryType.Equity);

        var bankAccount = new BankAccount
        {
            CompanyId = period.CompanyId,
            Name = "Current Account",
            OpeningBalance = 1m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bankAccount);
        await db.SaveChangesAsync();

        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = shareCapitalCategory.Id,
            Credit = 1m,
            SourceNote = "Opening share capital per register",
            EnteredBy = "Accounts reviewer",
            Reviewed = true,
            ReviewedBy = "Accounts reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = period.CompanyId,
            ShareClass = "Ordinary",
            NumberIssued = 1,
            NominalValue = 1m,
            TotalValue = 1m,
            IssueDate = period.PeriodStart
        });
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bankAccount.Id,
            PeriodId = period.Id,
            Date = period.PeriodStart.AddMonths(2),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = salesCategory.Id
        });
        db.YearEndReviewConfirmations.AddRange(
            NilReview(period.Id, "adjustments"),
            NilReview(period.Id, "debtors"),
            NilReview(period.Id, "creditors"),
            NilReview(period.Id, "inventory"),
            NilReview(period.Id, "payroll"),
            NilReview(period.Id, "tax"),
            NilReview(period.Id, "dividends"),
            NilReview(period.Id, "post-balance-sheet-events"),
            NilReview(period.Id, "related-parties"),
            NilReview(period.Id, "contingent-liabilities"),
            NilReview(period.Id, "going-concern"),
            NilReview(period.Id, "note-directors-remuneration", "Directors' remuneration disclosure reviewed; nil for this test fixture."),
            NilReview(period.Id, "note-financial-instruments", "Financial instruments disclosure reviewed; no additional matters for this test fixture."),
            NilReview(period.Id, "note-capital-commitments", "Capital commitments disclosure reviewed; nil for this test fixture."),
            NilReview(period.Id, "note-deferred-tax", "Deferred tax disclosure reviewed; nil for this test fixture."),
            NilReview(
                period.Id,
                DirectorsReportService.PrincipalActivitiesReviewKey,
                "The principal activity of the company is the provision of professional services."),
            NilReview(
                period.Id,
                DirectorsReportService.AuditInformationReviewKey,
                "Director confirmations and audit-information enquiries retained at WP-DR-330."));
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);

        _ = bankCategory;
    }

    private static void AttachCompleteAuditorReportEvidence(AccountingPeriod period, string reference)
    {
        var signedAt = DateTime.UtcNow.AddMinutes(-5);
        var artifact = Encoding.ASCII.GetBytes("%PDF-1.7\nSynthetic signed auditor opinion fixture\n%%EOF");
        period.AuditorsReportReceived = true;
        period.AuditorsReportReference = reference;
        period.AuditorsReportFirmName = "Example Audit Firm";
        period.AuditorsReportSignerName = "Qualified Auditor";
        period.AuditorsReportProfessionalBody = "Chartered Accountants Ireland";
        period.AuditorsReportMembershipNumber = "TEST-12345";
        period.AuditorsReportSignedAt = signedAt;
        period.AuditorsReportReviewedBy = "Qualified Accountant Reviewer";
        period.AuditorsReportReviewedAt = signedAt.AddMinutes(1);
        period.AuditorsReportReviewDecision = "accepted";
        period.AuditorsReportArtifact = artifact;
        period.AuditorsReportSha256 = FilingReleaseGate.ComputeSha256(artifact);
    }

    private static YearEndReviewConfirmation NilReview(
        int periodId,
        string sectionKey,
        string note = "Nil position reviewed.") => new()
    {
        PeriodId = periodId,
        SectionKey = sectionKey,
        Confirmed = true,
        ConfirmedBy = "Accounts reviewer",
        Note = note
    };

    private static AuthService CreateAuthService(
        AccountsDbContext db,
        string? signingKey = null,
        string environmentName = "Development",
        int expiryMinutes = 60,
        bool secureCookiesInProduction = true,
        IPasswordVerifier? passwordVerifier = null,
        TimeProvider? timeProvider = null) =>
        new(
            db,
            Options.Create(new AuthSessionConfig
            {
                SigningKey = signingKey ?? StrongSessionSigningKey(),
                ExpiryMinutes = expiryMinutes,
                RequirePrivilegedMfa = false,
                SecureCookiesInProduction = secureCookiesInProduction
            }),
            new TestEnvironment(environmentName),
            passwordVerifier ?? new Pbkdf2PasswordVerifier(),
            timeProvider);

    private static async Task<Tenant> SeedTenantAsync(
        AccountsDbContext db,
        string name = "Example Firm",
        string slug = "example-firm")
    {
        var tenant = new Tenant
        {
            Name = name,
            Slug = slug
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static async Task<Company> SeedTenantCompanyAsync(
        AccountsDbContext db,
        int tenantId,
        string legalName)
    {
        var company = new Company
        {
            TenantId = tenantId,
            LegalName = legalName,
            CroNumber = $"T{tenantId}{Guid.NewGuid():N}"[..20],
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2025, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 9, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Main Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return company;
    }

    private static async Task<UserAccount> SeedUserAsync(
        AccountsDbContext db,
        Tenant tenant,
        string email,
        string password,
        bool isActive = true,
        string role = "Owner",
        string passwordAlgorithm = AuthService.PasswordAlgorithm)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            210_000,
            HashAlgorithmName.SHA256,
            32);
        var user = new UserAccount
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = "Owner User",
            Role = role,
            PasswordHash = Convert.ToBase64String(hash),
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordAlgorithm = passwordAlgorithm,
            PasswordStrengthScore = 5,
            IsActive = isActive
        };
        db.UserAccounts.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IOptions<AuthSessionConfig> AuthSessionOptions(IConfiguration config) =>
        Options.Create(config.GetSection("AuthSession").Get<AuthSessionConfig>() ?? new AuthSessionConfig());

    private static AuditIntegrityConfig AuditIntegrityCheckpointOptions() => new()
    {
        ActiveKeyId = "audit-key-2026",
        SigningKeys =
        [
            new AuditIntegritySigningKeyConfig
            {
                KeyId = "audit-key-2026",
                SigningKey = StrongAuditCheckpointSigningKey()
            }
        ]
    };

    private static IOptions<MonitoringConfig> MonitoringOptions() =>
        Options.Create(new MonitoringConfig
        {
            ErrorTrackingProvider = "Sentry-compatible",
            ErrorTrackingDsn = "https://public@sentry.example.ie/1",
            StructuredJsonConsole = true,
            IncludeCorrelationId = true,
            OnCallOwner = "Test Operations Owner",
            AlertRoute = "test-operations-route"
        });

    private static IOptions<DeadlineDeliveryConfig> SecureDeadlineDeliveryOptions() =>
        Options.Create(new DeadlineDeliveryConfig
        {
            Enabled = true,
            Provider = "Webhook",
            ProviderEndpoint = "https://deadline-provider.example.test/reminders",
            ProviderToken = new string('d', 48)
        });

    private static IOptions<PlatformMetricsConfig> SecurePlatformMetricsOptions() =>
        Options.Create(new PlatformMetricsConfig());

    private static IOptions<DatabaseTenantIsolationConfig> SecureDatabaseTenantIsolationOptions() =>
        Options.Create(new DatabaseTenantIsolationConfig
        {
            Required = true,
            ApplicationGroupRole = TenantIsolationPolicyCatalog.ApplicationGroupRole,
            ApplicationLoginRole = "accounts_api",
            ContextSigningKey = Convert.ToBase64String(Enumerable.Range(1, 64).Select(value => (byte)value).ToArray())
        });

    private static IOptions<IdentitySecurityConfig> SecureIdentitySecurityOptions() =>
        Options.Create(new IdentitySecurityConfig
        {
            RequireInProduction = true,
            BreachedPasswordCheckEnabled = true,
            BreachedPasswordFailClosed = true,
            IdentityHmacKey = Convert.ToBase64String(Enumerable.Range(65, 64).Select(value => (byte)value).ToArray()),
            ActiveMfaEncryptionKeyId = "test-primary",
            MfaEncryptionKeys =
            [
                new MfaEncryptionKeyConfig
                {
                    KeyId = "test-primary",
                    EncryptionKey = Convert.ToBase64String(Enumerable.Range(129, 64).Select(value => (byte)value).ToArray())
                }
            ]
        });

    private static string StrongSessionSigningKey() =>
        Convert.ToBase64String(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());

    private static string StrongSessionSigningKeyBase64Url() =>
        StrongSessionSigningKey().TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string DevelopmentSessionSigningKey() =>
        "IyRih0V4m+9WHp+buroLFov10a+LGRhgg8g4J7vm3uTnRtUU6t1JenYYfZTaqT9Gl9H7FmYhGwNORssVZ/BPkg==";

    private static string StrongAuditCheckpointSigningKey() =>
        Convert.ToBase64String(Enumerable.Range(64, 64).Select(i => (byte)i).ToArray());

    private static AuthenticatedUser AuthenticatedRole(string role) => new(
        UserId: 1,
        TenantId: 1,
        TenantName: "Example Firm",
        Email: "user@example.ie",
        DisplayName: "Example User",
        Role: role);

    private static QualifiedAccountantApprovalEvidence TrustedAccountantEvidence(
        int tenantId,
        FilingReleaseWorkflow workflow)
    {
        var artifact = Encoding.UTF8.GetBytes("professional-register-response:trusted-member:current");
        return new QualifiedAccountantApprovalEvidence(
            "Trusted Qualified Accountant",
            tenantId,
            workflow switch
            {
                FilingReleaseWorkflow.Cro => "cro-final-filing",
                FilingReleaseWorkflow.Revenue => "revenue-final-filing",
                FilingReleaseWorkflow.Charity => "charity-final-filing",
                _ => throw new ArgumentOutOfRangeException(nameof(workflow))
            },
            "qualified-accountant",
            "approved",
            "Chartered Accountants Ireland",
            "trusted-member",
            "https://register.example.test/trusted-member/check",
            artifact,
            FilingReleaseGate.ComputeSha256(artifact),
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddDays(30));
    }

    private static async Task<CroFilingPackage> BindTrustedCroApprovalAsync(
        AccountsDbContext db,
        AccountingPeriod period)
    {
        var package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        var tenantId = await db.Companies
            .Where(c => c.Id == period.CompanyId)
            .Select(c => c.TenantId)
            .SingleAsync() ?? throw new InvalidOperationException("Test company tenant is missing.");
        var gate = new FilingReleaseGate(db, package.ArtifactReleaseCandidate ?? "local-development");
        var signedArtifact = Encoding.UTF8.GetBytes("retained-executed-cro-signing-pack");
        await gate.RecordVerifiedCroSignatureAsync(
            period.CompanyId,
            period.Id,
            new CroSignatureEvidence(
                "A Director",
                "B Secretary",
                DateTime.UtcNow.AddMinutes(-2),
                signedArtifact,
                FilingReleaseGate.ComputeSha256(signedArtifact)));
        await gate.BindVerifiedApprovalAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Cro,
            TrustedAccountantEvidence(tenantId, FilingReleaseWorkflow.Cro));
        package = await db.CroFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        package.FilingStatus = FilingStatus.Approved;
        await db.SaveChangesAsync();
        return package;
    }

    private static async Task<CharityFilingPackage> BindTrustedCharityApprovalAsync(
        AccountsDbContext db,
        AccountingPeriod period)
    {
        var package = await db.CharityFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        var tenantId = await db.Companies
            .Where(c => c.Id == period.CompanyId)
            .Select(c => c.TenantId)
            .SingleAsync() ?? throw new InvalidOperationException("Test company tenant is missing.");
        var gate = new FilingReleaseGate(db, package.ArtifactReleaseCandidate ?? "local-development");
        await gate.BindVerifiedApprovalAsync(
            period.CompanyId,
            period.Id,
            FilingReleaseWorkflow.Charity,
            TrustedAccountantEvidence(tenantId, FilingReleaseWorkflow.Charity));
        package = await db.CharityFilingPackages.SingleAsync(p => p.PeriodId == period.Id);
        package.FilingStatus = FilingStatus.Approved;
        await db.SaveChangesAsync();
        return package;
    }

    private static async Task FinaliseReleaseTestPeriodAsync(AccountsDbContext db, AccountingPeriod period)
    {
        var classification = await db.SizeClassifications.SingleOrDefaultAsync(s => s.PeriodId == period.Id);
        if (classification is not null && string.IsNullOrWhiteSpace(classification.DecisionInputFingerprintSha256))
        {
            var existingRegime = await db.FilingRegimes.SingleOrDefaultAsync(f => f.PeriodId == period.Id);
            var electedRegime = existingRegime?.ElectedRegime;
            var forceAuditRequired = existingRegime?.AuditExempt == false;
            await new SizeClassificationService(db, Options.Create(new SizeThresholdConfig()))
                .ClassifyAsync(period.CompanyId, period.Id);
            var determined = await new FilingRegimeService(db)
                .DetermineAsync(period.CompanyId, period.Id, electedRegime);
            if (forceAuditRequired)
            {
                var savedRegime = await db.FilingRegimes.SingleAsync(f => f.PeriodId == period.Id);
                savedRegime.AuditExempt = false;
            }
            _ = determined;
        }
        period.Status = PeriodStatus.Finalised;
        period.LockedAt = DateTime.UtcNow;
        period.LockedBy = "release-gate-test";
        period.ApprovalDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        await db.SaveChangesAsync();
        await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
    }

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Accounts.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
    }

    private sealed record CapturedErrorReport(Exception Exception, ErrorReportContext Context);

    private static async Task SaveSupportedTaxScopeAsync(AccountsDbContext db, int companyId, int periodId)
    {
        await new TaxComputationService(db, new FinancialStatementsService(db)).SaveScopeReviewAsync(
            companyId,
            periodId,
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
    }

    private sealed class CapturingErrorReporter : IErrorReporter
    {
        public List<CapturedErrorReport> Reports { get; } = [];

        public string CaptureUnexpectedException(Exception exception, ErrorReportContext context)
        {
            Reports.Add(new CapturedErrorReport(exception, context));
            return $"captured-test-event-{Reports.Count}";
        }
    }

    private sealed class FailingIxbrlService(AccountsDbContext db, FinancialStatementsService statementsService) : IxbrlService(db, statementsService)
    {
        public override Task<byte[]> GenerateIxbrlAsync(int companyId, int periodId) =>
            throw new InvalidOperationException("Npgsql failure for password=secret while rendering iXBRL");
    }

    private sealed class FailingProfitAndLossStatementsService(AccountsDbContext db) : FinancialStatementsService(db)
    {
        public override Task<ProfitAndLoss> GetProfitAndLossAsync(int companyId, int periodId) =>
            throw new InvalidOperationException("Simulated profit and loss failure");
    }

    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ThrowingReadStream(string message) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException(message);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class AuditFailingDbContext(DbContextOptions<AccountsDbContext> options) : AccountsDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State == EntityState.Added))
                throw new InvalidOperationException("Simulated audit write failure");

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private sealed class CountingPasswordVerifier : IPasswordVerifier
    {
        public int CallCount { get; private set; }

        public bool Verify(string password, UserAccount? user)
        {
            CallCount++;
            return false;
        }
    }

}
