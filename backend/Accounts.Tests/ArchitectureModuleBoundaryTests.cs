using Xunit;

namespace Accounts.Tests;

public class ArchitectureModuleBoundaryTests
{
    [Fact]
    public void ProductionReadinessImplementation_RemainsSplitIntoFocusedModules()
    {
        var services = Path.Combine(RepositoryRoot(), "backend", "Accounts.Api", "Services");
        var budgets = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ProductionReadinessReportService.cs"] = 1_200,
            ["ProductionReadinessContracts.cs"] = 800,
            ["ProductionReadinessStatutoryCatalog.cs"] = 1_200,
            ["ProductionReadinessReleaseCatalog.cs"] = 1_000,
            ["ProductionReadinessOperationsCatalog.cs"] = 650,
            ["ProductionReadinessAccountantVisualCatalog.cs"] = 950
        };

        foreach (var (fileName, maximumLines) in budgets)
        {
            var path = Path.Combine(services, fileName);
            Assert.True(File.Exists(path), $"Expected focused readiness module {fileName}.");
            var lineCount = File.ReadLines(path).Count();
            Assert.True(
                lineCount <= maximumLines,
                $"{fileName} has {lineCount} lines; keep it at or below {maximumLines} by extracting a domain catalog.");
        }
    }

    private static string RepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        foreach (var startPath in new[] { Directory.GetCurrentDirectory(), Path.GetDirectoryName(sourceFilePath) })
        {
            if (string.IsNullOrWhiteSpace(startPath))
                continue;

            var directory = new DirectoryInfo(startPath);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "compose.yml")))
                directory = directory.Parent;

            if (directory is not null)
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
