using Accounts.Api.Data;
using Accounts.Api.Entities;
using System.Text.Json;

namespace Accounts.Api.Services;

public class AuditService(AccountsDbContext db)
{
    public async Task LogAsync(int? companyId, int? periodId, string entityType, int entityId, string action, object? oldValue = null, object? newValue = null, string? userId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            PeriodId = periodId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValueJson = oldValue != null ? JsonSerializer.Serialize(oldValue) : null,
            NewValueJson = newValue != null ? JsonSerializer.Serialize(newValue) : null,
            UserId = userId
        });
        await db.SaveChangesAsync();
    }
}
