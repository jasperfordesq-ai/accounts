using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

public sealed class DirectorLoanComplianceEvidenceTests
{
    [Fact]
    public async Task InputValidationRejectsOfficerWithoutVerifiedAppointmentDate()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        fixture.Director.AppointedDate = null;
        await db.SaveChangesAsync();
        var input = new DirectorLoanInput(
            fixture.Director.Id,
            OpeningBalance: 100m,
            Advances: 0m,
            Repayments: 0m,
            ClosingBalance: 100m,
            InterestRate: 0m,
            InterestCharged: 0m,
            IsDocumented: true,
            LoanTerms: "Written terms",
            MaxBalanceDuringYear: 100m);

        var validation = await DirectorLoanInputs.ValidateAsync(
            db,
            fixture.Company.Id,
            fixture.Period.Id,
            input);

        Assert.NotNull(validation);
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(-1000, 1, false)]
    [InlineData(100000, 9999.99, true)]
    [InlineData(100000, 10000, false)]
    public async Task Section240_UsesStrictRelevantAssetsBoundary(
        decimal relevantAssets,
        decimal exposure,
        bool expectedBelow)
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        var boundaryLoan = IndividualLoan(fixture, exposure);
        boundaryLoan.RelevantAssetsAmount = relevantAssets;
        db.DirectorLoans.Add(boundaryLoan);
        await db.SaveChangesAsync();

        var result = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);

        var loan = Assert.Single(result.Loans);
        Assert.Equal(expectedBelow, loan.Section240StrictlyBelowThreshold);
        if (!expectedBelow)
        {
            Assert.Contains(result.BlockingIssues, issue => relevantAssets <= 0
                ? issue.Contains("zero, negative or missing", StringComparison.Ordinal)
                : issue.Contains("strictly below", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Section236_PresumedInterestUsesDatedBalances_NotAverageBalance()
    {
        var loan = new DirectorLoan
        {
            OpeningBalance = 1_000m,
            ClosingBalance = 1_500m,
            BalanceMovements =
            [
                new DirectorLoanMovement
                {
                    MovementDate = new DateOnly(2026, 7, 1),
                    MovementType = DirectorLoanMovementType.Advance,
                    Amount = 1_000m
                },
                new DirectorLoanMovement
                {
                    MovementDate = new DateOnly(2026, 10, 1),
                    MovementType = DirectorLoanMovementType.Repayment,
                    Amount = 500m
                }
            ]
        };

        var interest = DirectorLoanComplianceService.CalculateTimeWeightedInterest(
            loan,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            DirectorLoanComplianceService.AppropriateRatePercent);

        Assert.Equal(68.90m, interest);
        Assert.NotEqual(((loan.OpeningBalance + loan.ClosingBalance) / 2m) * 0.05m, interest);
    }

    [Fact]
    public async Task Section243_IsOnlyAcceptedForRetainedIntraGroupEvidence()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        var invalid = IndividualLoan(fixture, 100m);
        invalid.ComplianceBasis = DirectorLoanComplianceBasis.Section243IntraGroup;
        invalid.ExceptionEvidenceReference = "group-chart.pdf#relationship";
        db.DirectorLoans.Add(invalid);
        await db.SaveChangesAsync();

        var invalidResult = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains(invalidResult.BlockingIssues, issue => issue.Contains("intra-group exception", StringComparison.Ordinal));

        db.DirectorLoans.Remove(invalid);
        db.DirectorLoans.Add(GroupCompanyLoan(fixture, 100m));
        await db.SaveChangesAsync();

        var validResult = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.DoesNotContain(validResult.BlockingIssues, issue => issue.Contains("Section 243", StringComparison.Ordinal));
        Assert.Equal(0m, validResult.Section236PresumedInterest);
    }

    [Fact]
    public async Task Section242_ValidatesAllSapDatesContentAndReferences()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        var loan = IndividualLoan(fixture, 100m);
        loan.ComplianceBasis = DirectorLoanComplianceBasis.Section242SummaryApprovalProcedure;
        loan.SapDeclarationDate = new DateOnly(2026, 1, 10);
        loan.SapResolutionDate = new DateOnly(2026, 1, 20);
        loan.SapActivityStartDate = new DateOnly(2026, 2, 1);
        loan.SapCroFilingDate = new DateOnly(2026, 2, 20);
        loan.SapDeclarationReference = "sap-declaration.pdf#sha256=abc";
        loan.SapResolutionReference = "special-resolution.pdf#sha256=def";
        loan.SapCroFilingReference = "CRO-G1-REFERENCE-123";
        loan.SapDeclarationCoversSection203Matters = true;
        db.DirectorLoans.Add(loan);
        await db.SaveChangesAsync();

        var valid = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.DoesNotContain(valid.BlockingIssues, issue => issue.Contains("SAP", StringComparison.Ordinal));

        loan.SapCroFilingDate = loan.SapActivityStartDate!.Value.AddDays(22);
        await db.SaveChangesAsync();
        var late = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains(late.BlockingIssues, issue => issue.Contains("within 21 days", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Section244And245_RequireTheirOwnExceptionEvidence()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        var expenseLoan = IndividualLoan(fixture, 100m);
        expenseLoan.ComplianceBasis = DirectorLoanComplianceBasis.Section244VouchedExpense;
        expenseLoan.ExpenseIncurredDate = new DateOnly(2026, 1, 1);
        expenseLoan.ExpenseDischargedDate = new DateOnly(2026, 7, 2);
        expenseLoan.ExceptionEvidenceReference = "vouched-expense.pdf";
        db.DirectorLoans.Add(expenseLoan);
        var businessLoan = IndividualLoan(fixture, 50m);
        businessLoan.ComplianceBasis = DirectorLoanComplianceBasis.Section245OrdinaryBusiness;
        businessLoan.OrdinaryCourseConfirmed = true;
        businessLoan.NoMoreFavourableTermsConfirmed = false;
        businessLoan.ExceptionEvidenceReference = "customer-comparator.pdf";
        db.DirectorLoans.Add(businessLoan);
        await db.SaveChangesAsync();

        var result = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Contains(result.BlockingIssues, issue => issue.Contains("within six months", StringComparison.Ordinal));
        Assert.Contains(result.BlockingIssues, issue => issue.Contains("no more favourable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnresolvedArrangementAppearsInFinalOutputBlockersAndSignOffPacket()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        fixture.Company.HasDirectorLoans = true;
        var unresolvedLoan = IndividualLoan(fixture, 100m);
        unresolvedLoan.ComplianceBasis = DirectorLoanComplianceBasis.Unassessed;
        unresolvedLoan.ReviewDecision = DirectorLoanReviewDecision.Unreviewed;
        unresolvedLoan.ReviewedBy = null;
        unresolvedLoan.ReviewerRole = null;
        unresolvedLoan.ReviewedAtUtc = null;
        db.DirectorLoans.Add(unresolvedLoan);
        await db.SaveChangesAsync();

        var compliance = await Service(db).GetComplianceStatusAsync(fixture.Company.Id, fixture.Period.Id);
        var finalBlockers = await new FinancialStatementsService(db)
            .GetFinalOutputReadinessBlockersAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.False(compliance.SignOffPacket.ReadyForFinalOutput);
        Assert.Contains(compliance.SignOffPacket.OpenBlockers, issue => issue.Contains("legal basis", StringComparison.Ordinal));
        Assert.Contains(finalBlockers, issue => issue.Contains("director-loan compliance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Section307NoteCarriesRequiredParticularsAndDoesNotClaimAutomaticSapCompliance()
    {
        await using var db = CreateDbContext();
        var fixture = await SeedPeriodAsync(db);
        var disclosureLoan = IndividualLoan(fixture, 1_000m);
        disclosureLoan.Advances = 500m;
        disclosureLoan.Repayments = 200m;
        disclosureLoan.ClosingBalance = 1_300m;
        disclosureLoan.MaxBalanceDuringYear = 1_500m;
        disclosureLoan.InterestRate = 2.5m;
        disclosureLoan.InterestCharged = 25m;
        disclosureLoan.AllowanceMade = 10m;
        disclosureLoan.BalanceMovements =
        [
            new DirectorLoanMovement
            {
                MovementDate = new DateOnly(2026, 3, 1),
                MovementType = DirectorLoanMovementType.Advance,
                Amount = 500m,
                EvidenceReference = "bank-ledger#advance"
            },
            new DirectorLoanMovement
            {
                MovementDate = new DateOnly(2026, 9, 1),
                MovementType = DirectorLoanMovementType.Repayment,
                Amount = 200m,
                EvidenceReference = "bank-ledger#repayment"
            }
        ];
        db.DirectorLoans.Add(disclosureLoan);
        await db.SaveChangesAsync();

        var note = await Service(db).GenerateSection307NoteAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Contains("Opening balance", note);
        Assert.Contains("Advances", note);
        Assert.Contains("Repayments", note);
        Assert.Contains("Allowance", note);
        Assert.Contains("Maximum outstanding", note);
        Assert.Contains("Closing balance", note);
        Assert.Contains("Interest rate", note);
        Assert.Contains("Written-terms status", note);
        Assert.Contains("Other main conditions", note);
        Assert.Contains("Written repayment date and explicit interest terms", note);
        Assert.Contains("Preceding financial year", note);
        Assert.DoesNotContain("SAP required under s.239", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndependentFixtureCoversRequiredCasesButCannotClaimHumanAcceptance()
    {
        var fixturePath = FindFixturePath("director-loan-compliance-independent-v1.json");
        using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var root = document.RootElement;
        Assert.Equal("pending-qualified-accountant", root.GetProperty("independentQualifiedAccountantReview").GetString());
        var codes = root.GetProperty("scenarios")
            .EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("section240-zero-relevant-assets", codes);
        Assert.Contains("section240-negative-relevant-assets", codes);
        Assert.Contains("section240-one-cent-below-boundary", codes);
        Assert.Contains("section240-exact-boundary", codes);
        Assert.Contains("section236-written-complete", codes);
        Assert.Contains("section236-not-written-dated-movements", codes);
        Assert.Contains("section243-intra-group", codes);
        Assert.Contains("section242-valid-sap-timing", codes);
    }

    private static DirectorLoanComplianceService Service(AccountsDbContext db) =>
        new(db, new FinancialStatementsService(db));

    private static string FindFixturePath(string fileName)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var outputCandidate = Path.Combine(directory.FullName, "Fixtures", fileName);
                if (File.Exists(outputCandidate))
                    return outputCandidate;
                var candidate = Path.Combine(directory.FullName, "backend", "Accounts.Tests", "Fixtures", fileName);
                if (File.Exists(candidate))
                    return candidate;
                var localCandidate = Path.Combine(directory.FullName, "Accounts.Tests", "Fixtures", fileName);
                if (File.Exists(localCandidate))
                    return localCandidate;
                directory = directory.Parent;
            }
        }
        throw new FileNotFoundException($"Could not locate director-loan fixture {fileName}.");
    }

    private static AccountsDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<TestFixture> SeedPeriodAsync(AccountsDbContext db)
    {
        var company = new Company
        {
            TenantId = 1,
            LegalName = "Director Loan Evidence Limited",
            CroNumber = "765432",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2020, 1, 1),
            IsTrading = true
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true
        };
        var director = new CompanyOfficer
        {
            CompanyId = company.Id,
            Name = "Evidence Director",
            Role = OfficerRole.Director,
            AppointedDate = new DateOnly(2020, 1, 1)
        };
        db.AccountingPeriods.Add(period);
        db.CompanyOfficers.Add(director);
        await db.SaveChangesAsync();
        return new TestFixture(company, period, director);
    }

    private static DirectorLoan IndividualLoan(TestFixture fixture, decimal exposure) => new()
    {
        PeriodId = fixture.Period.Id,
        DirectorId = fixture.Director.Id,
        CounterpartyType = DirectorLoanCounterpartyType.Director,
        ArrangementType = DirectorLoanArrangementType.Loan,
        ArrangementDate = fixture.Period.PeriodStart,
        OpeningBalance = exposure,
        ClosingBalance = exposure,
        MaxBalanceDuringYear = exposure,
        TermsStatus = DirectorLoanTermsStatus.WrittenComplete,
        IsDocumented = true,
        LoanTerms = "Written repayment date and explicit interest terms retained with the agreement.",
        ComplianceBasis = DirectorLoanComplianceBasis.Section240BelowTenPercent,
        RelevantAssetsBasis = DirectorLoanRelevantAssetsBasis.LastLaidEntityFinancialStatements,
        RelevantAssetsAmount = 100_000m,
        RelevantAssetsAsOfDate = fixture.Period.PeriodStart.AddDays(-1),
        RelevantAssetsReference = "last-laid-financial-statements.pdf#net-assets",
        RelevantAssetsFallReview = DirectorLoanRelevantAssetsFallReview.NoRelevantFall,
        ReviewDecision = DirectorLoanReviewDecision.Accepted,
        ReviewNote = "Reviewed against the retained statutory evidence and underlying ledger.",
        ReviewedBy = "Evidence Reviewer",
        ReviewerRole = "Accountant",
        ReviewedAtUtc = DateTime.UtcNow
    };

    private static DirectorLoan GroupCompanyLoan(TestFixture fixture, decimal exposure) => new()
    {
        PeriodId = fixture.Period.Id,
        CounterpartyType = DirectorLoanCounterpartyType.GroupCompany,
        CounterpartyName = "Evidence Holdings Limited",
        ArrangementType = DirectorLoanArrangementType.Loan,
        ArrangementDate = fixture.Period.PeriodStart,
        OpeningBalance = exposure,
        ClosingBalance = exposure,
        MaxBalanceDuringYear = exposure,
        TermsStatus = DirectorLoanTermsStatus.WrittenComplete,
        IsDocumented = true,
        LoanTerms = "Written intra-group agreement with repayment date and explicit interest terms.",
        ComplianceBasis = DirectorLoanComplianceBasis.Section243IntraGroup,
        ExceptionEvidenceReference = "group-structure.pdf#subsidiary-relationship",
        ReviewDecision = DirectorLoanReviewDecision.Accepted,
        ReviewNote = "Reviewed against the retained group structure and written agreement.",
        ReviewedBy = "Evidence Reviewer",
        ReviewerRole = "Accountant",
        ReviewedAtUtc = DateTime.UtcNow
    };

    private sealed record TestFixture(Company Company, AccountingPeriod Period, CompanyOfficer Director);
}
