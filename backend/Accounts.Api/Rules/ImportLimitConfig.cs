namespace Accounts.Api.Rules;

public class ImportLimitConfig
{
    public long MaxCsvBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxRows { get; set; } = 25_000;
    public string[] AllowedExtensions { get; set; } = [".csv", ".txt"];
    public string[] AllowedContentTypes { get; set; } =
    [
        "text/csv",
        "application/csv",
        "text/plain",
        "application/vnd.ms-excel",
        "application/octet-stream"
    ];
}
