using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounts.Tests;

// E2E finding (Classify, subsequent year): a later year's size test needs the preceding year's
// figures and a current, hash-linked decision chain back to the first year (Companies Act 2014,
// s.280A). The page gave no warning, so the requirement surfaced only as an error after submit.
// The prerequisites read uses the same chain evaluation as the classifier; the classifier's
// rejection and its messages are unchanged.
public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task ClassificationPrerequisites_FirstYear_IsReadyWithoutAPredecessor()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedClassificationChainAsync(db);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(first.CompanyId, first.Id);

        Assert.True(prerequisites.Ready);
        Assert.True(prerequisites.IsFirstYear);
        Assert.Null(prerequisites.FirstBlockingPeriod);
        Assert.Equal("First financial year: the size test uses this period's figures only.", prerequisites.Message);
    }

    [Fact]
    public async Task ClassificationPrerequisites_NoPredecessorClassification_StartsFromTheEarliestUnclassifiedPeriod()
    {
        await using var db = CreateDbContext();
        var (first, _, _, current) = await SeedClassificationChainAsync(db);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.False(prerequisites.IsFirstYear);
        var blocking = Assert.IsType<ClassificationBlockingPeriod>(prerequisites.FirstBlockingPeriod);
        Assert.Equal(first.Id, blocking.PeriodId);
        Assert.Equal(new DateOnly(2017, 7, 20), blocking.PeriodStart);
        Assert.Equal(new DateOnly(2018, 12, 30), blocking.PeriodEnd);
        Assert.Equal("not classified yet", blocking.Reason);
        Assert.Equal(
            "Classify 20 Jul 2017 – 30 Dec 2018 first, or record historic figures from the filed accounts for 1 Aug 2024 – 31 Jul 2025. Each year's size test uses the previous year's figures (Companies Act 2014, s.280A), so without historic figures every period from 20 Jul 2017 – 30 Dec 2018 must be classified in order.",
            prerequisites.Message);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ClassificationPrerequisites_PartlyClassifiedChain_PointsAtTheFirstGap()
    {
        await using var db = CreateDbContext();
        var (first, second, _, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(second.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        Assert.Equal("not classified yet", prerequisites.FirstBlockingPeriod?.Reason);
        Assert.EndsWith("every period from 31 Dec 2018 – 31 Jul 2019 must be classified in order.", prerequisites.Message);
    }

    [Fact]
    public async Task ClassificationPrerequisites_InputsOnlyPredecessor_IsNotClassifiedYet()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        AddInputsOnlyRow(db, third);
        await db.SaveChangesAsync();

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(third.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        Assert.Equal("not classified yet", prerequisites.FirstBlockingPeriod?.Reason);
        Assert.Equal(
            "Classify 1 Aug 2024 – 31 Jul 2025 first. Each year's size test uses the previous year's figures (Companies Act 2014, s.280A).",
            prerequisites.Message);
    }

    [Fact]
    public async Task ClassificationPrerequisites_StaleChain_PointsAtTheStalePeriod()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        await ClassifyWithFiguresAsync(db, third);
        await ChangeRetainedTurnoverAsync(db, first);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(first.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        Assert.Equal("classification out of date — re-run it", prerequisites.FirstBlockingPeriod?.Reason);
        Assert.Equal(
            "Classify 1 Aug 2024 – 31 Jul 2025 first. Each year's size test uses the previous year's figures (Companies Act 2014, s.280A), so earlier periods must be classified in order, starting from 20 Jul 2017 – 30 Dec 2018. 20 Jul 2017 – 30 Dec 2018: classification out of date — re-run it.",
            prerequisites.Message);
    }

    [Fact]
    public async Task ClassificationPrerequisites_TwoFirstYears_ReportsThePeriodSetUp()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        second.IsFirstYear = true;
        await db.SaveChangesAsync();
        await ClassifyWithFiguresAsync(db, second);
        AddInputsOnlyRow(db, third);
        await db.SaveChangesAsync();

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.False(prerequisites.Ready);
        Assert.Equal(second.Id, prerequisites.FirstBlockingPeriod?.PeriodId);
        Assert.Equal(
            "more than one period is marked as the first financial year — correct the period set-up",
            prerequisites.FirstBlockingPeriod?.Reason);
    }

    [Fact]
    public async Task ClassificationPrerequisites_FullyClassifiedChain_IsReady()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        await ClassifyWithFiguresAsync(db, third);

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.True(prerequisites.Ready);
        Assert.False(prerequisites.IsFirstYear);
        Assert.Null(prerequisites.FirstBlockingPeriod);
        Assert.Equal(
            "Every earlier period has a current classification, so this period can be classified.",
            prerequisites.Message);

        // Ready means the classifier accepts it.
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id);
    }

    [Fact]
    public async Task ClassificationPrerequisites_PredecessorElection_IsNotAFalseBlock()
    {
        // The election is chosen on the form when classifying; the read must not block a period
        // whose predecessors are consistent with each other.
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        await ClassifyWithFiguresAsync(db, third, new DateOnly(2023, 1, 1));

        var prerequisites = await ClassificationService(db).GetPrerequisitesAsync(current.CompanyId, current.Id);

        Assert.True(prerequisites.Ready);
    }

    [Fact]
    public async Task ClassificationPrerequisitesEndpoint_ReturnsThePrerequisitesWithExplicitNulls()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedClassificationChainAsync(db);

        var result = await ClassificationEndpoints.GetClassificationPrerequisitesEndpointAsync(
            first.CompanyId,
            first.Id,
            ClassificationService(db),
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{first.CompanyId}/periods/{first.Id}/classification/prerequisites"));

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var payload = Assert.IsType<ClassificationPrerequisites>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.True(payload.Ready);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, ApiJson));
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("firstBlockingPeriod").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("historicFiguresPeriod").ValueKind);
    }

    [Fact]
    public async Task ClassificationPrerequisitesEndpoint_PeriodOfAnotherCompany_IsNotFound()
    {
        await using var db = CreateDbContext();
        var (first, _, _, _) = await SeedClassificationChainAsync(db);

        var result = await ClassificationEndpoints.GetClassificationPrerequisitesEndpointAsync(
            first.CompanyId + 1000,
            first.Id,
            ClassificationService(db),
            db,
            AuthenticatedRequest("Owner", HttpMethods.Get, $"/api/companies/{first.CompanyId + 1000}/periods/{first.Id}/classification/prerequisites"));

        Assert.Equal(StatusCodes.Status404NotFound, ResultStatusCode(result));
    }

    // The classifier's s.280A rejection keeps its exact messages; the prerequisites read is only an
    // earlier, non-throwing view of the same chain evaluation.
    [Fact]
    public async Task ClassifySubsequentYear_IncompleteChain_KeepsTheStatutoryMessage()
    {
        await using var db = CreateDbContext();
        var (first, _, third, current) = await SeedClassificationChainAsync(db);
        AddInputsOnlyRow(db, third);
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            $"The retained statutory decision chain is incomplete at period {first.Id}; classify every preceding period first.",
            error.Message);
    }

    [Fact]
    public async Task ClassifySubsequentYear_StaleChain_KeepsTheStatutoryMessage()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        await ClassifyWithFiguresAsync(db, third);
        await ChangeRetainedTurnoverAsync(db, first);
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            $"The statutory size decision for preceding period {first.Id} is stale; re-run it before relying on consecutive-year qualification.",
            error.Message);
    }

    [Fact]
    public async Task ClassifySubsequentYear_InputsOnlyPredecessor_KeepsTheStaleMessage()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        AddInputsOnlyRow(db, third);
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            $"The statutory size decision for preceding period {third.Id} is stale; re-run it before relying on consecutive-year qualification.",
            error.Message);
    }

    [Fact]
    public async Task ClassifySubsequentYear_ElectionMismatch_KeepsTheStatutoryMessage()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        await ClassifyWithFiguresAsync(db, second);
        await ClassifyWithFiguresAsync(db, third, new DateOnly(2023, 1, 1));
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            "The 2024 threshold adjustment election must be applied consistently to every financial year of the company beginning on or after 2023-01-01.",
            error.Message);
    }

    [Fact]
    public async Task ClassifySubsequentYear_TwoFirstYears_KeepsTheStatutoryMessage()
    {
        await using var db = CreateDbContext();
        var (first, second, third, current) = await SeedClassificationChainAsync(db);
        await ClassifyWithFiguresAsync(db, first);
        second.IsFirstYear = true;
        await db.SaveChangesAsync();
        await ClassifyWithFiguresAsync(db, second);
        AddInputsOnlyRow(db, third);
        AddInputsOnlyRow(db, current);
        await db.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ClassificationService(db).ClassifyAsync(current.CompanyId, current.Id));

        Assert.Equal(
            "The retained statutory decision chain does not have one deterministic first financial year.",
            error.Message);
    }

    // Side defects: a saved-but-undecided classification row is not a classification.
    [Fact]
    public async Task ReadinessScore_InputsOnlyClassification_IsNotCompleted()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        AddInputsOnlyRow(db, period);
        await db.SaveChangesAsync();

        var undecided = await new FinancialStatementsService(db).GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.Contains("Size classification not completed", undecided.MissingItems);

        await ClassificationService(db).ClassifyAsync(period.CompanyId, period.Id);
        var decided = await new FinancialStatementsService(db).GetReadinessScoreAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain("Size classification not completed", decided.MissingItems);
    }

    [Fact]
    public async Task NotesChecklist_UndecidedClassification_IsTreatedAsNoClassification()
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        // Inputs changed after an earlier Medium decision: the class column is no longer a decision.
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 100m,
            BalanceSheetTotal = 100m,
            AvgEmployees = 1,
            CalculatedClass = CompanySizeClass.Medium,
            DecisionInputFingerprintSha256 = null
        });
        await db.SaveChangesAsync();

        var undecided = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        Assert.DoesNotContain(undecided, note => note.Code == StatutoryNoteCodes.FinancialInstruments);

        var row = await db.SizeClassifications.SingleAsync(s => s.PeriodId == period.Id);
        row.DecisionInputFingerprintSha256 = new string('a', 64);
        await db.SaveChangesAsync();
        var decided = await new NotesDisclosureService(db).GenerateNotesAsync(period.CompanyId, period.Id);
        Assert.Contains(decided, note => note.Code == StatutoryNoteCodes.FinancialInstruments);
    }

    /// <summary>
    /// First year 20 Jul 2017 – 30 Dec 2018, then 31 Dec 2018 – 31 Jul 2019, 1 Aug 2024 – 31 Jul 2025
    /// and the current 1 Aug 2025 – 31 Jul 2026, all unclassified.
    /// </summary>
    private static async Task<(AccountingPeriod First, AccountingPeriod Second, AccountingPeriod Third, AccountingPeriod Current)>
        SeedClassificationChainAsync(AccountsDbContext db)
    {
        var (first, current) = await SeedLiveCharityShapedPeriodsAsync(db);
        var second = new AccountingPeriod
        {
            CompanyId = first.CompanyId,
            PeriodStart = new DateOnly(2018, 12, 31),
            PeriodEnd = new DateOnly(2019, 7, 31),
            IsFirstYear = false
        };
        var third = new AccountingPeriod
        {
            CompanyId = first.CompanyId,
            PeriodStart = new DateOnly(2024, 8, 1),
            PeriodEnd = new DateOnly(2025, 7, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.AddRange(second, third);
        await db.SaveChangesAsync();
        return (first, second, third, current);
    }

    private static void AddInputsOnlyRow(AccountsDbContext db, AccountingPeriod period) =>
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 638m,
            BalanceSheetTotal = 17_430m,
            AvgEmployees = 0
        });

    private static async Task ClassifyWithFiguresAsync(
        AccountsDbContext db,
        AccountingPeriod period,
        DateOnly? election = null)
    {
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 638m,
            BalanceSheetTotal = 17_430m,
            AvgEmployees = 0,
            ThresholdElectionEffectiveFrom = election
        });
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(period.CompanyId, period.Id);
    }

    private static async Task ChangeRetainedTurnoverAsync(AccountsDbContext db, AccountingPeriod period)
    {
        var row = await db.SizeClassifications.SingleAsync(s => s.PeriodId == period.Id);
        row.Turnover += 1m;
        await db.SaveChangesAsync();
    }
}
