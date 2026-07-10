using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Services;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Xunit;

namespace Accounts.Tests;

public sealed class StatutoryNoteChecklistTests
{
    public StatutoryNoteChecklistTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Theory]
    [InlineData(ElectedRegime.Micro)]
    [InlineData(ElectedRegime.SmallAbridged)]
    [InlineData(ElectedRegime.Small)]
    [InlineData(ElectedRegime.Medium)]
    [InlineData(ElectedRegime.Full)]
    public async Task EveryRegime_DeletingOneRequiredCodeFailsChecklist(ElectedRegime regime)
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, regime, includeManualReviews: true);
        var service = new NotesDisclosureService(db);
        var notes = await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);

        Assert.Empty(await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.All(notes.Where(note => note.IsRequired), note => Assert.True(StatutoryNoteCodes.IsStableCode(note.Code)));
        Assert.All(
            notes.Where(note => note.IsIncluded && note.IsRequired).GroupBy(note => note.Code),
            group => Assert.Single(group));

        var deleted = notes.First(note => note.Code == StatutoryNoteCodes.AccountingPolicies);
        db.NotesDisclosures.Remove(deleted);
        await db.SaveChangesAsync();

        var issues = await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains(issues, issue => issue.Contains(StatutoryNoteCodes.AccountingPolicies, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MutationMatrix_RejectsDuplicateTamperedAndUnreviewedUnsupportedNotes()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, ElectedRegime.Medium, includeManualReviews: true);
        var service = new NotesDisclosureService(db);
        var notes = await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Empty(await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id));

        var reviewedNil = notes.Single(note => note.Code == StatutoryNoteCodes.Debtors);
        reviewedNil.ReviewEvidence = null;
        await db.SaveChangesAsync();
        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("lacks retained review evidence", StringComparison.OrdinalIgnoreCase));
        notes = await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);

        var policies = notes.Single(note => note.Code == StatutoryNoteCodes.AccountingPolicies);
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = fixture.Period.Id,
            NoteNumber = notes.Max(note => note.NoteNumber) + 1,
            Title = policies.Title,
            Content = policies.Content,
            IsRequired = false,
            IsIncluded = true,
            ChecklistState = NoteChecklistState.Required
        });
        await db.SaveChangesAsync();
        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("duplicated", StringComparison.OrdinalIgnoreCase));

        db.NotesDisclosures.Remove(db.NotesDisclosures.Local.Last());
        policies.Content += "\nUnreconciled mutation.";
        await db.SaveChangesAsync();
        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("no longer reconciles", StringComparison.OrdinalIgnoreCase));

        policies.Content = (await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id))
            .Single(note => note.Code == StatutoryNoteCodes.AccountingPolicies)
            .Content;
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = fixture.Period.Id,
            NoteNumber = (await db.NotesDisclosures.MaxAsync(note => note.NoteNumber)) + 1,
            Title = "Unsupported custom assertion",
            Content = "There were none and the company had no derivative financial instruments or capital commitments.",
            IsRequired = false,
            IsIncluded = true
        });
        await db.SaveChangesAsync();
        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("unsupported representation", StringComparison.OrdinalIgnoreCase));

        await using var unresolvedDb = CreateDb();
        var unresolved = await SeedAsync(unresolvedDb, ElectedRegime.Medium, includeManualReviews: false);
        var unresolvedService = new NotesDisclosureService(unresolvedDb);
        var unresolvedNotes = await unresolvedService.GenerateNotesAsync(unresolved.Company.Id, unresolved.Period.Id);
        Assert.DoesNotContain(
            unresolvedNotes.Where(note => note.IsIncluded),
            note => note.Code is StatutoryNoteCodes.FinancialInstruments or StatutoryNoteCodes.CapitalCommitments);
        var unresolvedIssues = await unresolvedService.GetChecklistIssuesAsync(unresolved.Company.Id, unresolved.Period.Id);
        Assert.Contains(unresolvedIssues, issue => issue.Contains(StatutoryNoteCodes.FinancialInstruments, StringComparison.Ordinal));
        Assert.Contains(unresolvedIssues, issue => issue.Contains(StatutoryNoteCodes.CapitalCommitments, StringComparison.Ordinal));
        Assert.Contains(unresolvedIssues, issue => issue.Contains(StatutoryNoteCodes.DeferredTax, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(StatutoryNoteCodes.AccountingPolicies)]
    [InlineData(StatutoryNoteCodes.FixedAssets)]
    [InlineData(StatutoryNoteCodes.Approval)]
    public async Task DuplicateGeneratedPresentationTitles_FailValidation(string code)
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, ElectedRegime.Small, includeManualReviews: true);
        var bank = await db.BankAccounts.SingleAsync(account => account.CompanyId == fixture.Company.Id);
        var assetCategory = await db.AccountCategories.SingleAsync(category =>
            category.CompanyId == fixture.Company.Id && category.Code == "0050");
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = fixture.Period.Id,
            Date = new DateOnly(2026, 4, 1),
            Description = "Computer equipment purchase",
            Amount = -40m,
            CategoryId = assetCategory.Id
        });
        await db.SaveChangesAsync();

        var service = new NotesDisclosureService(db);
        var notes = await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        var target = notes.Single(note => note.Code == code);
        Assert.True(target.IsIncluded);
        db.NotesDisclosures.Add(new NotesDisclosure
        {
            PeriodId = fixture.Period.Id,
            NoteNumber = notes.Max(note => note.NoteNumber) + 1,
            Title = target.Title,
            Content = "Duplicate presentation note",
            IsRequired = false,
            IsIncluded = true
        });
        await db.SaveChangesAsync();

        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TaxAndCreditorNotes_UseStatementFiguresWithoutDoubleCountingTaxBalance()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, ElectedRegime.Small, includeManualReviews: true);
        var categories = await db.AccountCategories.Where(category => category.CompanyId == fixture.Company.Id).ToListAsync();
        var taxExpense = categories.Single(category => category.Code == "8000");
        var taxCreditor = categories.Single(category => category.Code == "2400");
        db.Adjustments.Add(new Adjustment
        {
            PeriodId = fixture.Period.Id,
            Description = "Current corporation tax",
            DebitCategoryId = taxExpense.Id,
            CreditCategoryId = taxCreditor.Id,
            Amount = 50m,
            IsAuto = true,
            Source = AdjustmentSource.Auto,
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = "Reviewer"
        });
        db.TaxBalances.Add(new TaxBalance
        {
            PeriodId = fixture.Period.Id,
            TaxType = TaxType.CorporationTax,
            Liability = 50m,
            Balance = 50m
        });
        await db.SaveChangesAsync();

        var notes = await new NotesDisclosureService(db).GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        var creditor = notes.Single(note => note.Code == StatutoryNoteCodes.CurrentCreditors);
        var tax = notes.Single(note => note.Code == StatutoryNoteCodes.TaxOnProfit);

        Assert.Contains("€50.00", creditor.Content);
        Assert.DoesNotContain("€100.00", creditor.Content);
        Assert.Contains("€50.00", tax.Content);
    }

    [Fact]
    public async Task RenderedPackage_EmitsEachGeneratedNoteOnceAndKeepsApprovalDateStable()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, ElectedRegime.Micro, includeManualReviews: true);
        var notesService = new NotesDisclosureService(db);
        var first = await notesService.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        var approval = first.Single(note => note.Code == StatutoryNoteCodes.Approval).Content;

        var second = await notesService.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Equal(approval, second.Single(note => note.Code == StatutoryNoteCodes.Approval).Content);
        Assert.Empty(await notesService.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id));

        var documents = new DocumentGeneratorService(db, new FinancialStatementsService(db));
        var pdfText = ExtractPdfText(await documents.GenerateAccountsPackageAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.Equal(1, Occurrences(pdfText, "ACCOUNTING POLICIES"));
        Assert.Equal(1, Occurrences(pdfText, "APPROVAL OF FINANCIAL STATEMENTS"));
        Assert.Contains("15 June 2026", pdfText);
        Assert.DoesNotContain("Deferred tax", pdfText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("derivative", pdfText, StringComparison.OrdinalIgnoreCase);

        var required = await db.NotesDisclosures.SingleAsync(note => note.Code == StatutoryNoteCodes.AccountingPolicies);
        db.NotesDisclosures.Remove(required);
        await db.SaveChangesAsync();
        var blocked = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            documents.GenerateAccountsPackageAsync(fixture.Company.Id, fixture.Period.Id));
        Assert.Contains(StatutoryNoteCodes.AccountingPolicies, blocked.Message);
    }

    [Fact]
    public async Task FinalisationRefresh_UsesThePersistedApprovalDateBeforeTheTransactionCommits()
    {
        await using var db = CreateDb();
        var fixture = await SeedAsync(db, ElectedRegime.Micro, includeManualReviews: true);
        fixture.Period.ApprovalDate = null;
        await db.SaveChangesAsync();
        var service = new NotesDisclosureService(db);
        await service.GenerateNotesAsync(fixture.Company.Id, fixture.Period.Id);
        Assert.Contains(
            await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id),
            issue => issue.Contains("approval", StringComparison.OrdinalIgnoreCase));

        fixture.Period.ApprovalDate = new DateOnly(2026, 6, 15);
        await NotesDisclosureService.RefreshApprovalNoteAsync(db, fixture.Period);

        Assert.Empty(await service.GetChecklistIssuesAsync(fixture.Company.Id, fixture.Period.Id));
        var approval = await db.NotesDisclosures.SingleAsync(note => note.Code == StatutoryNoteCodes.Approval);
        Assert.Contains("15 June 2026", approval.Content);
    }

    private static async Task<Fixture> SeedAsync(
        AccountsDbContext db,
        ElectedRegime regime,
        bool includeManualReviews)
    {
        var tenant = new Tenant { Name = "Notes Test Firm", Slug = $"notes-{Guid.NewGuid():N}" };
        var company = new Company
        {
            Tenant = tenant,
            LegalName = "Stable Notes Limited",
            CroNumber = "778899",
            CompanyType = CompanyType.Private,
            IncorporationDate = new DateOnly(2026, 1, 1),
            AnnualReturnDate = new DateOnly(2024, 12, 15),
            IsTrading = true,
            RegisteredOfficeAddress1 = "1 Review Street",
            RegisteredOfficeCity = "Dublin",
            RegisteredOfficeCounty = "Dublin"
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var period = new AccountingPeriod
        {
            CompanyId = company.Id,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = new DateOnly(2026, 12, 31),
            IsFirstYear = true,
            GoingConcernConfirmed = true,
            ApprovalDate = new DateOnly(2026, 6, 15)
        };
        db.AccountingPeriods.Add(period);
        await db.SaveChangesAsync();

        var size = regime switch
        {
            ElectedRegime.Micro => CompanySizeClass.Micro,
            ElectedRegime.Small or ElectedRegime.SmallAbridged => CompanySizeClass.Small,
            ElectedRegime.Medium => CompanySizeClass.Medium,
            _ => CompanySizeClass.Large
        };
        db.SizeClassifications.Add(new SizeClassification
        {
            PeriodId = period.Id,
            CalculatedClass = size,
            Turnover = 100m,
            BalanceSheetTotal = 200m,
            AvgEmployees = 0
        });
        db.FilingRegimes.Add(new FilingRegime
        {
            PeriodId = period.Id,
            ElectedRegime = regime,
            CanUseMicro = regime == ElectedRegime.Micro,
            CanFileAbridged = regime is ElectedRegime.Micro or ElectedRegime.SmallAbridged,
            AuditExempt = true
        });

        var categories = await new CategoryService(db).SeedDefaultCategoriesAsync(company.Id);
        int Category(string code) => categories.Single(category => category.Code == code).Id;
        var bank = new BankAccount
        {
            CompanyId = company.Id,
            Name = "Current account",
            OpeningBalance = 100m,
            OpeningBalanceDate = period.PeriodStart
        };
        db.BankAccounts.Add(bank);
        db.OpeningBalances.Add(new OpeningBalance
        {
            PeriodId = period.Id,
            AccountCategoryId = Category("3000"),
            Credit = 100m,
            SourceNote = "Issued share capital",
            EnteredBy = "Reviewer",
            Reviewed = true,
            ReviewedBy = "Reviewer",
            ReviewedAt = DateTime.UtcNow
        });
        db.ShareCapitals.Add(new ShareCapital
        {
            CompanyId = company.Id,
            ShareClass = "Ordinary",
            NumberIssued = 100,
            NominalValue = 1m,
            TotalValue = 100m,
            IssueDate = period.PeriodStart
        });
        await db.SaveChangesAsync();
        db.ImportedTransactions.Add(new ImportedTransaction
        {
            BankAccountId = bank.Id,
            PeriodId = period.Id,
            Date = new DateOnly(2026, 3, 1),
            Description = "Customer receipt",
            Amount = 100m,
            CategoryId = Category("4000")
        });

        foreach (var key in new[]
                 {
                     "adjustments", "debtors", "creditors", "fixed-assets", "inventory", "loans",
                     "director-loans", "payroll", "tax", "dividends", "post-balance-sheet-events",
                     "related-parties", "contingent-liabilities", "going-concern"
                 })
        {
            db.YearEndReviewConfirmations.Add(Review(period.Id, key, "Nil/source position reviewed."));
        }
        if (includeManualReviews)
        {
            db.YearEndReviewConfirmations.Add(Review(
                period.Id,
                "note-directors-remuneration",
                "Directors' remuneration was reviewed against the retained payroll and board records; the disclosure amount is €0.00."));
            db.YearEndReviewConfirmations.Add(Review(
                period.Id,
                "note-financial-instruments",
                "The financial instruments disclosure was prepared manually from the retained instrument register and approved for these accounts."));
            db.YearEndReviewConfirmations.Add(Review(
                period.Id,
                "note-capital-commitments",
                "Capital commitments of €0.00 were confirmed from the retained board minutes and supplier-contract review."));
            db.YearEndReviewConfirmations.Add(Review(
                period.Id,
                "note-deferred-tax",
                "The deferred tax disclosure was prepared manually from retained timing-difference schedules and approved for these accounts."));
        }
        await db.SaveChangesAsync();
        return new Fixture(company, period);
    }

    private static YearEndReviewConfirmation Review(int periodId, string key, string note) => new()
    {
        PeriodId = periodId,
        SectionKey = key,
        Confirmed = true,
        ConfirmedBy = "Qualified accountant",
        ConfirmedAt = new DateTime(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc),
        Note = note
    };

    private static AccountsDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountsDbContext>()
            .UseInMemoryDatabase($"statutory-notes-{Guid.NewGuid():N}")
            .Options);

    private static string ExtractPdfText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
            builder.Append(' ').Append(string.Join(' ', page.GetWords().Select(word => word.Text)));
        return builder.ToString();
    }

    private static int Occurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }
        return count;
    }

    private sealed record Fixture(Company Company, AccountingPeriod Period);
}
