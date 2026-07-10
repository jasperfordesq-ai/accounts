namespace Accounts.Api.Services;

/// <summary>
/// The platform can render an accountant-review prototype, but it does not yet implement the full
/// Revenue content/tagging contract or an official validator integration. No prototype may enter a
/// filing-ready workflow until that independent capability is implemented and evidenced.
/// </summary>
public static class RevenueIxbrlGenerationPolicy
{
    public const bool FilingReadyGenerationEnabled = false;

    public const string ManualHandoffReason =
        "Revenue filing-ready iXBRL generation is disabled. The current XHTML is an incomplete "
        + "accountant-review prototype only; prepare and validate the complete Revenue artifact in "
        + "an approved external production tool and retain the manual handoff evidence.";

    public static void AssertFilingReadyGenerationEnabled()
    {
        if (!FilingReadyGenerationEnabled)
            throw new FilingReleaseBlockedException(ManualHandoffReason);
    }
}
