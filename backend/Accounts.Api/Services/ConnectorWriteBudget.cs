using Accounts.Api.Rules;
using Microsoft.Extensions.Options;

namespace Accounts.Api.Services;

/// <summary>
/// Per-connector-family sliding one-minute write budget. The connector usually shares the
/// owner's IP address with their browser, so the IP rate limiter cannot be what stops a looping agent.
/// </summary>
public sealed class ConnectorWriteBudget(IOptions<ConnectorConfig> options)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Dictionary<Guid, Queue<DateTimeOffset>> windows = [];
    private readonly Lock gate = new();
    private int takesSinceSweep;

    public int Limit => Math.Max(1, options.Value.WriteBudgetPerMinute);

    public bool TryTake(Guid familyId, DateTimeOffset now, out TimeSpan retryAfter)
    {
        lock (gate)
        {
            if (++takesSinceSweep >= 500) Sweep(now);
            if (!windows.TryGetValue(familyId, out var stamps))
                windows[familyId] = stamps = new Queue<DateTimeOffset>();
            while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
            if (stamps.Count >= Limit)
            {
                retryAfter = Window - (now - stamps.Peek());
                if (retryAfter < TimeSpan.FromSeconds(1)) retryAfter = TimeSpan.FromSeconds(1);
                return false;
            }
            stamps.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        takesSinceSweep = 0;
        foreach (var (familyId, stamps) in windows.ToArray())
        {
            while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
            if (stamps.Count == 0) windows.Remove(familyId);
        }
    }
}
