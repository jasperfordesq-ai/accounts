using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

public sealed record DeadlineReminderSubject(
    int TenantId,
    int CompanyId,
    int PeriodId,
    int FilingDeadlineId,
    DeadlineType DeadlineType,
    DateOnly DueDate,
    DateOnly? FiledDate,
    string? CalculationFingerprintSha256);

public sealed record DeadlineReminderHistory(
    Guid Id,
    DeadlineReminderKind ReminderKind,
    DeadlineReminderState State,
    DateOnly ObservedDueDate,
    string DeduplicationKeySha256,
    DateTime CreatedAtUtc);

public enum DeadlineReminderPlanActionType
{
    Enqueue,
    Supersede,
    Cancel
}

public sealed record DeadlineReminderPlanAction(
    DeadlineReminderPlanActionType ActionType,
    Guid? ExistingOutboxId,
    DeadlineReminderKind ReminderKind,
    DateOnly ObservedDueDate,
    string? DeduplicationKeySha256);

public sealed record DeadlineReminderPlan(
    DateOnly EvaluatedOn,
    IReadOnlyList<DeadlineReminderPlanAction> Actions);

/// <summary>
/// Pure, time-controlled deadline state transition planner. The persisted outbox is the sole
/// deduplication source, so process restarts cannot cause duplicate notification intents.
/// </summary>
public sealed class DeadlineReminderPlanner(
    TimeProvider timeProvider,
    IOptions<DeadlineDeliveryConfig> options)
{
    private static readonly TimeZoneInfo IrelandTimeZone = ResolveIrelandTimeZone();
    private readonly DeadlineDeliveryConfig config = options.Value;

    public DeadlineReminderPlan Plan(
        DeadlineReminderSubject subject,
        IReadOnlyCollection<DeadlineReminderHistory> history)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(history);
        if (subject.TenantId <= 0 || subject.CompanyId <= 0 || subject.PeriodId <= 0 || subject.FilingDeadlineId <= 0)
            throw new ArgumentOutOfRangeException(nameof(subject), "Reminder subjects require positive tenant, company, period and deadline identifiers.");
        if (subject.DueDate == default)
            throw new ArgumentException("Reminder subjects require a due date.", nameof(subject));

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), IrelandTimeZone).DateTime);
        var actions = new List<DeadlineReminderPlanAction>();
        var active = history
            .Where(item => item.State is DeadlineReminderState.Pending
                or DeadlineReminderState.Delivering
                or DeadlineReminderState.RetryScheduled)
            .ToList();

        if (subject.FiledDate is not null)
        {
            actions.AddRange(active.Select(item => ExistingAction(DeadlineReminderPlanActionType.Cancel, item)));
            return new DeadlineReminderPlan(today, actions);
        }

        var latestObserved = history
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        var dueDateChanged = latestObserved is not null && latestObserved.ObservedDueDate != subject.DueDate;
        if (dueDateChanged)
        {
            actions.AddRange(active
                .Where(item => item.ObservedDueDate != subject.DueDate)
                .Select(item => ExistingAction(DeadlineReminderPlanActionType.Supersede, item)));

            var correctionKey = CreateDeduplicationKey(
                subject,
                DeadlineReminderKind.Corrected,
                latestObserved!.ObservedDueDate,
                latestObserved.Id);
            if (history.All(item => !string.Equals(item.DeduplicationKeySha256, correctionKey, StringComparison.Ordinal)))
                actions.Add(NewAction(DeadlineReminderKind.Corrected, subject.DueDate, correctionKey));
        }

        var daysUntilDue = subject.DueDate.DayNumber - today.DayNumber;
        DeadlineReminderKind? riskKind = daysUntilDue switch
        {
            < 0 => DeadlineReminderKind.Overdue,
            _ when daysUntilDue <= config.DueSoonDays => DeadlineReminderKind.DueSoon,
            _ => null
        };
        if (riskKind is null)
            return new DeadlineReminderPlan(today, actions);

        if (riskKind == DeadlineReminderKind.Overdue)
        {
            actions.AddRange(active
                .Where(item => item.ObservedDueDate == subject.DueDate
                    && item.ReminderKind == DeadlineReminderKind.DueSoon
                    && actions.All(action => action.ExistingOutboxId != item.Id))
                .Select(item => ExistingAction(DeadlineReminderPlanActionType.Supersede, item)));
        }

        var riskKey = CreateDeduplicationKey(subject, riskKind.Value, null, null);
        if (history.All(item => !string.Equals(item.DeduplicationKeySha256, riskKey, StringComparison.Ordinal)))
            actions.Add(NewAction(riskKind.Value, subject.DueDate, riskKey));

        return new DeadlineReminderPlan(today, actions);
    }

    public static string CreateDeduplicationKey(
        DeadlineReminderSubject subject,
        DeadlineReminderKind kind,
        DateOnly? previousDueDate,
        Guid? previousOutboxId)
    {
        var payload = string.Join('|',
            "deadline-reminder-v1",
            subject.TenantId.ToString(CultureInfo.InvariantCulture),
            subject.FilingDeadlineId.ToString(CultureInfo.InvariantCulture),
            kind.ToString(),
            subject.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            previousDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-",
            previousOutboxId?.ToString("N") ?? "-");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static DeadlineReminderPlanAction ExistingAction(
        DeadlineReminderPlanActionType actionType,
        DeadlineReminderHistory item) =>
        new(actionType, item.Id, item.ReminderKind, item.ObservedDueDate, null);

    private static DeadlineReminderPlanAction NewAction(
        DeadlineReminderKind kind,
        DateOnly dueDate,
        string key) =>
        new(DeadlineReminderPlanActionType.Enqueue, null, kind, dueDate, key);

    private static TimeZoneInfo ResolveIrelandTimeZone()
    {
        foreach (var id in new[] { "Europe/Dublin", "GMT Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Try the platform-specific identifier next.
            }
            catch (InvalidTimeZoneException)
            {
                // Try the platform-specific identifier next.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
