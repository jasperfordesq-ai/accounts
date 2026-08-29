using System.Globalization;
using System.Text;
using System.Text.Json;
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

    public record ImportResult(
        int TotalRows,
        int ImportedRows,
        int DuplicateCandidates,
        int AutoCategorised,
        List<string> Warnings,
        int? ImportBatchId,
        string SourceFilename,
        string SourceFileSha256,
        long SourceFileBytes);

    /// <summary>
    /// Maps CSV columns to transaction fields. For AIB Internet Banking exports use
    /// <see cref="DebitColumn"/>/<see cref="CreditColumn"/> instead of <see cref="AmountColumn"/>
    /// (set AmountColumn to -1), and <see cref="DescriptionColumns"/> instead of
    /// <see cref="DescriptionColumn"/> (set DescriptionColumn to -1).
    /// </summary>
    public record ColumnMapping(
        int DateColumn,
        int DescriptionColumn,
        int AmountColumn,
        int? BalanceColumn,
        int? ReferenceColumn,
        string DateFormat = "dd/MM/yyyy",
        int? DebitColumn = null,
        int? CreditColumn = null,
        int[]? DescriptionColumns = null);

    // Auto-detect bank format from CSV headers
    public record BankFormat(string Name, ColumnMapping Mapping);

    private static readonly List<BankFormat> KnownFormats =
    [
        // AIB Internet Banking 12-column export (Posted Account / Posted Transactions Date /
        // Description1-3 / Debit Amount / Credit Amount / Balance / …).
        // Detected before the generic AIB check; both formats must coexist.
        new("AIBInternetBanking", new(-1, -1, -1, 7, 0, "dd/MM/yyyy", 5, 6, [2, 3, 4])),
        new("AIB", new(0, 1, 3, 4, 2, "dd/MM/yyyy")),
        new("BOI", new(0, 1, 2, 3, -1, "dd/MM/yyyy")),
        new("Revolut", new(0, 1, 2, 3, -1, "yyyy-MM-dd")),
        new("Stripe", new(0, 4, 2, -1, 1, "yyyy-MM-dd")),
        new("Generic", new(0, 1, 2, -1, -1, "dd/MM/yyyy")),
    ];

    public async Task<ImportResult> ImportCsvAsync(
        int companyId,
        int bankAccountId,
        int periodId,
        Stream csvStream,
        string filename,
        ColumnMapping? mapping = null,
        CancellationToken cancellationToken = default)
    {
        if (csvStream.CanSeek && csvStream.Length > _limits.MaxCsvBytes)
            throw new BusinessRuleException($"CSV file is too large. Maximum allowed size is {_limits.MaxCsvBytes / 1024 / 1024} MB.");

        var bankAccount = await db.BankAccounts
            .FirstOrDefaultAsync(b => b.Id == bankAccountId && b.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Bank account {bankAccountId} not found");
        var period = await db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, cancellationToken)
            ?? throw new ResourceNotFoundException($"Period {periodId} not found");

        byte[] sourceBytes;
        try
        {
            using var buffer = new MemoryStream();
            await csvStream.CopyToAsync(buffer, cancellationToken);
            sourceBytes = buffer.ToArray();
        }
        catch (IOException)
        {
            throw new BusinessRuleException("CSV file could not be read. Upload a valid CSV bank statement.");
        }

        if (sourceBytes.LongLength > _limits.MaxCsvBytes)
            throw new BusinessRuleException($"CSV file is too large. Maximum allowed size is {_limits.MaxCsvBytes / 1024 / 1024} MB.");

        string content;
        using (var reader = new StreamReader(new MemoryStream(sourceBytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync(cancellationToken);
        var sourceFilename = NormalizeFilename(filename);
        var sourceFileSha256 = DuplicateCandidateDetector.ComputeSourceFileSha256(sourceBytes);

        CsvImportRows csvRows;
        try
        {
            csvRows = await ReadCsvRowsAsync(content, cancellationToken);
        }
        catch (CsvHelperException)
        {
            throw new BusinessRuleException("CSV file could not be parsed. Upload a valid CSV bank statement.");
        }

        if (csvRows.Header.Length == 0)
            return new ImportResult(
                0, 0, 0, 0, ["File is empty or has no data rows"], null,
                sourceFilename, sourceFileSha256, sourceBytes.LongLength);

        // Resolve and validate the header before checking for data rows. A one-row headerless file
        // must be rejected explicitly rather than silently treating its only transaction as a header.
        var mappingWasSupplied = mapping is not null;
        var detectedFormat = DetectFormat(string.Join(",", csvRows.Header));
        mapping ??= detectedFormat.Mapping;
        ValidateMapping(mapping, csvRows.Header.Length);
        if (LooksLikeHeaderlessData(csvRows.Header))
            throw new BusinessRuleException("CSV appears to be headerless. Add a header row so the first transaction is never silently consumed.");
        if (!mappingWasSupplied && detectedFormat.Name == "Generic" && !HasRecognizedGenericHeader(csvRows.Header, mapping))
            throw new BusinessRuleException("CSV header is not recognised. Use Date, Description and Amount headers or provide an explicit reviewed column mapping.");

        if (csvRows.Rows.Count == 0)
            return new ImportResult(
                0, 0, 0, 0, ["File has a header but no data rows"], null,
                sourceFilename, sourceFileSha256, sourceBytes.LongLength);

        if (csvRows.Rows.Count > _limits.MaxRows)
            throw new BusinessRuleException($"CSV has too many rows. Maximum allowed data rows is {_limits.MaxRows:N0}.");

        var batch = new ImportBatch
        {
            BankAccountId = bankAccountId,
            Filename = sourceFilename,
            RowCount = csvRows.Rows.Count,
            SourceFileSha256 = sourceFileSha256,
            SourceFileBytes = sourceBytes.LongLength,
            SourceHeaderJson = JsonSerializer.Serialize(csvRows.Header)
        };
        db.ImportBatches.Add(batch);

        // Retain source rows and score possible matches within the same period. A score only creates
        // a review candidate; it never excludes a row from accounting.
        var possibleMatches = await db.ImportedTransactions
            .AsNoTracking()
            .Where(t => t.BankAccountId == bankAccountId && t.PeriodId == periodId)
            .Select(t => new DuplicateSourceRow(
                t.Id,
                t.ImportBatchId,
                t.Date,
                t.Amount,
                t.Description,
                t.Balance,
                t.Reference,
                t.SourceRowSha256))
            .ToListAsync(cancellationToken);
        var candidateIndex = new DuplicateCandidateIndex(possibleMatches);

        // Get transaction rules for auto-categorisation
        var rules = await db.TransactionRules
            .Where(r => r.CompanyId == bankAccount.CompanyId)
            .Where(r => r.Category.CompanyId == bankAccount.CompanyId || (r.Category.IsSystem && r.Category.CompanyId == null))
            .OrderBy(r => r.Priority)
            .ToListAsync(cancellationToken);

        var warnings = new List<string>();
        var transactions = new List<ImportedTransaction>();
        int duplicateCandidates = 0;
        int autoCategorised = 0;

        foreach (var row in csvRows.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = row.Fields;
            var usingSplitAmounts = mapping.DebitColumn.HasValue && mapping.CreditColumn.HasValue;
            var usingDescriptionColumns = mapping.DescriptionColumns is { Length: > 0 };
            var requiredIndices = new List<int> { mapping.DateColumn };
            if (!usingDescriptionColumns) requiredIndices.Add(mapping.DescriptionColumn);
            else requiredIndices.AddRange(mapping.DescriptionColumns!);
            if (!usingSplitAmounts) requiredIndices.Add(mapping.AmountColumn);
            else { requiredIndices.Add(mapping.DebitColumn!.Value); requiredIndices.Add(mapping.CreditColumn!.Value); }
            if (fields.Length <= requiredIndices.Max())
            {
                warnings.Add($"Row {row.RowNumber}: insufficient columns");
                continue;
            }

            var dateStr = CleanCsvField(fields[mapping.DateColumn]);
            var description = usingDescriptionColumns
                ? string.Join(" ", mapping.DescriptionColumns!
                    .Where(c => c < fields.Length && !string.IsNullOrWhiteSpace(fields[c]))
                    .Select(c => NeutraliseCsvText(fields[c])))
                : NeutraliseCsvText(fields[mapping.DescriptionColumn]);

            if (string.IsNullOrWhiteSpace(description))
            {
                warnings.Add($"Row {row.RowNumber}: description is empty");
                continue;
            }
            if (description.Length > 1000)
            {
                warnings.Add($"Row {row.RowNumber}: description exceeds the 1,000-character retained-row limit");
                continue;
            }

            if (!TryParseMappedDate(dateStr, mapping.DateFormat, out var date))
            {
                warnings.Add($"Row {row.RowNumber}: could not parse date");
                continue;
            }

            if (date < period.PeriodStart || date > period.PeriodEnd)
            {
                warnings.Add($"Row {row.RowNumber}: transaction date {date:yyyy-MM-dd} is outside accounting period {period.PeriodStart:yyyy-MM-dd} to {period.PeriodEnd:yyyy-MM-dd}");
                continue;
            }

            decimal amount;
            if (usingSplitAmounts)
            {
                TryParseBankDecimal(CleanCsvField(fields[mapping.DebitColumn!.Value]), out var debitVal);
                TryParseBankDecimal(CleanCsvField(fields[mapping.CreditColumn!.Value]), out var creditVal);
                var hasDebit = debitVal > 0;
                var hasCredit = creditVal > 0;
                if (hasDebit && hasCredit)
                {
                    warnings.Add($"Row {row.RowNumber}: both debit and credit columns have non-zero values");
                    continue;
                }
                if (!hasDebit && !hasCredit)
                {
                    warnings.Add($"Row {row.RowNumber}: neither debit nor credit column has a parseable non-zero value");
                    continue;
                }
                amount = hasDebit ? -debitVal : creditVal;
            }
            else
            {
                if (!TryParseBankDecimal(CleanCsvField(fields[mapping.AmountColumn]), out amount))
                {
                    warnings.Add($"Row {row.RowNumber}: could not parse amount");
                    continue;
                }
            }
            var normalizedAmount = decimal.Round(amount, 2, MidpointRounding.ToEven);
            if (amount != normalizedAmount)
            {
                warnings.Add($"Row {row.RowNumber}: amount has more than two decimal places and was not imported");
                continue;
            }
            if (decimal.Abs(normalizedAmount) > 9_999_999_999_999_999.99m)
            {
                warnings.Add($"Row {row.RowNumber}: amount exceeds the supported 18-digit currency range");
                continue;
            }
            amount = normalizedAmount;

            decimal? balance = null;
            if (mapping.BalanceColumn is >= 0 && fields.Length > mapping.BalanceColumn)
            {
                var balanceString = CleanCsvField(fields[mapping.BalanceColumn.Value]);
                if (TryParseBankDecimal(balanceString, out var parsedBalance))
                {
                    var normalizedBalance = decimal.Round(parsedBalance, 2, MidpointRounding.ToEven);
                    if (parsedBalance != normalizedBalance
                        || decimal.Abs(normalizedBalance) > 9_999_999_999_999_999.99m)
                        warnings.Add($"Row {row.RowNumber}: running balance precision or range is unsupported and was retained as unavailable");
                    else
                        balance = normalizedBalance;
                }
                else if (!string.IsNullOrWhiteSpace(balanceString))
                    warnings.Add($"Row {row.RowNumber}: running balance could not be parsed and was retained as unavailable");
            }

            string? reference = null;
            if (mapping.ReferenceColumn is >= 0 && fields.Length > mapping.ReferenceColumn)
            {
                reference = NeutraliseCsvText(fields[mapping.ReferenceColumn.Value]);
                if (string.IsNullOrWhiteSpace(reference)) reference = null;
                if (reference?.Length > 200)
                {
                    warnings.Add($"Row {row.RowNumber}: bank reference exceeds the 200-character retained-row limit");
                    continue;
                }
            }

            var sourceRowSha256 = DuplicateCandidateDetector.ComputeSourceRowSha256(
                sourceFileSha256, row.RowNumber, date, amount, description, balance, reference);
            var sourceRow = new DuplicateSourceRow(
                null, null, date, amount, description, balance, reference, sourceRowSha256);
            var duplicateAssessment = candidateIndex.AssessAndAdd(sourceRow);

            int? categoryId = null;
            decimal? confidence = null;
            foreach (var rule in rules)
            {
                if (!description.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase)) continue;
                categoryId = rule.CategoryId;
                confidence = 0.85m;
                autoCategorised++;
                break;
            }

            var transaction = new ImportedTransaction
            {
                BankAccountId = bankAccountId,
                PeriodId = periodId,
                ImportBatch = batch,
                Date = date,
                Description = description,
                Amount = amount,
                Balance = balance,
                Reference = reference,
                CategoryId = categoryId,
                ConfidenceScore = confidence,
                IsDuplicate = false,
                ManualOverride = false,
                SourceRowNumber = row.RowNumber,
                SourceRowSha256 = sourceRowSha256,
                SourceRowJson = JsonSerializer.Serialize(fields),
                DuplicateReviewStatus = duplicateAssessment.IsCandidate
                    ? DuplicateReviewStatus.Pending
                    : DuplicateReviewStatus.NotCandidate,
                DuplicateCandidateKind = duplicateAssessment.Kind,
                DuplicateConfidence = duplicateAssessment.IsCandidate ? duplicateAssessment.Confidence : null,
                DuplicateCandidateReasonsJson = duplicateAssessment.IsCandidate
                    ? JsonSerializer.Serialize(duplicateAssessment.Reasons)
                    : null,
                DuplicateMatchedTransactionId = duplicateAssessment.MatchedTransactionId,
                DuplicateMatchedSourceRowSha256 = duplicateAssessment.MatchedSourceRowSha256
            };
            transactions.Add(transaction);
            if (duplicateAssessment.IsCandidate) duplicateCandidates++;
        }

        db.ImportedTransactions.AddRange(transactions);
        batch.MatchedCount = autoCategorised;
        batch.ImportWarningsJson = JsonSerializer.Serialize(warnings);
        await db.SaveChangesAsync(cancellationToken);

        return new ImportResult(
            csvRows.Rows.Count,
            transactions.Count,
            duplicateCandidates,
            autoCategorised,
            warnings,
            batch.Id,
            sourceFilename,
            sourceFileSha256,
            sourceBytes.LongLength);
    }

    public BankFormat DetectFormat(string headerLine)
    {
        var lower = headerLine.ToLower();
        // AIB Internet Banking: 12-column format with separate debit/credit columns.
        // Match on the full distinguishing signature before the simpler AIB substring.
        if (lower.Contains("posted transactions date") && lower.Contains("debit amount") && lower.Contains("credit amount"))
            return KnownFormats[0]; // AIBInternetBanking
        if (lower.Contains("posted account") || lower.Contains("aib")) return KnownFormats[1]; // AIB
        if (lower.Contains("bank of ireland") || lower.Contains("boi")) return KnownFormats[2]; // BOI
        if (lower.Contains("started date") || lower.Contains("revolut")) return KnownFormats[3]; // Revolut
        if (lower.Contains("balance_transaction") || lower.Contains("stripe")) return KnownFormats[4]; // Stripe
        return KnownFormats[5]; // Generic
    }

    private static void ValidateMapping(ColumnMapping mapping, int headerLength)
    {
        var usingSplitAmounts = mapping.DebitColumn.HasValue && mapping.CreditColumn.HasValue;
        var usingDescriptionColumns = mapping.DescriptionColumns is { Length: > 0 };

        bool OutOfRange(int i) => i < 0 || i >= headerLength;

        if (OutOfRange(mapping.DateColumn))
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (!usingDescriptionColumns && OutOfRange(mapping.DescriptionColumn))
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (!usingSplitAmounts && OutOfRange(mapping.AmountColumn))
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (usingSplitAmounts && (OutOfRange(mapping.DebitColumn!.Value) || OutOfRange(mapping.CreditColumn!.Value)))
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (usingDescriptionColumns && mapping.DescriptionColumns!.Any(OutOfRange))
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (mapping.BalanceColumn is >= 0 && mapping.BalanceColumn >= headerLength
            || mapping.ReferenceColumn is >= 0 && mapping.ReferenceColumn >= headerLength)
            throw new BusinessRuleException("CSV column mapping does not match the uploaded header.");
        if (string.IsNullOrWhiteSpace(mapping.DateFormat))
            throw new BusinessRuleException("CSV date format is required.");
    }

    private static string NormalizeFilename(string filename)
    {
        var normalized = Path.GetFileName(filename?.Trim());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "bank-statement.csv";
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static async Task<CsvImportRows> ReadCsvRowsAsync(string content, CancellationToken cancellationToken)
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
        cancellationToken.ThrowIfCancellationRequested();
        if (!await parser.ReadAsync())
            return new CsvImportRows([], []);

        var header = parser.Record?.ToArray() ?? [];
        var rows = new List<CsvImportRow>();
        var rowNumber = 0;
        while (await parser.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.Record?.ToArray() ?? [];
            if (fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            rowNumber++;
            rows.Add(new CsvImportRow(rowNumber, fields));
        }

        return new CsvImportRows(header, rows);
    }

    private static bool LooksLikeHeaderlessData(string[] header)
    {
        return KnownFormats
            .Select(format => format.Mapping)
            .Where(mapping => mapping.AmountColumn >= 0
                && header.Length > Math.Max(mapping.DateColumn, mapping.AmountColumn))
            .Any(mapping =>
                TryParseMappedDate(CleanCsvField(header[mapping.DateColumn]), mapping.DateFormat, out _)
                && TryParseBankDecimal(CleanCsvField(header[mapping.AmountColumn]), out _));
    }

    private static bool HasRecognizedGenericHeader(string[] header, ColumnMapping mapping)
    {
        var dateHeader = CleanCsvField(header[mapping.DateColumn]).ToLowerInvariant();
        var descriptionHeader = CleanCsvField(header[mapping.DescriptionColumn]).ToLowerInvariant();
        var amountHeader = CleanCsvField(header[mapping.AmountColumn]).ToLowerInvariant();
        return dateHeader.Contains("date", StringComparison.Ordinal)
            && (descriptionHeader.Contains("description", StringComparison.Ordinal)
                || descriptionHeader.Contains("detail", StringComparison.Ordinal)
                || descriptionHeader.Contains("memo", StringComparison.Ordinal)
                || descriptionHeader.Contains("narrative", StringComparison.Ordinal))
            && (amountHeader.Contains("amount", StringComparison.Ordinal)
                || amountHeader.Contains("value", StringComparison.Ordinal)
                || amountHeader.Contains("debit", StringComparison.Ordinal)
                || amountHeader.Contains("credit", StringComparison.Ordinal));
    }

    private static bool TryParseMappedDate(string value, string mappingFormat, out DateOnly date)
    {
        var formats = mappingFormat switch
        {
            "dd/MM/yyyy" => new[] { "d/M/yyyy", "dd/M/yyyy", "d/MM/yyyy", "dd/MM/yyyy" },
            "yyyy-MM-dd" => new[] { "yyyy-M-d", "yyyy-MM-d", "yyyy-M-dd", "yyyy-MM-dd" },
            _ => new[] { mappingFormat }
        };
        return DateOnly.TryParseExact(
            value,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static bool TryParseBankDecimal(string value, out decimal amount)
    {
        amount = 0m;
        var token = value.Trim();
        if (token.Length == 0) return false;
        var negative = token.StartsWith('(') && token.EndsWith(')');
        if (negative) token = token[1..^1].Trim();
        token = token.TrimStart('€', '£', '$').TrimEnd('€', '£', '$').Trim();
        if (token.StartsWith('-'))
        {
            negative = !negative;
            token = token[1..];
        }
        else if (token.StartsWith('+'))
        {
            token = token[1..];
        }
        if (token.Length == 0 || token.Any(character => !char.IsAsciiDigit(character) && character is not ('.' or ',')))
            return false;

        var commaCount = token.Count(character => character == ',');
        var dotCount = token.Count(character => character == '.');
        string canonical;
        if (commaCount > 0 && dotCount > 0)
        {
            var decimalSeparator = token.LastIndexOf(',') > token.LastIndexOf('.') ? ',' : '.';
            var groupSeparator = decimalSeparator == ',' ? '.' : ',';
            if (token.Count(character => character == decimalSeparator) != 1)
                return false;
            var decimalIndex = token.LastIndexOf(decimalSeparator);
            var integerPart = token[..decimalIndex];
            var fractionPart = token[(decimalIndex + 1)..];
            if (fractionPart.Length is < 1 or > 2
                || !fractionPart.All(char.IsAsciiDigit)
                || !TryNormalizeGroupedInteger(integerPart, groupSeparator, out var normalizedInteger))
                return false;
            canonical = $"{normalizedInteger}.{fractionPart}";
        }
        else if (commaCount > 1 || dotCount > 1)
        {
            var separator = commaCount > 1 ? ',' : '.';
            if (!TryNormalizeGroupedInteger(token, separator, out canonical)) return false;
        }
        else if (commaCount == 1 || dotCount == 1)
        {
            var separator = commaCount == 1 ? ',' : '.';
            var parts = token.Split(separator);
            if (parts.Length != 2 || parts[0].Length == 0 || !parts[0].All(char.IsAsciiDigit) || !parts[1].All(char.IsAsciiDigit))
                return false;
            // A single separator followed by exactly three digits is ambiguous (thousands vs
            // unsupported 3-decimal currency). Refuse to guess; exports should include a decimal
            // part or omit the thousands separator.
            if (parts[1].Length == 3) return false;
            if (parts[1].Length is < 1 or > 2) return false;
            canonical = $"{parts[0]}.{parts[1]}";
        }
        else
        {
            canonical = token;
        }

        if (!decimal.TryParse(canonical, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;
        if (negative) amount = -amount;
        return true;
    }

    private static bool TryNormalizeGroupedInteger(string value, char separator, out string normalized)
    {
        normalized = string.Empty;
        var groups = value.Split(separator);
        if (groups.Length < 2
            || groups[0].Length is < 1 or > 3
            || !groups[0].All(char.IsAsciiDigit)
            || groups.Skip(1).Any(group => group.Length != 3 || !group.All(char.IsAsciiDigit)))
            return false;
        normalized = string.Concat(groups);
        return true;
    }

    private static string CleanCsvField(string? value) => value?.Trim() ?? string.Empty;

    // Characters that make a spreadsheet treat a cell as a formula. A bank memo/reference that
    // begins with one of these is a CSV-injection vector: e.g. =HYPERLINK("http://evil",...) in a
    // description executes when a user later exports the imported transactions to Excel/Sheets.
    private static readonly char[] CsvFormulaTriggers = ['=', '+', '-', '@', '\t', '\r', '\n'];

    // Neutralise spreadsheet formula-injection in free-text fields that are stored verbatim and may
    // later be exported. If the field begins with a formula trigger, prefix an apostrophe so the
    // spreadsheet treats it as literal text (OWASP CSV-injection mitigation). Numeric/date fields are
    // parsed to typed values with CleanCsvField (never stored raw), so they keep trim-only cleaning
    // and a legitimate leading-minus amount like "-12.50" still parses correctly.
    private static string NeutraliseCsvText(string? value)
    {
        var cleaned = CleanCsvField(value);
        if (cleaned.Length > 0 && Array.IndexOf(CsvFormulaTriggers, cleaned[0]) >= 0)
            return "'" + cleaned;
        return cleaned;
    }

    private sealed record CsvImportRows(string[] Header, List<CsvImportRow> Rows);

    private sealed record CsvImportRow(int RowNumber, string[] Fields);
}
