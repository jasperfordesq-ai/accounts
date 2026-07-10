using Accounts.Api.Entities;
using Accounts.Api.Rules;
using Accounts.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounts.Tests;

public sealed class DeadlineReminderPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DueSoon_EnqueuesExactlyOnceAcrossRepeatedRuns()
    {
        var planner = Planner();
        var subject = Subject(new DateOnly(2026, 7, 24));

        var first = planner.Plan(subject, []);
        var enqueue = Assert.Single(first.Actions);
        Assert.Equal(DeadlineReminderKind.DueSoon, enqueue.ReminderKind);
        Assert.Equal(64, enqueue.DeduplicationKeySha256!.Length);

        var persisted = History(enqueue, DeadlineReminderState.Delivered, Now.UtcDateTime);
        var repeated = planner.Plan(subject, [persisted]);
        Assert.Empty(repeated.Actions);
    }

    [Fact]
    public void Overdue_SupersedesPendingDueSoonAndEnqueuesOverdue()
    {
        var planner = Planner();
        var subject = Subject(new DateOnly(2026, 7, 9));
        var priorSubject = subject with { DueDate = new DateOnly(2026, 7, 9) };
        var priorKey = DeadlineReminderPlanner.CreateDeduplicationKey(
            priorSubject,
            DeadlineReminderKind.DueSoon,
            null,
            null);
        var prior = new DeadlineReminderHistory(
            Guid.NewGuid(),
            DeadlineReminderKind.DueSoon,
            DeadlineReminderState.RetryScheduled,
            subject.DueDate,
            priorKey,
            Now.UtcDateTime.AddDays(-2));

        var plan = planner.Plan(subject, [prior]);

        Assert.Contains(plan.Actions, action => action.ActionType == DeadlineReminderPlanActionType.Supersede
            && action.ExistingOutboxId == prior.Id);
        Assert.Contains(plan.Actions, action => action.ActionType == DeadlineReminderPlanActionType.Enqueue
            && action.ReminderKind == DeadlineReminderKind.Overdue);
    }

    [Fact]
    public void CorrectedDeadline_SupersedesOldIntentAndCreatesCorrectionAndCurrentRiskIntents()
    {
        var planner = Planner();
        var oldDueDate = new DateOnly(2026, 7, 20);
        var subject = Subject(new DateOnly(2026, 7, 25));
        var oldSubject = subject with { DueDate = oldDueDate };
        var oldKey = DeadlineReminderPlanner.CreateDeduplicationKey(
            oldSubject,
            DeadlineReminderKind.DueSoon,
            null,
            null);
        var old = new DeadlineReminderHistory(
            Guid.NewGuid(),
            DeadlineReminderKind.DueSoon,
            DeadlineReminderState.Pending,
            oldDueDate,
            oldKey,
            Now.UtcDateTime.AddDays(-1));

        var plan = planner.Plan(subject, [old]);

        Assert.Contains(plan.Actions, action => action.ActionType == DeadlineReminderPlanActionType.Supersede
            && action.ExistingOutboxId == old.Id);
        Assert.Contains(plan.Actions, action => action.ActionType == DeadlineReminderPlanActionType.Enqueue
            && action.ReminderKind == DeadlineReminderKind.Corrected);
        Assert.Contains(plan.Actions, action => action.ActionType == DeadlineReminderPlanActionType.Enqueue
            && action.ReminderKind == DeadlineReminderKind.DueSoon);
    }

    [Fact]
    public void FiledDeadline_CancelsRetryableFailuresAndNeverEnqueues()
    {
        var planner = Planner();
        var subject = Subject(new DateOnly(2026, 7, 9)) with { FiledDate = new DateOnly(2026, 7, 10) };
        var key = DeadlineReminderPlanner.CreateDeduplicationKey(
            subject,
            DeadlineReminderKind.Overdue,
            null,
            null);
        var retry = new DeadlineReminderHistory(
            Guid.NewGuid(),
            DeadlineReminderKind.Overdue,
            DeadlineReminderState.RetryScheduled,
            subject.DueDate,
            key,
            Now.UtcDateTime.AddHours(-1));

        var plan = planner.Plan(subject, [retry]);

        var cancel = Assert.Single(plan.Actions);
        Assert.Equal(DeadlineReminderPlanActionType.Cancel, cancel.ActionType);
        Assert.Equal(retry.Id, cancel.ExistingOutboxId);
    }

    [Fact]
    public void OutsideRiskWindow_DoesNotEnqueue()
    {
        var plan = Planner().Plan(Subject(new DateOnly(2026, 10, 1)), []);
        Assert.Empty(plan.Actions);
    }

    private static DeadlineReminderPlanner Planner() => new(
        new FixedTimeProvider(Now),
        Options.Create(new DeadlineDeliveryConfig { DueSoonDays = 30 }));

    private static DeadlineReminderSubject Subject(DateOnly dueDate) => new(
        7,
        11,
        13,
        17,
        DeadlineType.CRO,
        dueDate,
        null,
        new string('a', 64));

    private static DeadlineReminderHistory History(
        DeadlineReminderPlanAction action,
        DeadlineReminderState state,
        DateTime createdAtUtc) => new(
        Guid.NewGuid(),
        action.ReminderKind,
        state,
        action.ObservedDueDate,
        action.DeduplicationKeySha256!,
        createdAtUtc);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
