using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public class ImportService(AccountsDbContext db, IOptions<ImportLimitConfig>? importLimits = null)
{
    private readonly ImportLimitConfig _limits = importLimits?.Value ?? new ImportLimitConfig();

    public record ImportResult(int TotalRows, int ImportedRows, int DuplicatesSkipped, int AutoCategorised, List<string> Warnings);

    public record ColumnMapping(int DateColumn, int DescriptionColumn, int AmountColumn, int? BalanceColumn, int? ReferenceColumn, string DateFormat = "dd/MM/yyyy");

    // Auto-detect bank format from CSV headers
    public record BankFormat(string Name, ColumnMapping Mapping);

    private static readonly List<BankFormat> KnownFormats =
    [
        new("AIB", new(0, 1, 3, 4, 2, "dd/MM/yyyy")),
        new("BOI", new(0, 1, 2, 3, -1, "dd/MM/yyyy")),
        new("Revolut", new(0, 1, 2, 3, -1, "yyyy-MM-dd")),
        new("Stripe", new(0, 4, 2, -1, 1, "yyyy-MM-dd")),
        new("Generic", new(0, 1, 2, -1, -1, "dd/MM/yyyy")),
    ];

    public async Task<ImportResult> ImportCsvAsync(int companyId, int bankAccountId, int periodId, Stream csvStream, string filename, ColumnMapping? mapping = null)
    {
        if (csvStream.CanSeek && csvStream.Length > _limits.MaxCsvBytes)
            throw new BusinessRuleException($"CSV file is too large. Maximum allowed size is {_limits.MaxCsvBytes / 1024 / 1024} MB.");

        var bankAccount = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == bankAccountId && b.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Bank account {bankAccountId} not found");
        var period = await db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        string content;
        try
        {
            using var reader = new StreamReader(csvStream);
            content = await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            throw new BusinessRuleException("CSV file could not be read. Upload a valid CSV bank statement.");
        }

        if (Encoding.UTF8.GetByteCount(content) > _limits.MaxCsvBytes)
            throw new BusinessRuleException($"CSV file is too large. Maximum allowed size is {_limits.MaxCsvBytes / 1024 / 1024} MB.");

        CsvImportRows csvRows;
        try
        {
            csvRows = await ReadCsvRowsAsync(content);
        }
        catch (CsvHelperException)
        {
            throw new BusinessRuleException("CSV file could not be parsed. Upload a valid CSV bank statement.");
        }

        if (csvRows.Header.Length == 0 || csvRows.Rows.Count == 0)
            return new ImportResult(0, 0, 0, 0, ["File is empty or has no data rows"]);

        if (csvRows.Rows.Count > _limits.MaxRows)
            throw new BusinessRuleException($"CSV has too many rows. Maximum allowed data rows is {_limits.MaxRows:N0}.");

        // Auto-detect format if no mapping provided
        mapping ??= DetectFormat(string.Join(",", csvRows.Header)).Mapping;

        var batch = new ImportBatch
        {
            BankAccountId = bankAccountId,
            Filename = filename,
            RowCount = csvRows.Rows.Count
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();

        // Get existing transactions for duplicate detection
        var existingHashes = await db.ImportedTransactions
            .Where(t => t.BankAccountId == bankAccountId)
            .Select(t => new { t.Date, t.Amount, t.Description })
            .ToListAsync();

        var existingHashSet = existingHashes
            .Select(t => ComputeHash(t.Date, t.Amount, t.Description))
            .ToHashSet();

        // Get transaction rules for auto-categorisation
        var rules = await db.TransactionRules
            .Where(r => r.CompanyId == bankAccount.CompanyId)
            .Where(r => r.Category.CompanyId == bankAccount.CompanyId || (r.Category.IsSystem && r.Category.CompanyId == null))
            .OrderBy(r => r.Priority)
            .ToListAsync();

        var warnings = new List<string>();
        var transactions = new List<ImportedTransaction>();
        int duplicates = 0;
        int autoCategorised = 0;

        foreach (var row in csvRows.Rows)
        {
            try
            {
                var fields = row.Fields;

                if (fields.Length <= mapping.DescriptionColumn || fields.Length <= mapping.AmountColumn)
                {
                    warnings.Add($"Row {row.RowNumber}: insufficient columns");
                    continue;
                }

                var dateStr = CleanCsvField(fields[mapping.DateColumn]);
                var description = CleanCsvField(fields[mapping.DescriptionColumn]);
                var amountStr = CleanCsvField(fields[mapping.AmountColumn]);

                if (!DateOnly.TryParseExact(dateStr, mapping.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                    !DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out date))
                {
                    warnings.Add($"Row {row.RowNumber}: could not parse date");
                    continue;
                }

                if (date < period.PeriodStart || date > period.PeriodEnd)
                {
                    warnings.Add($"Row {row.RowNumber}: transaction date {date:yyyy-MM-dd} is outside accounting period {period.PeriodStart:yyyy-MM-dd} to {period.PeriodEnd:yyyy-MM-dd}");
                    continue;
                }

                if (!decimal.TryParse(amountStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    warnings.Add($"Row {row.RowNumber}: could not parse amount");
                    continue;
                }

                decimal? balance = null;
                if (mapping.BalanceColumn.HasValue && mapping.BalanceColumn >= 0 && fields.Length > mapping.BalanceColumn)
                {
                    var balStr = CleanCsvField(fields[mapping.BalanceColumn.Value]);
                    if (decimal.TryParse(balStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
                        balance = bal;
                }

                string? reference = null;
                if (mapping.ReferenceColumn.HasValue && mapping.ReferenceColumn >= 0 && fields.Length > mapping.ReferenceColumn)
                    reference = CleanCsvField(fields[mapping.ReferenceColumn.Value]);

                // Duplicate detection
                var hash = ComputeHash(date, amount, description);
                bool isDuplicate = existingHashSet.Contains(hash);
                if (isDuplicate) { duplicates++; continue; }
                existingHashSet.Add(hash);

                // Auto-categorise
                int? categoryId = null;
                decimal? confidence = null;
                foreach (var rule in rules)
                {
                    if (description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        categoryId = rule.CategoryId;
                        confidence = 0.85m;
                        autoCategorised++;
                        break;
                    }
                }

                transactions.Add(new ImportedTransaction
                {
                    BankAccountId = bankAccountId,
                    PeriodId = periodId,
                    ImportBatchId = batch.Id,
                    Date = date,
                    Description = description,
                    Amount = amount,
                    Balance = balance,
                    Reference = reference,
                    CategoryId = categoryId,
                    ConfidenceScore = confidence,
                    IsDuplicate = false,
                    ManualOverride = false
                });
            }
            catch
            {
                warnings.Add($"Row {row.RowNumber}: could not import row");
            }
        }

        db.ImportedTransactions.AddRange(transactions);
        batch.MatchedCount = autoCategorised;
        await db.SaveChangesAsync();

        return new ImportResult(csvRows.Rows.Count, transactions.Count, duplicates, autoCategorised, warnings);
    }

    public BankFormat DetectFormat(string headerLine)
    {
        var lower = headerLine.ToLower();
        if (lower.Contains("posted account") || lower.Contains("aib")) return KnownFormats[0]; // AIB
        if (lower.Contains("bank of ireland") || lower.Contains("boi")) return KnownFormats[1]; // BOI
        if (lower.Contains("started date") || lower.Contains("revolut")) return KnownFormats[2]; // Revolut
        if (lower.Contains("balance_transaction") || lower.Contains("stripe")) return KnownFormats[3]; // Stripe
        return KnownFormats[4]; // Generic
    }

    private static string ComputeHash(DateOnly date, decimal amount, string description)
    {
        var input = $"{date:yyyy-MM-dd}|{amount:F2}|{description.Trim().ToLower()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..16];
    }

    private static async Task<CsvImportRows> ReadCsvRowsAsync(string content)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            ExceptionMessagesContainRawData = false,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim
        };

        using var textReader = new StringReader(content);
        using var parser = new CsvParser(textReader, config, false);
        if (!await parser.ReadAsync())
            return new CsvImportRows([], []);

        var header = parser.Record?.ToArray() ?? [];
        var rows = new List<CsvImportRow>();
        var rowNumber = 0;
        while (await parser.ReadAsync())
        {
            var fields = parser.Record?.ToArray() ?? [];
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            rowNumber++;
            rows.Add(new CsvImportRow(rowNumber, fields));
        }

        return new CsvImportRows(header, rows);
    }

    private static string CleanCsvField(string? value) => value?.Trim() ?? string.Empty;

    private sealed record CsvImportRows(string[] Header, List<CsvImportRow> Rows);

    private sealed record CsvImportRow(int RowNumber, string[] Fields);
}
