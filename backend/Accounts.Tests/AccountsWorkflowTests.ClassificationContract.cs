using System.Text.Json;
using Accounts.Api.Data;
using Accounts.Api.Endpoints;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

// Live defect 2026-10-10 (FY2026 workspace would not render): saving classification inputs and
// running the engine were two separately committed requests. When the engine rejected the second,
// the first left a row whose decision columns held CLR defaults (0 years, "Micro"), which the
// period response then presented as a decision and the frontend contract rejected on every load.
public partial class AccountsWorkflowTests
{
    [Fact]
    public async Task ClassifyWithInputs_RejectedSubsequentYear_LeavesNoInputsOnlyRow()
    {
        await using var db = CreateDbContext();
        var (_, current) = await SeedLiveCharityShapedPeriodsAsync(db);

        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => ClassificationEndpoints.ClassifySizeEndpointAsync(
            current.CompanyId,
            current.Id,
            new SizeClassificationInput(638m, 17_430m, 0, null, new DateOnly(2024, 1, 1)),
            ClassificationService(db),
            db,
            new AuditService(db),
            ClassificationRequest(current),
            DisabledApiAccess()));

        Assert.Contains("subsequent-year classification requires", error.Message);
        Assert.Empty(await db.SizeClassifications.AsNoTracking().ToListAsync());
        Assert.Empty(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationDataSaved).ToListAsync());
    }

    [Fact]
    public async Task ClassifyWithInputs_PersistsInputsAndDecisionTogether()
    {
        await using var db = CreateDbContext();
        var (first, _) = await SeedLiveCharityShapedPeriodsAsync(db);

        var result = await ClassificationEndpoints.ClassifySizeEndpointAsync(
            first.CompanyId,
            first.Id,
            new SizeClassificationInput(638m, 17_430m, 0, null, new DateOnly(2024, 1, 1)),
            ClassificationService(db),
            db,
            new AuditService(db),
            ClassificationRequest(first),
            DisabledApiAccess());

        Assert.Equal(StatusCodes.Status200OK, ResultStatusCode(result));
        var saved = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id);
        Assert.Equal(0, saved.AvgEmployees);
        Assert.Equal(638m, saved.Turnover);
        Assert.True(saved.HasCurrentDecision);
        Assert.Equal(CompanySizeClass.Micro, saved.CalculatedClass);
        Assert.True(saved.PeriodLengthInYears > 0);
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationDataSaved).ToListAsync());
        Assert.Single(await db.AuditLogs.Where(a => a.Action == AuditEventCodes.SizeClassificationRun).ToListAsync());
    }

    [Fact]
    public async Task InputsOnlyClassification_SerializesDecisionAsAbsentNotDefaults()
    {
        await using var db = CreateDbContext();
        var (_, current) = await SeedLiveCharityShapedPeriodsAsync(db);

        await ClassificationEndpoints.SaveSizeClassificationEndpointAsync(
            current.CompanyId,
            current.Id,
            new SizeClassificationInput(1_640m, 22_358m, 0, null, new DateOnly(2024, 1, 1)),
            db,
            new AuditService(db),
            ClassificationRequest(current, HttpMethods.Put, "size-classification"),
            DisabledApiAccess());

        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == current.Id);
        Assert.Equal(0m, row.PeriodLengthInYears);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, ApiJson));
        var body = json.RootElement;
        // The API omits null members, and the frontend contract treats an absent decision as
        // "not yet classified" rather than as a 0-year Micro decision.
        Assert.False(body.TryGetProperty("periodLengthInYears", out _));
        Assert.False(body.TryGetProperty("calculatedClass", out _));
        Assert.False(body.TryGetProperty("annualisedTurnover", out _));
        Assert.False(body.TryGetProperty("rawCurrentClass", out _));
        Assert.False(body.TryGetProperty("rawCurrentMicroQualified", out _));
        Assert.Equal(0, body.GetProperty("avgEmployees").GetInt32());
        Assert.Equal(1_640m, body.GetProperty("turnover").GetDecimal());
        Assert.False(body.TryGetProperty("periodLengthInYearsDecision", out _));
        Assert.False(body.TryGetProperty("hasCurrentDecision", out _));
    }

    [Fact]
    public async Task ClassifiedRow_SerializesTheRecordedDecision()
    {
        await using var db = CreateDbContext();
        var (first, _) = await SeedLiveCharityShapedPeriodsAsync(db);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = first.Id,
            Turnover = 638m,
            BalanceSheetTotal = 17_430m,
            AvgEmployees = 0,
            ThresholdElectionEffectiveFrom = new DateOnly(2024, 1, 1)
        });
        await db.SaveChangesAsync();
        await ClassificationService(db).ClassifyAsync(first.CompanyId, first.Id);

        var row = await db.SizeClassifications.AsNoTracking().SingleAsync(s => s.PeriodId == first.Id);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row, ApiJson));
        var body = json.RootElement;
        Assert.Equal(decimal.Divide(529, 365.2425m), body.GetProperty("periodLengthInYears").GetDecimal());
        Assert.Equal("Micro", body.GetProperty("calculatedClass").GetString());
        Assert.Equal("Micro", body.GetProperty("rawCurrentClass").GetString());
        Assert.True(body.GetProperty("rawCurrentMicroQualified").GetBoolean());
    }

    [Theory]
    [InlineData("2025-08-01", "2026-07-31")]
    [InlineData("2017-07-20", "2018-12-30")]
    [InlineData("2018-12-31", "2019-07-31")]
    public async Task PeriodLength_IsPositiveForTheLiveCharityPeriodShapes(string startValue, string endValue)
    {
        await using var db = CreateDbContext();
        var period = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        period.PeriodStart = DateOnly.Parse(startValue);
        period.PeriodEnd = DateOnly.Parse(endValue);
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            Turnover = 638m,
            BalanceSheetTotal = 17_430m,
            AvgEmployees = 0
        });
        await db.SaveChangesAsync();

        var result = await ClassificationService(db).ClassifyAsync(period.CompanyId, period.Id);

        var start = DateOnly.Parse(startValue);
        var end = DateOnly.Parse(endValue);
        var expected = end.AddDays(1) == start.AddYears(1)
            ? 1m
            : decimal.Divide(end.DayNumber - start.DayNumber + 1, 365.2425m);
        Assert.Equal(expected, result.PeriodLengthInYears);
        Assert.InRange(result.PeriodLengthInYears, 0.0001m, 2m);
    }

    private static SizeClassificationService ClassificationService(AccountsDbContext db) =>
        new(db, Options.Create(new SizeThresholdConfig()), new AuditService(db));

    private static HttpContext ClassificationRequest(
        AccountingPeriod period,
        string method = "POST",
        string action = "classify") =>
        AuthenticatedRequest(
            "Owner",
            method,
            $"/api/companies/{period.CompanyId}/periods/{period.Id}/{action}");

    /// <summary>
    /// A CLG first period 20 Jul 2017 - 30 Dec 2018 and an unclassified FY2026 subsequent period,
    /// the shape of the live record when the defect occurred.
    /// </summary>
    private static async Task<(AccountingPeriod First, AccountingPeriod Current)> SeedLiveCharityShapedPeriodsAsync(
        AccountsDbContext db)
    {
        var first = await SeedCompanyPeriodAsync(db, isFirstYear: true);
        first.PeriodStart = new DateOnly(2017, 7, 20);
        first.PeriodEnd = new DateOnly(2018, 12, 30);
        var current = new AccountingPeriod
        {
            CompanyId = first.CompanyId,
            PeriodStart = new DateOnly(2025, 8, 1),
            PeriodEnd = new DateOnly(2026, 7, 31),
            IsFirstYear = false
        };
        db.AccountingPeriods.Add(current);
        await db.SaveChangesAsync();
        return (first, current);
    }
}
