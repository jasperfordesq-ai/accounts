using System.Text.Json;
using System.Text.RegularExpressions;

namespace Accounts.Api.Middleware;

/// <summary>
/// Turns a minimal-API binding failure into a message that names the offending field or route
/// parameter and, for an enumeration, lists the values FilingBridge accepts. Without this a wrong
/// tax type, valuation method or body shape produced an empty 400 that the client could only show
/// as "Invalid request".
/// </summary>
public static partial class RequestBindingError
{
    public static string Describe(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException json)
            return DescribeJson(json);

        var bind = BindParameterPattern().Match(exception.Message);
        if (bind.Success)
        {
            var name = bind.Groups["name"].Value;
            var value = bind.Groups["value"].Value;
            var allowed = EnumValues(bind.Groups["type"].Value);
            return allowed is null
                ? $"Invalid value '{value}' for '{name}'."
                : $"Unknown {name} '{value}'. Allowed values: {allowed}.";
        }

        return exception.Message;
    }

    private static string DescribeJson(JsonException json)
    {
        var path = json.Path is null or "" or "$" ? null : json.Path.TrimStart('$', '.');
        var where = path is null ? "The request body" : $"The request body field '{path}'";
        var convert = ConvertTypePattern().Match(json.Message ?? string.Empty);
        if (convert.Success)
        {
            var typeName = convert.Groups["type"].Value;
            var allowed = EnumValues(typeName);
            return allowed is null
                ? $"{where} has the wrong type (expected {FriendlyType(typeName)})."
                : $"{where} has an unknown value. Allowed values: {allowed}.";
        }

        return path is null ? "The request body is not valid JSON." : $"{where} is not valid.";
    }

    // "Allowed values" for an enumeration named by the framework message, else null.
    private static string? EnumValues(string typeName)
    {
        var name = typeName.Trim();
        var nullable = NullablePattern().Match(name);
        if (nullable.Success)
            name = nullable.Groups["inner"].Value;
        name = name.Split(',')[0].Trim().TrimStart('[').TrimEnd(']');

        var type = Type.GetType(name)
            ?? typeof(RequestBindingError).Assembly.GetType(name)
            ?? typeof(RequestBindingError).Assembly.GetTypes()
                .FirstOrDefault(candidate => candidate.IsEnum && candidate.Name == name);
        return type is { IsEnum: true } ? string.Join(", ", Enum.GetNames(type)) : null;
    }

    private static string FriendlyType(string typeName)
    {
        var name = typeName.Split(',')[0].Trim();
        var nullable = NullablePattern().Match(name);
        if (nullable.Success)
            name = nullable.Groups["inner"].Value.Split(',')[0].Trim().TrimStart('[');
        return name switch
        {
            "System.Decimal" or "System.Double" or "System.Single" or "System.Int32" or "System.Int64" or "System.Int16" => "a number",
            "System.Boolean" => "true or false",
            "System.DateOnly" or "System.DateTime" or "System.DateTimeOffset" => "a date (yyyy-MM-dd)",
            "System.String" => "text",
            _ => name[(name.LastIndexOf('.') + 1)..]
        };
    }

    [GeneratedRegex(@"Failed to bind parameter ""(?<type>[^\s""]+) (?<name>[^\s""]+)"" from ""(?<value>[^""]*)""")]
    private static partial Regex BindParameterPattern();

    [GeneratedRegex(@"could not be converted to (?<type>[^\s]+?)\.(?:\s|$)")]
    private static partial Regex ConvertTypePattern();

    [GeneratedRegex(@"^System\.Nullable`1\[+(?<inner>[^\]]+)\]+$")]
    private static partial Regex NullablePattern();
}
