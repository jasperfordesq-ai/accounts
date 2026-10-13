using System.Text.RegularExpressions;
using Accounts.Api.Data;
using Accounts.Api.Middleware;
using Microsoft.EntityFrameworkCore;

namespace Accounts.Api.Services;

/// <summary>
/// Builds the plain-language line a person sees before approving a connector action. It is built
/// on the server from tenant-scoped records and only uses public or non-personal fields (company
/// legal name and CRO number, officer name and role as on the CRO register, period end, account
/// name, user id and role). It never includes addresses, emails or free-text narratives.
/// </summary>
public sealed partial class ConnectorActionSummaryService(AccountsDbContext db)
{
    public async Task<string> DescribeAsync(HttpContext context, string routePattern, CancellationToken cancellationToken = default)
    {
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method.ToUpperInvariant();
        var parts = new List<string> { DescribeAction(method, routePattern) };
        try
        {
            if (TenantAccessMiddleware.TryGetCompanyId(context.Request.Path, out var companyId))
            {
                var company = await db.Companies.AsNoTracking().IgnoreQueryFilters()
                    .Where(candidate => candidate.Id == companyId && candidate.TenantId == AuthContext.RequireUser(context).TenantId)
                    .Select(candidate => new { candidate.LegalName, candidate.CroNumber })
                    .SingleOrDefaultAsync(cancellationToken);
                if (company is not null)
                    parts.Add(string.IsNullOrWhiteSpace(company.CroNumber)
                        ? $"company {company.LegalName}"
                        : $"company {company.LegalName} (CRO {company.CroNumber})");
            }

            var period = PeriodRoute().Match(path);
            if (period.Success && int.TryParse(period.Groups["id"].Value, out var periodId))
            {
                var row = await db.AccountingPeriods.AsNoTracking()
                    .Where(candidate => candidate.Id == periodId)
                    .Select(candidate => new { candidate.PeriodEnd, candidate.Status })
                    .SingleOrDefaultAsync(cancellationToken);
                if (row is not null)
                    parts.Add($"period ending {row.PeriodEnd:yyyy-MM-dd} (currently {row.Status})");
            }

            var officer = OfficerRoute().Match(path);
            if (officer.Success && int.TryParse(officer.Groups["id"].Value, out var officerId))
            {
                var row = await db.CompanyOfficers.AsNoTracking()
                    .Where(candidate => candidate.Id == officerId)
                    .Select(candidate => new { candidate.Name, candidate.Role })
                    .SingleOrDefaultAsync(cancellationToken);
                if (row is not null) parts.Add($"officer {row.Name} ({row.Role})");
            }

            var bankAccount = BankAccountRoute().Match(path);
            if (bankAccount.Success && int.TryParse(bankAccount.Groups["id"].Value, out var bankAccountId))
            {
                var name = await db.BankAccounts.AsNoTracking()
                    .Where(candidate => candidate.Id == bankAccountId)
                    .Select(candidate => candidate.Name)
                    .SingleOrDefaultAsync(cancellationToken);
                if (name is not null) parts.Add($"bank account {name}");
            }

            var user = AdminUserRoute().Match(path);
            if (user.Success && int.TryParse(user.Groups["id"].Value, out var userId))
            {
                var role = await db.UserAccounts.AsNoTracking()
                    .Where(candidate => candidate.Id == userId)
                    .Select(candidate => candidate.Role)
                    .SingleOrDefaultAsync(cancellationToken);
                if (role is not null) parts.Add($"user #{userId} ({role})");
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A summary lookup must never block the approval prompt; fall back to the route words.
        }

        var resourceId = ConnectorPostureMiddleware.ResourceIdOf(context.Request.Path);
        if (parts.Count == 1 && resourceId is not null) parts.Add($"record {resourceId}");
        if (await RequestedValuesAsync(context.Request) is { Length: > 0 } requested)
            parts.Add("requested: " + requested);
        var summary = string.Join(", ", parts);
        return summary.Length > 500 ? summary[..500] : summary;
    }

    /// <summary>
    /// Fields whose new value decides what the person is approving. Only enumerations, flags,
    /// dates and identifiers are shown; free text, names and addresses never are.
    /// </summary>
    private static readonly HashSet<string> RequestedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "status", "role", "isActive", "active", "decision", "approved", "accessLevel",
        "filedDate", "filingDate", "dateFiled", "paymentDate", "approvalDate", "noticeDate",
        "chartOfAccountsVariant", "variant", "taxType", "regime", "filingRegime", "electedRegime",
        "sizeClass", "received", "outcome", "kind", "workflow", "submissionReference"
    };

    private static async Task<string?> RequestedValuesAsync(HttpRequest request)
    {
        if (request.ContentLength is 0 or > 1_000_000) return null;
        try
        {
            request.EnableBuffering();
            request.Body.Position = 0;
            using var document = await System.Text.Json.JsonDocument.ParseAsync(request.Body);
            request.Body.Position = 0;
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var values = new List<string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals("companyIds", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    values.Add($"companyIds=[{string.Join(",", property.Value.EnumerateArray().Take(20).Select(item => item.GetRawText()))}]");
                    continue;
                }
                if (!RequestedFields.Contains(property.Name)) continue;
                var value = property.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                    System.Text.Json.JsonValueKind.Number or System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => property.Value.GetRawText(),
                    System.Text.Json.JsonValueKind.Null => "(cleared)",
                    _ => null
                };
                if (value is null) continue;
                value = new string(value.Where(character => !char.IsControl(character)).ToArray());
                if (value.Length > 60) value = value[..60];
                values.Add($"{property.Name}={value}");
            }
            return values.Count == 0 ? null : string.Join(", ", values);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        finally
        {
            if (request.Body.CanSeek) request.Body.Position = 0;
        }
    }

    public static string DescribeAction(string method, string routePattern)
    {
        var words = routePattern
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !segment.StartsWith('{') && segment is not "api" and not "companies" and not "periods")
            .Select(segment => segment.Replace('-', ' '))
            .ToArray();
        var subject = words.Length == 0 ? "record" : string.Join(" ", words);
        var verb = method switch
        {
            "DELETE" => "Delete",
            "PUT" or "PATCH" => "Change",
            _ => "Run"
        };
        return $"{verb} {subject}";
    }

    [GeneratedRegex(@"^/api/companies/\d+/periods/(?<id>\d+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex PeriodRoute();

    [GeneratedRegex(@"^/api/companies/\d+/officers/(?<id>\d+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficerRoute();

    [GeneratedRegex(@"^/api/companies/\d+/bank-accounts/(?<id>\d+)/?$", RegexOptions.IgnoreCase)]
    private static partial Regex BankAccountRoute();

    [GeneratedRegex(@"^/api/admin/users/(?<id>\d+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AdminUserRoute();
}
