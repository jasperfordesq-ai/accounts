using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Accounts.Api.Services;

/// <summary>
/// Binds an approval to one exact request: session family, method, path (query ignored) and a
/// canonical form of the JSON body (object keys sorted, array order kept). Non-JSON bodies are
/// bound by the SHA-256 of their bytes.
/// </summary>
public static class ConnectorActionDigest
{
    public static string Compute(Guid familyId, string method, string path, ReadOnlySpan<byte> body)
    {
        var canonicalBody = CanonicalBody(body);
        var material = JsonSerializer.Serialize(new[]
        {
            familyId.ToString("D"),
            method.Trim().ToUpperInvariant(),
            (path ?? "").Trim().TrimEnd('/').ToLowerInvariant(),
            canonicalBody
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    public static async Task<string> ComputeAsync(Guid familyId, HttpRequest request)
    {
        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return Compute(familyId, request.Method, request.Path.Value ?? "", buffer.ToArray());
    }

    private static string CanonicalBody(ReadOnlySpan<byte> body)
    {
        var trimmed = TrimWhitespace(body);
        if (trimmed.IsEmpty) return "{}";
        try
        {
            using var document = JsonDocument.Parse(trimmed.ToArray());
            var builder = new StringBuilder();
            Write(document.RootElement, builder);
            return builder.ToString();
        }
        catch (JsonException)
        {
            return "raw:" + Convert.ToHexStringLower(SHA256.HashData(body));
        }
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (!first) builder.Append(',');
                    first = false;
                    builder.Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    Write(property.Value, builder);
                }
                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!firstItem) builder.Append(',');
                    firstItem = false;
                    Write(item, builder);
                }
                builder.Append(']');
                break;
            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsWhitespace(value[start])) start++;
        while (end > start && IsWhitespace(value[end - 1])) end--;
        return value[start..end];
    }

    private static bool IsWhitespace(byte value) => value is 0x20 or 0x09 or 0x0D or 0x0A;
}
