using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Data;
using Accounts.Api.Entities;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

public class ImportService(AccountsDbContext db)
{
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

    public async Task<ImportResult> ImportCsvAsync(int bankAccountId, int periodId, Stream csvStream, string filename, ColumnMapping? mapping = null)
    {
        var bankAccount = await db.BankAccounts.FindAsync(bankAccountId)
            ?? throw new InvalidOperationException("Bank account not found");

        // Read all lines
        using var reader = new StreamReader(csvStream);
        var content = await reader.ReadToEndAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
            return new ImportResult(0, 0, 0, 0, ["File is empty or has no data rows"]);

        // Auto-detect format if no mapping provided
        mapping ??= DetectFormat(lines[0]).Mapping;

        var batch = new ImportBatch
        {
            BankAccountId = bankAccountId,
            Filename = filename,
            RowCount = lines.Length - 1 // exclude header
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
            .OrderBy(r => r.Priority)
            .ToListAsync();

        var warnings = new List<string>();
        var transactions = new List<ImportedTransaction>();
        int duplicates = 0;
        int autoCategorised = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            try
            {
                var fields = ParseCsvLine(line);

                if (fields.Length <= mapping.DescriptionColumn || fields.Length <= mapping.AmountColumn)
                {
                    warnings.Add($"Row {i}: insufficient columns");
                    continue;
                }

                var dateStr = fields[mapping.DateColumn].Trim().Trim('"');
                var description = fields[mapping.DescriptionColumn].Trim().Trim('"');
                var amountStr = fields[mapping.AmountColumn].Trim().Trim('"');

                if (!DateOnly.TryParseExact(dateStr, mapping.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                    !DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out date))
                {
                    warnings.Add($"Row {i}: could not parse date '{dateStr}'");
                    continue;
                }

                if (!decimal.TryParse(amountStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    warnings.Add($"Row {i}: could not parse amount '{amountStr}'");
                    continue;
                }

                decimal? balance = null;
                if (mapping.BalanceColumn.HasValue && mapping.BalanceColumn >= 0 && fields.Length > mapping.BalanceColumn)
                {
                    var balStr = fields[mapping.BalanceColumn.Value].Trim().Trim('"');
                    if (decimal.TryParse(balStr.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
                        balance = bal;
                }

                string? reference = null;
                if (mapping.ReferenceColumn.HasValue && mapping.ReferenceColumn >= 0 && fields.Length > mapping.ReferenceColumn)
                    reference = fields[mapping.ReferenceColumn.Value].Trim().Trim('"');

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
            catch (Exception ex)
            {
                warnings.Add($"Row {i}: {ex.Message}");
            }
        }

        db.ImportedTransactions.AddRange(transactions);
        batch.MatchedCount = autoCategorised;
        batch.RowCount = transactions.Count + duplicates;
        await db.SaveChangesAsync();

        return new ImportResult(transactions.Count + duplicates, transactions.Count, duplicates, autoCategorised, warnings);
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

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); continue; }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
