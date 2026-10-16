using System.Text.Json;

namespace Accounts.Api.Endpoints;

/// <summary>
/// Strict reader for the going-concern body. The typed <see cref="GoingConcernInput"/> bound any JSON
/// object, so a body with other field names (for example goingConcernConfirmed / goingConcernNote,
/// the names the GET returns) became confirmed=false, note=null and reset the record with a 200.
/// Here an unknown, missing, duplicated or mistyped field is a 400 that names the accepted fields.
/// </summary>
public static class GoingConcernBody
{
    public const string AllowedFieldsHint =
        "The going-concern body accepts exactly: confirmed (boolean, required) and note (string, optional).";

    public static async Task<(GoingConcernInput? Input, IResult? Error)> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        string text;
        using (var reader = new StreamReader(request.Body))
            text = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return (null, Reject("The request body is empty."));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return (null, Reject("The request body is not valid JSON."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, Reject("The request body must be a JSON object."));

            bool? confirmed = null;
            string? note = null;
            var noteSeen = false;
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "confirmed", StringComparison.OrdinalIgnoreCase))
                {
                    if (confirmed is not null)
                        return (null, Reject("'confirmed' appears more than once."));
                    if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (null, Reject("'confirmed' must be true or false."));
                    confirmed = property.Value.GetBoolean();
                }
                else if (string.Equals(property.Name, "note", StringComparison.OrdinalIgnoreCase))
                {
                    if (noteSeen)
                        return (null, Reject("'note' appears more than once."));
                    if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        return (null, Reject("'note' must be a string or null."));
                    noteSeen = true;
                    note = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                }
                else
                {
                    return (null, Reject($"Unknown field '{property.Name}'."));
                }
            }

            if (confirmed is null)
                return (null, Reject("'confirmed' is required."));
            return (new GoingConcernInput(confirmed.Value, note), null);
        }
    }

    private static IResult Reject(string detail) =>
        Results.BadRequest(new { error = $"{detail} {AllowedFieldsHint}", allowedFields = new[] { "confirmed", "note" } });
}
