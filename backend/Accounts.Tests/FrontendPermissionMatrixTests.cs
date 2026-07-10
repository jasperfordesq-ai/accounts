using System.Runtime.CompilerServices;
using System.Text.Json;
using Accounts.Api.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Accounts.Tests;

public class FrontendPermissionMatrixTests
{
    private static readonly string[] Roles = ["Owner", "Accountant", "Reviewer", "Client"];

    [Fact]
    public void EveryAuditedUiActionMatchesBackendRoleAuthorization()
    {
        var catalogPath = Path.Combine(
            RepositoryRoot(),
            "frontend",
            "src",
            "lib",
            "permission-action-catalog.json");
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var actions = document.RootElement.EnumerateArray().ToArray();

        Assert.True(actions.Length >= 65, "The permission catalog must retain the complete audited UI action inventory.");
        Assert.Equal(actions.Length, actions.Select(ActionId).Distinct(StringComparer.Ordinal).Count());

        foreach (var action in actions)
        {
            var method = action.GetProperty("method").GetString()!;
            if (method == "LOCAL")
                continue;

            var path = action.GetProperty("path").GetString()!;
            var requiredPermission = action.GetProperty("requiredPermission").GetString()!;

            foreach (var role in Roles)
            {
                var decision = RoleAuthorizationService.Authorize(
                    User(role),
                    new PathString(path),
                    method);

                var expected = Expected(requiredPermission, role);
                Assert.True(
                    decision.IsAllowed == expected,
                    $"{ActionId(action)} expected {role} allowed={expected}, backend returned {decision.IsAllowed}: {decision.DenialReason}");
            }
        }
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Accountant")]
    [InlineData("Reviewer")]
    [InlineData("Client")]
    public void PasswordChangeAndLogoutRemainAvailableToEveryAuthenticatedFirmRole(string role)
    {
        var user = User(role);

        Assert.True(RoleAuthorizationService.Authorize(
            user,
            new PathString("/api/auth/password"),
            HttpMethods.Post).IsAllowed);
        Assert.True(RoleAuthorizationService.Authorize(
            user,
            new PathString("/api/auth/logout"),
            HttpMethods.Post).IsAllowed);
    }

    private static bool Expected(string permission, string role) => permission switch
    {
        "canRead" => true,
        "canCreateCompany" or "canDeleteCompany" or "canManageUsers" => role == "Owner",
        "canWriteWorkingPapers" => role is "Owner" or "Accountant",
        "canReadInternalWorkingPapers" => role is "Owner" or "Accountant" or "Reviewer",
        "canReview" or "canApprove" or "canReviewReleaseEvidence" => role is "Owner" or "Reviewer",
        _ => throw new InvalidOperationException($"Unknown frontend permission capability '{permission}'.")
    };

    private static string ActionId(JsonElement action) =>
        action.GetProperty("id").GetString()!;

    private static AuthenticatedUser User(string role) =>
        new(7, 1, "Permission Matrix Firm", $"{role.ToLowerInvariant()}@example.ie", role, role);

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
